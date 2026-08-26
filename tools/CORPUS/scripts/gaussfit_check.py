# -*- coding: utf-8 -*-
u"""`V13`: чего стоит починка демпфера в `gaussfit` — измерение, а не рассуждение.

Меряется СТАДИЯ 1 конвейера корпуса на своих копиях (`scripts/_corpus_raw`),
хранившейся калибровкой и моделью разрешения ГРУППЫ из `corpus/detectors.csv`,
допусками первого прохода `corpus_calib.calibrate` (2.5 ПШПВ, ширина 0.35…2.6).
Полного корпусного прогона FSA тут нет и не нужно: вопрос — какие ЛИНИИ находит
сопоставитель, а он живёт целиком в `corpus_calib.match_lines`.

⛔ Мерка обязана проверять не только «вернулось больше», но и «вернулось
настоящее». Поэтому считается три вещи:

1. **Что нашлось** — старым фитом (`--ref=` файл прежнего `gaussfit`) и новым,
   поимённо, с разбивкой по частям корпуса (`corpus/parts.csv`).
2. **Настоящая ли линия** — независимая от фита проверка бугра (`bump_ok`):
   в САМИХ отсчётах, сглаженных тройкой, обязана быть ВНУТРЕННЯЯ вершина,
   поднятая на 3√N и над прямой по краям окна, и над соседями по ОБЕ стороны.
   Гаусс тут не участвует вовсе, поэтому проверка не может «подтвердить» саму
   себя. ⚖ Мерка откалибрована по ШТАТНОМУ набору: из 473 линий, которые
   находит и прежний фит (понятная часть, 81 спектр), вершину имеют 337 —
   71.2 %; у непонятной части 69.9 %. Это и есть уровень, с которым сравнивают
   долю у вернувшихся.
3. **Не выросли ли фантомы** — отрицательный контроль: тем же кодом ищутся
   линии, СДВИНУТЫЕ на ±5 ПШПВ от настоящих, то есть заведомо стоящие не там.
   Всё найденное там — ложь по построению, и сравнение старого фита с новым
   говорит, не начал ли фит лепить пики из континуума.

Запуск (ничего не меняет, ничего не пересобирает):

    python tools/CORPUS/scripts/gaussfit_check.py [--only=KEY,KEY] [--csv=файл]
                                                  [--ref=прежний_gaussfit.py]
"""
import os
import sys
import csv
import io
import importlib.util

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import corpus_def                                     # noqa: E402
import corpus_calib                                   # noqa: E402
import calibrate                                      # noqa: E402
import build_corpus                                   # noqa: E402
import gaussfit                                       # noqa: E402
from spectrum import Spectrum                         # noqa: E402

RAW = os.path.join(HERE, '_corpus_raw')
PARTS = os.path.join(HERE, '..', 'corpus', 'parts.csv')
DETECTORS = os.path.join(HERE, '..', 'corpus', 'detectors.csv')

#: допуски первого прохода `corpus_calib.calibrate`
TOL, WLO, WHI = 2.5, 0.35, 2.6
#: на сколько ПШПВ отодвигается линия в отрицательном контроле
FAKE_OFFSET = 5.0


