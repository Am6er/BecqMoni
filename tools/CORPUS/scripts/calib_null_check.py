# -*- coding: utf-8 -*-
u"""`V18`/`V19`: нулевое плечо и положительный контроль машинки калибровки.

Мерка ставит `corpus_calib.choose` (и, по ключу, весь `calibrate`) в два
управляемых опыта на КОПИЯХ сырых спектров стадии 1 (`_corpus_raw`), корпус
не трогая:

  * **нулевое плечо** — к найденным опорам добавляется ещё одна РОВНО в тот
    канал, куда её кладёт нынешний ответ машинки. Новых данных ноль по
    построению; нейтральная машинка обязана вернуть тот же ответ. Мера —
    max |ΔE| по всей шкале между ответом до и после, в кэВ и в долях ПШПВ, и
    сменился ли режим;
  * **положительный контроль** — та же опора, сдвинутая на +2 ПШПВ по каналу.
    Она ОБЯЗАНА что-то сдвинуть, иначе нулевое плечо «прошло» оттого, что
    выбор заморожен, а не оттого, что машинка нейтральна.

Опора ставится в трёх местах шкалы (ниже нижней опоры, между, выше верхней):
`B24`-запрет экстраполяции считает от нижней опоры, и точка НИЖЕ неё меняет
его область — это отдельный механизм, и его надо видеть отдельно.

Два уровня. `choose` — единица, которую винит `V18`: пары зафиксированы,
меняется только выбор. `--pipeline` — весь `calibrate` с опорой, подшитой к
каждому `match_lines`: здесь нейтральность по построению НЕ гарантирована
(опора, поставленная итоговым ответом, на промежуточных проходах невязку
имеет), и число это справочное.

Запуск (из любого каталога):

    python calib_null_check.py --raw=<копии _corpus_raw> --out=<каталог>
        [--mode=margin|ftest] [--alpha=0.05] [--scope=all|poly] [--order=2|3] [--tag=имя]
        [--only=k1,k2] [--calib=<другой corpus_calib.py>] [--pipeline]
        [--family]                       # `V19`: сводная таблица семьи G1S
    python calib_null_check.py --compare=A.json,B.json --out=<каталог>
                                         # режим до / после, max|ΔE| по спектрам

Артефакты в `--out`: `arms_<tag>.csv` (каждый опыт), `spectra_<tag>.csv`
(по спектру: худшее нулевое плечо, слабейший контроль), `calib_<tag>.json`
(ответ машинки по спектру — для `--compare`), `family_<tag>.csv`,
`modes_<A>_vs_<B>.csv`, `log_<tag>.txt`.
"""
import csv
import hashlib
import importlib.util
import io
import json
import os
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)


def _load_calib(path):
    u"""Подменить модуль `corpus_calib` файлом по пути — ДО импорта
    `build_corpus`, который его импортирует по имени."""
    spec = importlib.util.spec_from_file_location('corpus_calib', path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules['corpus_calib'] = mod
    spec.loader.exec_module(mod)
    return mod


def _sha(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest()[:12]


def _args(argv):
    a = dict(raw=os.path.join(HERE, '_corpus_raw'), out=None, mode='margin',
             alpha=0.05, order=2, tag=None, only=None, calib=None, scope='all',
             pipeline=False, family=False, compare=None)
    for x in argv:
        if x.startswith('--'):
            k, _, v = x[2:].partition('=')
            if k in ('pipeline', 'family'):
                a[k] = True
            elif k == 'alpha':
                a[k] = float(v)
            elif k == 'order':
                a[k] = int(v)
            elif k == 'only':
                a[k] = set(v.split(','))
            elif k == 'compare':
                a[k] = v.split(',')
            else:
                a[k] = v
    if a['out'] is None:
        raise SystemExit(u'нужен --out=<каталог>')
    if a['tag'] is None:
        a['tag'] = '%s_o%d%s' % (a['mode'], a['order'],
                                 '' if a['mode'] == 'margin' else '_a%g' % a['alpha'])
    return a


# ---------------------------------------------------------------------------
# опыты над `choose`
# ---------------------------------------------------------------------------
def max_delta(cal_a, cal_b, res_a):
    u"""max |E_a − E_b| по всей шкале — в кэВ, в долях ПШПВ, и канал максимума."""
    grid = np.arange(0, cal_a.nmax, dtype=float)
    d = np.abs(cal_a.energy(grid) - cal_b.energy(grid))
    e = np.maximum(np.abs(cal_b.energy(grid)), 5.0)
    fw = d / (res_a * np.sqrt(e))
    i = int(np.argmax(d))
    return float(d[i]), float(fw.max()), i


def synthetic_pair(cal, e_star, pairs, res_a, shift_fwhm=0.0):
    u"""Опора в канале, куда `cal` кладёт энергию `e_star`, со значимостью
    медианной по найденным. `shift_fwhm` сдвигает её по каналу на столько
    ПШПВ — положительный контроль."""
    ch = cal.channel(e_star)
    # `Ecal.channel` интерполирует по целой сетке, и E* в этом канале
    # воспроизводится не точно; нулевая невязка обязана быть нулевой ТОЧНО,
    # поэтому энергия опоры — та, что сама калибровка даёт в этом канале.
    e_ref = float(cal.energy(ch))
    dedch = max(abs(cal.dEdch(ch)), 1e-9)
    fwhm_ch = res_a * np.sqrt(max(e_ref, 5.0)) / dedch
    sig = float(np.median([a['sig'] for a in pairs])) if pairs else 30.0
    sig_fit = float(np.median([a.get('sig_fit', 0.0) for a in pairs])) if pairs else 0.0
    return dict(ch=ch + shift_fwhm * fwhm_ch, e_ref=e_ref, label='synthetic',
                purity=1.0, fwhm=fwhm_ch, sig=sig, area=0.0, sig_fit=sig_fit)


def placements(cal, pairs, nmax):
    u"""Где ставить опору: ниже нижней, между, выше верхней. Только там, где
    канал внутри рабочей шкалы (от 8 до n−8) и энергия не ниже 15 кэВ."""
    es = sorted(a['e_ref'] for a in pairs)
    e_lo, e_hi = es[0], es[-1]
    top = float(cal.energy(nmax - 8))
    cand = [('low', 0.5 * e_lo),
            ('mid', float(np.sqrt(e_lo * e_hi)) if e_hi > e_lo * 1.05 else 1.5 * e_lo),
            ('high', min(1.5 * e_hi, top))]
    out = []
    for name, e in cand:
        if e < 15.0 or e >= top:
            continue
        ch = cal.channel(e)
        if ch < 8 or ch > nmax - 8:
            continue
        # не класть синтетическую опору на живую: `match_lines` такие сливает
        if any(abs(ch - a['ch']) < 1.0 for a in pairs):
            continue
        out.append((name, float(e)))
    return out


#: «Ноль на уровне округления»: сдвиг шкалы меньше тысячной доли ПШПВ в любом
#: канале. Это не допуск, а шум арифметики: перефит кубики на 16384 каналах
#: (`ch³` ~ 4·10¹²) в `lstsq` воспроизводит те же коэффициенты с точностью
#: ~10⁻⁴ кэВ у HPGe (10⁻⁴ ПШПВ), а ни один потребитель шкалы тысячную долю
#: ширины пика не различает: сетка каналов грубее.
NEUTRAL_FWHM = 1e-3


def mechanism(trace_before, tag_after):
    u"""По следу `choose` ДО опыта — почему победивший ПОСЛЕ кандидат не
    победил раньше: `unlock` — его не было по счёту опор; `gate:<...>` — не
    построился или выбыл по запрету экстраполяции; `rank:ftest` — был, но не
    оплатил свободу; `rank:margin` — был, оценивался и проиграл по порогу."""
    rec = trace_before.get(tag_after.split('/')[0])
    if rec is None:
        return 'unlock'
    if rec['status'] == 'count':
        return 'unlock'
    if rec['status'] != 'ok':
        return 'gate:' + rec['status']
    if rec['paid'] is False:
        return 'rank:ftest'
    return 'rank:margin'


def choose_arms(cc, stored, pairs, res_a, nmax, force, level_tag):
    u"""Нулевое плечо и контроль на уровне `choose`. Возвращает базу и опыты."""
    tag_b, cal_b, _ = cc.choose(stored, pairs, res_a, nmax, force=force)
    trace_list = list(getattr(cc, 'LAST_CANDIDATES', []))
    trace_b = {r['tag']: r for r in trace_list}
    rows = []
    for place, e_star in placements(cal_b, pairs, nmax):
        for arm, shift in (('null', 0.0), ('shift', 2.0)):
            extra = synthetic_pair(cal_b, e_star, pairs, res_a, shift)
            tag_x, cal_x, _ = cc.choose(stored, pairs + [extra], res_a, nmax, force=force)
            d_kev, d_fwhm, i_max = max_delta(cal_x, cal_b, res_a)
            d_star = float(cal_x.energy(extra['ch']) - cal_b.energy(extra['ch']))
            if arm == 'null':
                if tag_x == tag_b and d_fwhm < NEUTRAL_FWHM:
                    cause = 'neutral'
                elif tag_x == tag_b:
                    cause = 'refit'           # тот же режим, другие коэффициенты
                else:
                    cause = mechanism(trace_b, tag_x)
            else:
                cause = 'reacted' if (tag_x != tag_b or d_fwhm > 0.01) else 'frozen'
            rows.append(dict(level=level_tag, arm=arm, place=place, e_star=round(e_star, 2),
                             ch_star=round(extra['ch'], 2), tag_before=tag_b, tag_after=tag_x,
                             d_max_kev=round(d_kev, 4), d_max_fwhm=round(d_fwhm, 4),
                             ch_max=i_max, d_at_star_kev=round(d_star, 4), cause=cause))
    choose_arms.last_trace = trace_list
    return tag_b, cal_b, rows


def pipeline_arms(cc, bc, sp, entry, cal0, res_a, pairs):
    u"""То же сквозь весь `calibrate`: опора подшивается к каждому `match_lines`."""
    rows = []
    orig = cc.match_lines
    for place, e_star in placements(cal0, pairs, sp.n):
        for arm, shift in (('null', 0.0), ('shift', 2.0)):
            extra = synthetic_pair(cal0, e_star, pairs, res_a, shift)

            def patched(counts, ecal, lines, res, **kw):
                found = orig(counts, ecal, lines, res, **kw)
                if not any(abs(a['ch'] - extra['ch']) < 1.0 for a in found):
                    found = sorted(found + [dict(extra)], key=lambda a: a['ch'])
                return found

            cc.match_lines = patched
            try:
                cal_x, pairs_x, r662_x, tag_x = bc.calibrate_one(sp, entry)
            finally:
                cc.match_lines = orig
            d_kev, d_fwhm, i_max = max_delta(cal_x, cal0, res_a)
            d_star = float(cal_x.energy(extra['ch']) - cal0.energy(extra['ch']))
            rows.append(dict(level='pipeline', arm=arm, place=place, e_star=round(e_star, 2),
                             ch_star=round(extra['ch'], 2), tag_before=None, tag_after=tag_x,
                             d_max_kev=round(d_kev, 4), d_max_fwhm=round(d_fwhm, 4),
                             ch_max=i_max, d_at_star_kev=round(d_star, 4),
                             cause=('neutral' if d_fwhm < NEUTRAL_FWHM else 'moved')
                             if arm == 'null'
                             else ('reacted' if d_fwhm > 0.01 else 'frozen')))
    return rows


# ---------------------------------------------------------------------------
# `V19`: семья G1S на одной поставочной шкале
# ---------------------------------------------------------------------------
AG_K_ROWS = None


def ag_k_line(bc, calibrate, res_fn):
    u"""Куда G1S кладёт K-серию серебра: центроид линий nucdb (`109CD`, тип X)
    при разрешении прибора — тем же `calibrate.blend`, что и курирование опор."""
    import sqlite3
    uri = 'file:' + bc.chains_db().replace('\\', '/') + '?mode=ro'
    c = sqlite3.connect(uri, uri=True)
    rows = c.execute("select energy_num, intensity_num from decay_radiations "
                     "where parent_nucid = '109CD' and type_a = 'X' and energy_num > 15 "
                     "and energy_num < 30 and intensity_num > 0.5").fetchall()
    c.close()
    rows = [(float(e), float(i), 'Ag K') for e, i in rows]
    strongest = max(rows, key=lambda r: r[1])[0]
    e_eff, purity = calibrate.blend(strongest, rows, res_fn(strongest))
    return float(e_eff), float(purity), rows


def family_table(cc, bc, calibrate, state, log):
    u"""Спектры G1S с побайтно общей поставочной калибровкой → одна таблица опор
    на семью (журнал 27.08.2026 брал семью `G1S16_Eu152_P5`; здесь — все
    семьи от трёх спектров, по убыванию размера)."""
    from gaussfit import fit_peak, FWHM_SIGMA
    fams = {}
    for key, st in state.items():
        if not st['entry']['det'].startswith('G1S'):
            continue
        fams.setdefault(tuple(np.round(st['sp'].ecal, 12)), []).append(key)
    out = []
    for fam in sorted(fams.values(), key=lambda f: -len(f)):
        if len(fam) < 3:
            continue
        out.extend(_one_family(cc, bc, calibrate, state, log, sorted(fam), fit_peak,
                               FWHM_SIGMA))
    return out, []


def _one_family(cc, bc, calibrate, state, log, fam, fit_peak, FWHM_SIGMA):
    fam_id = fam[0]
    log(u'')
    log(u'=== семья G1S на одной поставочной (%d): %s' % (len(fam), ', '.join(fam)))
    anchor = max(fam, key=lambda k: len(state[k]['pairs']))
    res_a = state[anchor]['res_a']
    sp = state[anchor]['sp']
    stored = cc.Ecal(sp.ecal, sp.n)
    table = []
    for key in fam:
        for a in state[key]['pairs']:
            table.append(dict(a, src=key))
    cd = [k for k in fam if 'Cd109' in k]
    ag = None
    if cd:
        e_ag, purity, rows = ag_k_line(bc, calibrate, lambda e: res_a * np.sqrt(max(e, 5.0)))
        csp = state[cd[0]]['sp']
        ch_pred = stored.channel(e_ag)
        # Искать пик там, где он ЕСТЬ, а не там, куда его кладёт поставочная:
        # у G1S она промахивается на 3 канала (`B25`), и фит в предсказанном
        # окне садится на склон (ширина 0.9 канала, значимость 8). Вершина —
        # самый высокий канал ниже 40 кэВ по поставочной, выше порога отсечки.
        lo, hi = 5, int(stored.channel(40.0))
        ch0 = float(lo + int(np.argmax(csp.counts[lo:hi])))
        fwhm_ch = res_a * np.sqrt(e_ag) / max(abs(stored.dEdch(ch0)), 1e-9)
        r = fit_peak(csp.counts, ch0, fwhm_ch / FWHM_SIGMA, window=2.2)
        log(u'K-серия Ag (%s): центроид %.3f кэВ (чистота %.2f), поставочная кладёт в '
            u'канал %.2f, вершина в канале %.0f, фит: %s' % (cd[0], e_ag, purity, ch_pred, ch0,
                                     'mu=%.2f fwhm=%.2f sig=%.1f' % (r['mu'], r['fwhm'], r['sig'])
                                     if r else u'ОТКАЗ'))
        if r:
            ag = dict(ch=r['mu'], e_ref=e_ag, label='Ag K', purity=purity, fwhm=r['fwhm'],
                      sig=r['sig'], area=r['area'], sig_fit=float(r.get('sig_fit', 0.0)),
                      src=cd[0])
            table.append(ag)
    table.sort(key=lambda a: a['ch'])
    merged = []
    for a in table:
        if merged and abs(a['e_ref'] - merged[-1]['e_ref']) < 0.5:
            if a['sig'] > merged[-1]['sig']:
                merged[-1] = a
            continue
        merged.append(a)
    table = merged
    log(u'сводная таблица: %d опор, %.1f…%.1f кэВ' % (len(table), table[0]['e_ref'],
                                                      table[-1]['e_ref']))
    out = []
    low = [a for a in table if a['e_ref'] < 130.0]

    def miss(cal, a):
        fw = res_a * np.sqrt(max(a['e_ref'], 5.0))
        return float(cal.energy(a['ch']) - a['e_ref']) / fw

    def report(tag, cal, subset, extra=u''):
        for a in subset:
            out.append(dict(family=fam_id, fit=tag, n=len(subset), line=a['label'],
                            src=a['src'], e_ref=round(a['e_ref'], 3), ch=round(a['ch'], 2),
                            miss_kev=round(float(cal.energy(a['ch']) - a['e_ref']), 3),
                            miss_fwhm=round(miss(cal, a), 3)))
        log(u'  %-14s rms %.3f ПШПВ, худший |промах| %.2f; низ: %s%s' % (
            tag, cc.residual_fwhm(cal, subset, res_a),
            max(abs(miss(cal, a)) for a in subset),
            u' '.join(u'%s@%.0f:%+.2f' % (a['label'][:6], a['e_ref'], miss(cal, a))
                      for a in low if a in subset), extra))

    report('stored', stored, table)
    cals = {}
    for order in (1, 2, 3):
        cal = cc.poly_of(table, order, sp.n)
        if cal is None:
            log(u'  poly%d: кандидата нет (монотонность/изгиб)' % order)
            continue
        cals[order] = cal
        report('poly%d' % order, cal, table,
               u'  [SS %.4f, ν %d]' % (cc.residual_ss(cal, table, res_a), len(table) - order - 1))
    if 2 in cals and 3 in cals:
        ss2, ss3 = cc.residual_ss(cals[2], table, res_a), cc.residual_ss(cals[3], table, res_a)
        nu = len(table) - 4
        F = (ss2 - ss3) / (ss3 / nu)
        log(u'  F(poly3 против poly2) = %.2f при ν = %d; порог α=0.05: %.2f, α=0.10: %.2f, '
            u'α=0.25: %.2f' % (F, nu, cc.f_crit(1, nu, 0.05), cc.f_crit(1, nu, 0.10),
                              cc.f_crit(1, nu, 0.25)))
    tag_b, cal_b, rows = choose_arms(cc, stored, table, res_a, sp.n, False, 'family')
    trace = choose_arms.last_trace          # след БАЗОВОГО выбора, не последнего опыта
    report('choose:' + tag_b, cal_b, table)
    log(u'  choose (%s, степень %d): %s; след: %s' % (
        getattr(cc, 'MODEL_TEST', 'margin'), getattr(cc, 'MAX_ORDER', 2), tag_b,
        u'; '.join(u'%s=%s%s' % (r['tag'], r['status'],
                                 u'' if r['score'] is None else u'(%.3f%s)' % (
                                     r['score'], u'' if r['paid'] is None else
                                     u', paid' if r['paid'] else u', unpaid'))
                   for r in trace)))
    for r in rows:
        log(u'   %-5s %-4s E*=%7.1f  → %-14s max|ΔE| %8.4f кэВ = %.4f ПШПВ  [%s]' % (
            r['arm'], r['place'], r['e_star'], r['tag_after'], r['d_max_kev'],
            r['d_max_fwhm'], r['cause']))
    three = sorted(table, key=lambda a: -a['sig'])[:3]
    tag3, _, _ = cc.choose(stored, three, res_a, sp.n)
    five = sorted(table, key=lambda a: -a['sig'])[:5]
    tag5, _, _ = cc.choose(stored, five, res_a, sp.n)
    log(u'  контроль V19: по трём сильнейшим опорам — %s; по пяти — %s' % (tag3, tag5))
    return out


# ---------------------------------------------------------------------------
def compare(paths, out_dir):
    u"""`--compare=A.json,B.json`: режим до/после и max|ΔE| между ответами."""
    a_path, b_path = paths
    A = json.load(io.open(a_path, encoding='utf-8'))
    B = json.load(io.open(b_path, encoding='utf-8'))
    corpus = {}
    summ = os.path.join(os.path.dirname(HERE), 'corpus', 'summary.csv')
    if os.path.isfile(summ):
        with io.open(summ, encoding='utf-8-sig') as f:
            for r in csv.DictReader(f):
                corpus[r[u'спектр']] = r[u'калибровка']
    name_a = os.path.basename(a_path)[6:-5]
    name_b = os.path.basename(b_path)[6:-5]
    rows = []
    changed, moved = 0, 0
    for key in sorted(A):
        if key not in B:
            continue
        ra, rb = A[key], B[key]
        nmax = ra['nmax']
        grid = np.arange(0, nmax, dtype=float)
        ea = sum(c * grid ** i for i, c in enumerate(ra['coef']))
        eb = sum(c * grid ** i for i, c in enumerate(rb['coef']))
        d = np.abs(ea - eb)
        res_a = ra['res_a']
        fw = d / (res_a * np.sqrt(np.maximum(np.abs(ea), 5.0)))
        i = int(np.argmax(d))
        lo = min(ra['e_lo'], rb['e_lo']) if ra['n'] and rb['n'] else 0.0
        below = fw[ea < lo].max() if ra['n'] and np.any(ea < lo) else 0.0
        rows.append(dict(spectrum=key, det=ra['det'], n=ra['n'], corpus_mode=corpus.get(key, ''),
                         mode_a=ra['tag'], mode_b=rb['tag'], d_max_kev=round(float(d[i]), 3),
                         d_max_fwhm=round(float(fw.max()), 3), ch_max=i,
                         e_at_max=round(float(ea[i]), 1),
                         d_below_lowest_fwhm=round(float(below), 3),
                         e_lo=round(ra['e_lo'], 1), e_hi=round(ra['e_hi'], 1)))
        changed += ra['tag'] != rb['tag']
        moved += d[i] > 1e-6
    path = os.path.join(out_dir, 'modes_%s_vs_%s.csv' % (name_a, name_b))
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    print(u'%s: спектров %d, режим сменили %d, шкала сдвинулась (>1e-6 кэВ) у %d → %s'
          % (name_b, len(rows), changed, moved, path))
    return rows


def main(argv):
    a = _args(argv)
    if a['compare']:
        return compare(a['compare'], a['out'])
    os.makedirs(a['out'], exist_ok=True)
    tag = a['tag']
    log_path = os.path.join(a['out'], 'log_%s.txt' % tag)
    log_f = io.open(log_path, 'w', encoding='utf-8')

    def log(s):
        print(s)
        log_f.write(s + u'\n')
        log_f.flush()

    cc = _load_calib(a['calib']) if a['calib'] else __import__('corpus_calib')
    import build_corpus as bc
    import calibrate
    import corpus_def
    from spectrum import Spectrum

    for name in ('MODEL_TEST', 'MODEL_ALPHA', 'MAX_ORDER', 'MODEL_TEST_SCOPE'):
        val = {'MODEL_TEST': a['mode'], 'MODEL_ALPHA': a['alpha'], 'MAX_ORDER': a['order'],
               'MODEL_TEST_SCOPE': a['scope']}[name]
        if hasattr(cc, name):
            setattr(cc, name, val)
        elif not (a['mode'] == 'margin' and a['order'] == 2):
            raise SystemExit(u'этот corpus_calib не знает %s — только margin/o2' % name)
    log(u'corpus_calib: %s (sha %s); build_corpus sha %s; режим %s, α=%g, степень %d, '
        u'плата с %s' % (cc.__file__, _sha(cc.__file__), _sha(bc.__file__), a['mode'],
                        a['alpha'], a['order'], a['scope']))
    log(u'сырые копии: %s' % a['raw'])

    entries = [e for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON
               if a['only'] is None or e['key'] in a['only']]
    log(u'спектров стадии 1: %d (семёрка LEGACY копируется побайтно и стадию 1 не проходит)'
        % len(entries))

    arms, per_spec, calib_dump, state = [], [], {}, {}
    t0 = time.time()
    for e in entries:
        key = e['key']
        raw = os.path.join(a['raw'], key + '.xml')
        if not os.path.isfile(raw):
            log(u'%-22s НЕТ КОПИИ %s' % (key, raw))
            continue
        sp = Spectrum(raw)
        cal0, pairs0, r662, tag0 = bc.calibrate_one(sp, e)
        res_a = r662 * np.sqrt(662.0)
        stored = cc.Ecal(sp.ecal, sp.n)
        force = bool(e.get('recal'))
        n = len(pairs0)
        es = sorted(x['e_ref'] for x in pairs0)
        calib_dump[key] = dict(det=e['det'], nmax=sp.n, coef=[float(c) for c in cal0.coef],
                               tag=tag0, n=n, res_a=res_a,
                               e_lo=es[0] if es else 0.0, e_hi=es[-1] if es else 0.0,
                               pairs=[dict(ch=round(x['ch'], 3), e=round(x['e_ref'], 3),
                                           sig=round(x['sig'], 1)) for x in pairs0])
        state[key] = dict(entry=e, sp=sp, pairs=pairs0, res_a=res_a, cal=cal0, tag=tag0)
        rec = dict(spectrum=key, det=e['det'], n=n, tag_pipeline=tag0, tag_choose='',
                   null_worst_kev=0.0, null_worst_fwhm=0.0, null_worst_cause='n/a',
                   shift_weakest_fwhm=None, shift_weakest_cause='n/a',
                   pipe_null_worst_kev=None, pipe_null_worst_fwhm=None,
                   pipe_shift_weakest_fwhm=None, trace='')
        if n == 0:
            log(u'%-22s опор нет — опытов нет (%s)' % (key, tag0))
            per_spec.append(rec)
            continue
        tag_b, cal_b, rows = choose_arms(cc, stored, pairs0, res_a, sp.n, force, 'choose')
        rec['tag_choose'] = tag_b
        rec['trace'] = u'; '.join(
            u'%s=%s%s' % (r['tag'], r['status'],
                          u'' if r['score'] is None else u'(%.3f%s)' % (
                              r['score'], u'' if r['paid'] is None else
                              u',paid' if r['paid'] else u',unpaid'))
            for r in choose_arms.last_trace)
        if a['pipeline']:
            rows += pipeline_arms(cc, bc, sp, e, cal0, res_a, pairs0)
        for r in rows:
            r.update(spectrum=key, det=e['det'], n=n)
            if r['level'] != 'pipeline':
                r['tag_before'] = tag_b
        arms.extend(rows)
        nulls = [r for r in rows if r['arm'] == 'null' and r['level'] == 'choose']
        shifts = [r for r in rows if r['arm'] == 'shift' and r['level'] == 'choose']
        if nulls:
            w = max(nulls, key=lambda r: r['d_max_kev'])
            w = max(nulls, key=lambda r: r['d_max_fwhm'])
            rec.update(null_worst_kev=w['d_max_kev'], null_worst_fwhm=w['d_max_fwhm'],
                       null_worst_cause=w['cause'])
        if shifts:
            w = min(shifts, key=lambda r: r['d_max_fwhm'])
            rec.update(shift_weakest_fwhm=w['d_max_fwhm'], shift_weakest_cause=w['cause'])
        pn = [r for r in rows if r['arm'] == 'null' and r['level'] == 'pipeline']
        ps = [r for r in rows if r['arm'] == 'shift' and r['level'] == 'pipeline']
        if pn:
            w = max(pn, key=lambda r: r['d_max_kev'])
            rec.update(pipe_null_worst_kev=w['d_max_kev'], pipe_null_worst_fwhm=w['d_max_fwhm'])
        if ps:
            rec['pipe_shift_weakest_fwhm'] = min(r['d_max_fwhm'] for r in ps)
        per_spec.append(rec)
        log(u'%-22s n=%2d %-16s choose:%-14s ноль: %8.4f кэВ %-8s  контроль: %s'
            % (key, n, tag0, tag_b, rec['null_worst_kev'], rec['null_worst_cause'],
               '%.3f ПШПВ %s' % (rec['shift_weakest_fwhm'], rec['shift_weakest_cause'])
               if rec['shift_weakest_fwhm'] is not None else u'нет места'))
    log(u'время: %.0f с' % (time.time() - t0))

    with io.open(os.path.join(a['out'], 'arms_%s.csv' % tag), 'w', encoding='utf-8',
                 newline='') as f:
        cols = ['spectrum', 'det', 'n', 'level', 'arm', 'place', 'e_star', 'ch_star',
                'tag_before', 'tag_after', 'd_max_kev', 'd_max_fwhm', 'ch_max',
                'd_at_star_kev', 'cause']
        w = csv.DictWriter(f, fieldnames=cols)
        w.writeheader()
        w.writerows(arms)
    with io.open(os.path.join(a['out'], 'spectra_%s.csv' % tag), 'w', encoding='utf-8',
                 newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(per_spec[0].keys()))
        w.writeheader()
        w.writerows(per_spec)
    with io.open(os.path.join(a['out'], 'calib_%s.json' % tag), 'w', encoding='utf-8') as f:
        json.dump(calib_dump, f, ensure_ascii=False, indent=1)

    # сводка
    tested = [r for r in per_spec if r['n'] > 0 and r['null_worst_cause'] != 'n/a']
    neutral = [r for r in tested if r['null_worst_cause'] == 'neutral']
    log(u'')
    log(u'НУЛЕВОЕ ПЛЕЧО (choose): спектров с опорами %d, нейтральных %d, сдвинулись %d'
        % (len(tested), len(neutral), len(tested) - len(neutral)))
    from collections import Counter
    log(u'   причины: %s' % dict(Counter(r['null_worst_cause'] for r in tested)))
    if tested:
        log(u'   худший сдвиг: %.4f кэВ (%s)' % (
            max(r['null_worst_kev'] for r in tested),
            max(tested, key=lambda r: r['null_worst_kev'])['spectrum']))
    ctl = [r for r in per_spec if r['shift_weakest_fwhm'] is not None]
    log(u'ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (choose): проверено %d, отреагировали %d, заморожены %d'
        % (len(ctl), sum(r['shift_weakest_cause'] == 'reacted' for r in ctl),
           sum(r['shift_weakest_cause'] == 'frozen' for r in ctl)))
    if a['pipeline']:
        pt = [r for r in per_spec if r['pipe_null_worst_kev'] is not None]
        log(u'НУЛЕВОЕ ПЛЕЧО (pipeline): проверено %d, нейтральных %d, худший %.3f кэВ'
            % (len(pt), sum(r['pipe_null_worst_fwhm'] < NEUTRAL_FWHM for r in pt),
               max([r['pipe_null_worst_kev'] for r in pt] or [0.0])))
    if a['family']:
        fam_rows, _ = family_table(cc, bc, calibrate, state, log)
        with io.open(os.path.join(a['out'], 'family_%s.csv' % tag), 'w', encoding='utf-8',
                     newline='') as f:
            w = csv.DictWriter(f, fieldnames=list(fam_rows[0].keys()))
            w.writeheader()
            w.writerows(fam_rows)
    log_f.close()


if __name__ == '__main__':
    main(sys.argv[1:])