def load_ref(path):
    u"""Прежний `gaussfit` отдельным модулем — для сравнения бок о бок."""
    spec = importlib.util.spec_from_file_location('gaussfit_ref', path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def det_res_a():
    u"""a модели разрешения группы: FWHM(E) = a·√E, a = ПШПВ(662)/√662."""
    out = {}
    with io.open(DETECTORS, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            out[row['det']] = (float(row['fwhm_662_pct']) / 100.0 * 662.0
                               / np.sqrt(662.0))
    return out


def parts_of():
    out = {}
    if os.path.isfile(PARTS):
        with io.open(PARTS, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out[row['spectrum']] = row['part']
    return out


# ---------------------------------------------------------------------------
# независимая проверка «бугор есть»
# ---------------------------------------------------------------------------
def bump_ok(counts, ch0, sigma0, mu, window=2.2):
    u"""Есть ли в окне настоящий пик — БЕЗ всякого гаусса.

    ⛔ Первый вариант проверки (максимум ОСТАТКА над прямой по краям) годился
    не полностью и был пойман на `G1S24_Y88_P5` 198.1 кэВ: там отсчёты растут
    поперёк всего окна МОНОТОННО (14672 → 25019, ни одной вершины), а остаток
    над прямой всё равно выпуклый — континуум просто изогнут, — и проверка
    говорила «да». Гаусс на такой изгиб садится шириной во всё окно
    (ПШПВ 23.1 канала при модельных 9.1) и получает z около тысячи.

    Поэтому требуется ВЕРШИНА В САМИХ ОТСЧЁТАХ: сглаженные тройкой отсчёты
    обязаны иметь ВНУТРЕННИЙ максимум, значимо (3√N) поднятый и над прямой по
    краям, и над своими соседями по ОБЕ стороны, а подогнанный центр — стоять
    от этой вершины не дальше модельной ПШПВ.

    Возвращает (годится, высота над прямой, порог 3√N).
    """
    n = len(counts)
    half = max(4, int(round(window * sigma0)))
    lo = int(max(0, round(ch0 - half)))
    hi = int(min(n - 1, round(ch0 + half)))
    if hi - lo + 1 < 8:
        return False, 0.0, 0.0
    x = np.arange(lo, hi + 1, dtype=float)
    y = np.asarray(counts[lo:hi + 1], dtype=float)
    ys = np.convolve(y, np.ones(3) / 3.0, mode='same')
    ys[0], ys[-1] = y[0], y[-1]
    edge = max(2, x.size // 6)
    xe = np.concatenate([x[:edge], x[-edge:]])
    ye = np.concatenate([y[:edge], y[-edge:]])
    try:
        b1, b0 = np.polyfit(xe, ye, 1)
    except Exception:
        b1, b0 = 0.0, float(np.median(ye))
    k = int(np.argmax(ys))
    top = float(y[k] - (b0 + b1 * x[k]))
    need = 3.0 * np.sqrt(max(y[k], 1.0))
    if k < 1 or k > x.size - 2:
        return False, top, need          # вершины нет — окно на склоне
    if top < need:
        return False, top, need          # над континуумом ничего не поднялось
    drop = min(float(ys[k] - ys[:k].min()), float(ys[k] - ys[k + 1:].min()))
    if drop < need:
        return False, top, need          # спада по обе стороны нет — это ступень
    if abs(mu - x[k]) > sigma0 * gaussfit.FWHM_SIGMA:
        return False, top, need          # фит сел не на эту вершину
    return True, top, need


# ---------------------------------------------------------------------------
# один спектр
# ---------------------------------------------------------------------------
def lines_of(entry, sp, res_a):
    ent = dict(entry)
    ent['wanted'] = build_corpus.wanted_lines(entry)
    calibrate.sample_lines = build_corpus.sample_lines
    return calibrate.curate(ent, lambda e: res_a * np.sqrt(max(float(e), 5.0)),
                            min_purity=0.45)


def run_match(fitter, counts, cal, lines, res_a):
    u"""`match_lines` тем или иным фитом. -> (найденное, разбеги, упоры в σ)."""
    old_ex, old_pk = corpus_calib.fit_peak_ex, corpus_calib.fit_peak
    old_gf = corpus_calib.gaussfit
    corpus_calib.fit_peak_ex = fitter['peak_ex']
    corpus_calib.fit_peak = fitter['peak']
    corpus_calib.gaussfit = fitter['mod']
    try:
        found = corpus_calib.match_lines(counts, cal, lines, res_a,
                                         tol_fwhm=TOL, width_lo=WLO, width_hi=WHI)
        noconv = list(corpus_calib.LAST_NOCONV)
        bound = list(corpus_calib.LAST_BOUND)
    finally:
        corpus_calib.fit_peak_ex, corpus_calib.fit_peak = old_ex, old_pk
        corpus_calib.gaussfit = old_gf
    return found, noconv, bound


def fake_lines(lines, res_a, sign):
    u"""Те же линии, отодвинутые на ±5 ПШПВ — отрицательный контроль."""
    out = []
    for e_ref, label, purity, e_table in lines:
        fw = res_a * np.sqrt(max(e_ref, 5.0))
        e2 = e_ref + sign * FAKE_OFFSET * fw
        if e2 < 20.0:
            continue
        out.append((e2, label + '/ЛОЖЬ', purity, e2))
    return out


def main():
    only = None
    csv_out = None
    ref_path = os.path.join(HERE, 'gaussfit_ref.py')
    for a in sys.argv[1:]:
        if a.startswith('--only='):
            only = set(a.split('=', 1)[1].split(','))
        elif a.startswith('--csv='):
            csv_out = a.split('=', 1)[1]
        elif a.startswith('--ref='):
            ref_path = a.split('=', 1)[1]
        elif a.startswith('--shi='):
            gaussfit.SIGMA_HI_FRAC = float(a.split('=', 1)[1])
        elif a.startswith('--slo='):
            gaussfit.SIGMA_LO_FRAC = float(a.split('=', 1)[1])
    if not os.path.isfile(ref_path):
        print(u'нет прежнего gaussfit: %s' % ref_path)
        print(u'достать: git show HEAD~1:tools/CORPUS/scripts/gaussfit.py > %s'
              % ref_path)
        return 2
    ref = load_ref(ref_path)

    def ref_peak_ex(counts, mu0, sigma0, window=2.6, nmin=8):
        return ref.fit_peak(counts, mu0, sigma0, window=window, nmin=nmin), 'ok'

    fitters = {
        # mod=gaussfit и у старого: оттуда берутся только имена исходов, а
        # `ref_peak_ex` всегда отдаёт 'ok' — прежний фит их не различал вовсе.
        'старый': dict(mod=gaussfit, peak=ref.fit_peak, peak_ex=ref_peak_ex),
        'новый': dict(mod=gaussfit, peak=gaussfit.fit_peak,
                      peak_ex=gaussfit.fit_peak_ex),
    }

    res_by_det = det_res_a()
    part_of = parts_of()
    rows = []
    detail = []
    for e in corpus_def.ALL:
        key = e['key']
        if only and key not in only:
            continue
        raw = os.path.join(RAW, key + '.xml')
        if not os.path.isfile(raw):
            print(u'%-24s НЕТ своей копии в _corpus_raw' % key)
            continue
        res_a = res_by_det.get(e['det'])
        if res_a is None:
            print(u'%-24s НЕТ группы %s в detectors.csv' % (key, e['det']))
            continue
        try:
            sp = Spectrum(raw)
        except Exception as ex:
            print(u'%-24s ОШИБКА чтения: %s' % (key, ex))
            continue
        cal = corpus_calib.Ecal(sp.ecal, sp.n)
        lines = lines_of(e, sp, res_a)
        if not lines:
            continue

        got = {}
        nocv = {}
        for name, f in fitters.items():
            found, noconv, bnd = run_match(f, sp.counts, cal, lines, res_a)
            got[name] = dict((round(a['e_ref'], 3), a) for a in found)
            nocv[name] = (noconv, bnd)

        fake = dict((nm, [0, 0]) for nm in fitters)
        for sign in (+1.0, -1.0):
            fl = fake_lines(lines, res_a, sign)
            if not fl:
                continue
            for nm, f in fitters.items():
                for a in run_match(f, sp.counts, cal, fl, res_a)[0]:
                    fake[nm][0] += 1
                    ch0 = cal.channel(a['e_ref'])
                    dedch = abs(cal.dEdch(ch0))
                    fw = max(res_a * np.sqrt(max(a['e_ref'], 5.0))
                             / max(dedch, 1e-9), 1.2)
                    if bump_ok(sp.counts, ch0, fw / gaussfit.FWHM_SIGMA, a['ch'])[0]:
                        fake[nm][1] += 1
        fake_old, fake_old_bump = fake['старый']
        fake_new, fake_new_bump = fake['новый']

        only_new = sorted(set(got['новый']) - set(got['старый']))
        only_old = sorted(set(got['старый']) - set(got['новый']))
        moved = 0
        for k in set(got['новый']) & set(got['старый']):
            a, b = got['новый'][k], got['старый'][k]
            if abs(a['ch'] - b['ch']) > 0.05 * max(b['fwhm'], 1e-6):
                moved += 1

        real = 0
        for k in only_new:
            a = got['новый'][k]
            ch0 = cal.channel(a['e_ref'])
            dedch = abs(cal.dEdch(ch0))
            fwhm_ch = max(res_a * np.sqrt(max(a['e_ref'], 5.0)) / max(dedch, 1e-9), 1.2)
            ok, top, need = bump_ok(sp.counts, ch0, fwhm_ch / gaussfit.FWHM_SIGMA,
                                    a['ch'])
            real += 1 if ok else 0
            detail.append(dict(key=key, det=e['det'], part=part_of.get(key, '?'),
                               e_ref=a['e_ref'], label=a['label'], ch=a['ch'],
                               sig=a['sig'], sig_fit=a.get('sig_fit', 0.0),
                               fwhm=a['fwhm'], fwhm_model=fwhm_ch,
                               bump='да' if ok else 'НЕТ', top=top, need=need))

        rows.append(dict(key=key, det=e['det'], part=part_of.get(key, '?'),
                         old=len(got['старый']), new=len(got['новый']),
                         gain=len(only_new), lost=len(only_old), moved=moved,
                         real=real, noconv_old=len(nocv['старый'][0]),
                         noconv_new=len(nocv['новый'][0]),
                         bound_new=len(nocv['новый'][1]),
                         fake_old=fake_old, fake_new=fake_new,
                         fake_old_bump=fake_old_bump,
                         fake_new_bump=fake_new_bump))

    # ---- сводка ----------------------------------------------------------
    print(u'')
    print(u'%-22s %-9s %-8s %5s %5s %6s %6s %6s %6s %7s %7s' % (
        u'спектр', u'группа', u'часть', u'стар', u'нов', u'+нов', u'-пот',
        u'сдвиг', u'бугор', u'ложь_с', u'ложь_н'))
    for r in sorted(rows, key=lambda a: -a['gain']):
        if r['gain'] or r['lost'] or r['fake_new'] != r['fake_old']:
            print(u'%-22s %-9s %-8s %5d %5d %6d %6d %6d %6d %7d %7d' % (
                r['key'], r['det'], r['part'], r['old'], r['new'], r['gain'],
                r['lost'], r['moved'], r['real'], r['fake_old'], r['fake_new']))

    print(u'')
    print(u'%-10s %6s %6s %6s %6s %6s %6s %8s %8s %8s' % (
        u'часть', u'спектр', u'стар', u'нов', u'+нов', u'бугор', u'-пот',
        u'спектр+', u'ложь_с', u'ложь_н'))
    for part in ('known', 'unknown', 'excluded', '?'):
        sub = [r for r in rows if r['part'] == part]
        if not sub:
            continue
        print(u'%-10s %6d %6d %6d %6d %6d %6d %8d %8d %8d' % (
            part, len(sub), sum(r['old'] for r in sub), sum(r['new'] for r in sub),
            sum(r['gain'] for r in sub), sum(r['real'] for r in sub),
            sum(r['lost'] for r in sub),
            sum(1 for r in sub if r['gain']),
            sum(r['fake_old'] for r in sub), sum(r['fake_new'] for r in sub)))
        print(u'%-10s %6s %6s %6s %6s %6s %6s %8s %8d %8d  <- из них с бугром'
              % ('', '', '', '', '', '', '', '',
                 sum(r['fake_old_bump'] for r in sub),
                 sum(r['fake_new_bump'] for r in sub)))
    print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ — числа разных моделей')

    print(u'')
    print(u'предел σ: %.2f…%.2f от затравки' % (gaussfit.SIGMA_LO_FRAC,
                                                gaussfit.SIGMA_HI_FRAC))
    print(u'исходы фита за прогон (новый): %s' % gaussfit.STATS)
    print(u'РАЗБЕГ (`LAST_NOCONV`): старый фит не различал его вовсе; новый — %d '
          u'линий по корпусу. Упор в предел σ (`LAST_BOUND`): %d — это в основном '
          u'«линии нет», сторожем не служит.'
          % (sum(r['noconv_new'] for r in rows), sum(r['bound_new'] for r in rows)))

    if detail:
        print(u'')
        print(u'%-22s %8s %-22s %8s %7s %7s %6s' % (
            u'спектр', u'E, кэВ', u'линия', u'z', u'ПШПВ', u'модель', u'бугор'))
        for d in sorted(detail, key=lambda a: -a['sig'])[:60]:
            print(u'%-22s %8.2f %-22s %8.1f %7.2f %7.2f %6s' % (
                d['key'], d['e_ref'], d['label'][:22], d['sig'], d['fwhm'],
                d['fwhm_model'], d['bump']))

    if csv_out:
        with io.open(csv_out, 'w', encoding='utf-8', newline='') as f:
            w = csv.writer(f)
            w.writerow(['spectrum', 'det', 'part', 'e_ref', 'label', 'ch', 'sig',
                        'sig_fit', 'fwhm', 'fwhm_model', 'bump', 'top', 'need'])
            for d in detail:
                w.writerow([d['key'], d['det'], d['part'], '%.3f' % d['e_ref'],
                            d['label'], '%.3f' % d['ch'], '%.2f' % d['sig'],
                            '%.2f' % d['sig_fit'],
                            '%.3f' % d['fwhm'], '%.3f' % d['fwhm_model'],
                            d['bump'], '%.1f' % d['top'], '%.1f' % d['need']])
        print(u'\nподробности: %s' % csv_out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
