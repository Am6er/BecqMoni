# -*- coding: utf-8 -*-
u"""`V12`: НЕЗАВИСИМАЯ перепроверка приёмки второго прохода калибровки.

Проверяются ровно два утверждения строки `V12`, и оба на числах, а не
рассуждением:

**A. «Прежнее сравнение шло по РАЗНЫМ наборам линий».** Каждое решение стадии 2а
записывается вместе с наборами, по которым посчитаны `before` и `after`. Для
режима `legacy` печатается, у скольких решений наборы разошлись, сколько из них
`legacy` ПРИНЯЛ и у скольких принятых новый набор потерял САМУЮ НИЖНЮЮ опору —
то есть подгонка выиграла выбрасыванием. Для `union` печатается, что наборы
совпали у ВСЕХ решений: сравниваются множества энергий, а не берётся на веру код.

**B. Мерка, которая ВИДИТ линию, потерянную фитом.** Проект дважды слепнул на
том, что вариант, не нашедший линию, получал ноль промаха. `ecal_compare.py`
лечит это согласным каналом по слепкам вариантов, но и он молчит, если линию
потеряли ВСЕ варианты — а это ровно дефект `V13` (`gaussfit.fit_gauss`
расходится на сильных узких пиках). Поэтому здесь канал линии ищется **без
`gaussfit` вообще**: окно — объединение предсказаний обеих кривых, внутри него
континуум снимается прямой по краям, положение берётся центроидом ядра, а
значимость — по чистому счёту. Линия, найденная так и НЕ найденная `match_lines`
ни у одного варианта, печатается поимённо: это и есть случай «линия в спектре
есть, а фит её не нашёл».

⛔ Ничего не пишет ни в корпус, ни в базы. Читает свои копии из `_corpus_raw`.

Запуск:

    python tools/CORPUS/scripts/ecal_accept_check.py [--only=KEY,KEY]
                                                     [--band=200] [--sig=8]
                                                     [--csv=файл]
"""
import os
import sys
import csv

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
sys.path.insert(0, HERE)


def _load_from_head(names):
    u"""Подсунуть модули из HEAD вместо рабочего дерева (ключ `--base=head`).

    ⛔ Нужно потому, что `V12` и `V13` чинятся ОДНОВРЕМЕННО, в одном дереве.
    Мерка, посчитанная на рабочем дереве, мерила бы обе правки разом, а
    приписать число причине было бы нельзя. `--base=head` берёт `gaussfit` и
    `corpus_calib` из последнего коммита — то есть до правки `V13`, — и на этой
    базе видно, что мерка НЕ слепа к линии, потерянной фитом.
    """
    import subprocess
    import importlib.util
    import tempfile
    tmp = os.path.join(tempfile.gettempdir(), 'v12_head_modules')
    os.makedirs(tmp, exist_ok=True)
    for name in names:
        src = subprocess.check_output(
            ['git', 'show', 'HEAD:tools/CORPUS/scripts/%s.py' % name], cwd=REPO)
        path = os.path.join(tmp, name + '.py')
        with open(path, 'wb') as h:
            h.write(src)
        spec = importlib.util.spec_from_file_location(name, path)
        mod = importlib.util.module_from_spec(spec)
        sys.modules[name] = mod
        spec.loader.exec_module(mod)


BASE = 'tree'
for _a in sys.argv[1:]:
    if _a.startswith('--base='):
        BASE = _a.split('=', 1)[1]
if BASE == 'head':
    _load_from_head(('gaussfit', 'corpus_calib'))

import corpus_def                                     # noqa: E402
import corpus_calib                                   # noqa: E402
import build_corpus                                   # noqa: E402
from spectrum import Spectrum                         # noqa: E402

RAW = os.path.join(HERE, '_corpus_raw')
BAND_KEV = 200.0
MIN_SIG = 8.0


# ---------------------------------------------------------------------------
# A. стадия 2а с записью КАЖДОГО решения и наборов, по которым оно принято
# ---------------------------------------------------------------------------
def eset(lines):
    u"""Набор линий как множество энергий (округление до 0.01 кэВ)."""
    return frozenset(round(float(a['e_ref']), 2) for a in lines)


def stage12(entries, mode, log):
    u"""Стадии 1 и 2а конвейера в режиме `mode`; каждое решение — строка в log.

    Повторяет `ecal_extrapolation.build_state`, но зовёт `accept_recalibration`
    ЯВНО всеми тремя правилами на одной и той же паре (старое, новое), чтобы
    разницу можно было приписать правилу, а не траектории.
    """
    state = {}
    for e in entries:
        raw = os.path.join(RAW, e['key'] + '.xml')
        if not os.path.isfile(raw):
            continue
        try:
            sp = Spectrum(raw)
            ecal, acc, r662, tag = build_corpus.calibrate_one(sp, e)
        except Exception as ex:
            print('%-24s ОШИБКА стадии 1: %s' % (e['key'], ex))
            continue
        state[e['key']] = dict(entry=e, det=e['det'], sp=sp, ecal=ecal,
                               accepted=acc, r662=r662, mode=tag)

    for rnd in (1, 2):
        res_a = {}
        for det in sorted({st['det'] for st in state.values()}):
            pts = build_corpus.resolution_points(state, det)
            if len(pts) >= 2:
                res_a[det] = float(np.median([w / np.sqrt(e) for e, w, _ in pts]))
        moved = 0
        for key, st in sorted(state.items()):
            hint = res_a.get(st['det'])
            if hint is None:
                continue
            try:
                cal, acc, r662, tag = build_corpus.calibrate_one(
                    st['sp'], st['entry'], res_a_hint=hint)
            except Exception:
                continue

            old_e, new_e = eset(st['accepted']), eset(acc)
            rec = dict(key=key, traj=mode, round=rnd,
                       n_old=len(old_e), n_new=len(new_e),
                       e_lo_old=(min(old_e) if old_e else ''),
                       e_lo_new=(min(new_e) if new_e else ''),
                       only_old=len(old_e - new_e), only_new=len(new_e - old_e))

            for rule in ('legacy', 'union', 'old'):
                take, before, after, fixed = build_corpus.accept_recalibration(
                    st['ecal'], st['accepted'], st['r662'], cal, acc, r662,
                    mode=rule)
                # НАБОРЫ, по которым фактически посчитаны before и after
                if rule == 'legacy':
                    sb, sa = old_e, new_e
                else:
                    sb = sa = frozenset(round(float(a['e_ref']), 2)
                                        for a in fixed)
                rec['%s_take' % rule] = int(bool(take))
                rec['%s_before' % rule] = round(float(before), 4)
                rec['%s_after' % rule] = round(float(after), 4)
                rec['%s_nb' % rule] = len(sb)
                rec['%s_na' % rule] = len(sa)
                rec['%s_same' % rule] = int(sb == sa)
                if rule == 'union':
                    # ⚠ Курированная энергия — ЦЕНТРОИД БЛЕНДА, а он зависит от
                    # разрешения; у первого прохода разрешение своё, у второго
                    # из модели группы. Значит один и тот же физический пик
                    # может попасть в объединение ДВАЖДЫ под слегка разными
                    # энергиями. Считаем такие пары.
                    ee = sorted(sb)
                    r662u = r662 if r662 else st['r662']
                    dup = 0
                    for i in range(1, len(ee)):
                        fw = r662u * np.sqrt(662.0) * np.sqrt(max(ee[i], 5.0))
                        if ee[i] - ee[i - 1] < 0.3 * fw:
                            dup += 1
                    rec['union_dup'] = dup
            log.append(rec)

            if not rec['%s_take' % mode]:
                continue
            st.update(ecal=cal, accepted=acc, r662=r662, mode=tag + '/grp')
            moved += 1
        if not moved:
            break
    return state


# ---------------------------------------------------------------------------
# B. поиск линии ПО ДАННЫМ, без gaussfit
# ---------------------------------------------------------------------------
def locate(counts, ch_lo, ch_hi, fwhm_ch):
    u"""Положение и значимость пика в окне [ch_lo, ch_hi] — без всякого фита.

    Континуум снимается прямой по медианам крайних участков окна (по половине
    полуширины с каждого края), положение берётся центроидом ядра ±0.6 ПШПВ
    вокруг максимума сглаженного остатка, значимость — чистый счёт, делённый на
    корень из полного счёта в том же ядре.

    Возвращает (канал, значимость, чистый счёт) либо None.
    """
    n = len(counts)
    lo = int(max(0, np.floor(ch_lo)))
    hi = int(min(n - 1, np.ceil(ch_hi)))
    if hi - lo < 4:
        return None
    seg = np.asarray(counts[lo:hi + 1], dtype=float)
    m = max(2, int(round(0.5 * fwhm_ch)))
    m = min(m, max(1, len(seg) // 4))
    y0 = float(np.median(seg[:m]))
    y1 = float(np.median(seg[-m:]))
    x = np.arange(len(seg), dtype=float)
    base = y0 + (y1 - y0) * x / max(len(seg) - 1, 1)
    net = seg - base

    w = max(1, int(round(0.5 * fwhm_ch)))
    ker = np.ones(2 * w + 1) / (2 * w + 1)
    sm = np.convolve(net, ker, mode='same')
    if len(seg) > 2 * m + 2:
        core = np.arange(m, len(seg) - m)
    else:
        core = np.arange(len(seg))
    i = int(core[int(np.argmax(sm[core]))])

    half = max(1, int(round(0.6 * fwhm_ch)))
    a, b = max(0, i - half), min(len(seg) - 1, i + half)
    ker_net = np.clip(net[a:b + 1], 0.0, None)
    gross = float(seg[a:b + 1].sum())
    s = float(ker_net.sum())
    if s <= 0 or gross <= 0:
        return None
    sig = s / np.sqrt(max(gross, 1.0))
    cen = float((ker_net * np.arange(a, b + 1)).sum() / s)
    return lo + cen, sig, s


def curated(st, res_a, band):
    ent = dict(st['entry'])
    ent['wanted'] = build_corpus.wanted_lines(st['entry'])
    build_corpus.calibrate.sample_lines = build_corpus.sample_lines
    lines = build_corpus.calibrate.curate(
        ent, lambda e: res_a * np.sqrt(max(float(e), 5.0)), min_purity=0.45)
    return [l for l in lines if l[0] <= band]


def predictions(st_a, st_b, e_ref, res_a):
    u"""Предсказанный канал линии обеими кривыми и её полуширина в каналах."""
    preds, fw = [], []
    for st in (st_a, st_b):
        cal = st['ecal']
        ch = cal.channel(e_ref)
        d = abs(cal.dEdch(ch))
        if d <= 0:
            continue
        preds.append(ch)
        fw.append(max(res_a * np.sqrt(max(e_ref, 5.0)) / d, 1.2))
    if not preds:
        return None, None
    return preds, float(np.median(fw))


#: Сторож расстояния: найденное положение должно лежать не дальше стольких
#: полуширин от БЛИЖАЙШЕГО из двух предсказаний. Считается по минимуму из обеих
#: кривых, поэтому сторож не может помочь одному варианту против другого: он
#: срабатывает, лишь когда мимо промахнулись ОБА. Всё им отложенное печатается
#: поимённо — молча не пропадает ничего.
DIST_MAX = 1.0


def fixed_lines(st_a, st_b, band, min_sig, tol_fwhm=2.5):
    u"""Неподвижный набор (линия -> канал), найденный ПО ДАННЫМ.

    Окно — объединение предсказаний ОБЕИХ кривых, расширенное на `tol_fwhm`
    полуширин. Канал внутри него — свойство данных, а не калибровки, поэтому
    вариант не может спрятаться, не найдя линию.

    ⛔ **Широкое окно без разбора КРАДЁТ ЧУЖОЙ ПИК.** Первая редакция этой
    мерки объявила «найденной» линию 198.11 кэВ в бариевом спектре с 306 sigma —
    на самом деле в окно ±3 ПШПВ (на сцинтилляторе это ±76 кэВ) попал соседний
    настоящий пик бария. Поэтому найденное положение проходит ДВА отбора:
    оно должно быть ближе к предсказаниям СВОЕЙ линии, чем к предсказаниям
    любой другой курированной (соперники берутся по ВСЕЙ шкале, не только в
    полосе), и на одном канале остаётся одна линия — та, чьё предсказание
    ближе.
    """
    sp = st_a['sp']
    res_a = st_b['r662'] * np.sqrt(662.0)

    # соперники — ВСЕ курированные линии спектра, включая лежащие выше полосы
    rivals = []
    for e_ref, label, purity, e_table in curated(st_a, res_a, 1e9):
        preds, fwhm_ch = predictions(st_a, st_b, e_ref, res_a)
        if preds:
            rivals.append((round(float(e_ref), 2), preds, fwhm_ch))

    cand = {}
    for e_ref, label, purity, e_table in curated(st_a, res_a, band):
        e_key = round(float(e_ref), 2)
        preds, fwhm_ch = predictions(st_a, st_b, e_ref, res_a)
        if not preds:
            continue
        ch_lo = min(preds) - tol_fwhm * fwhm_ch
        ch_hi = max(preds) + tol_fwhm * fwhm_ch
        if ch_lo < 3 or ch_hi > sp.n - 4:
            continue
        r = locate(sp.counts, ch_lo, ch_hi, fwhm_ch)
        if r is None:
            continue
        ch, sig, area = r
        if sig < min_sig:
            continue
        # 1) расстояние до СВОЕЙ линии — в полуширинах
        mine = min(abs(ch - p) for p in preds) / max(fwhm_ch, 1e-9)
        # 2) есть ли соперник ближе
        stolen = None
        for e_r, p_r, fw_r in rivals:
            if e_r == e_key:
                continue
            d = min(abs(ch - p) for p in p_r) / max(fw_r, 1e-9)
            if d < mine - 1e-9:
                stolen = (e_r, d)
                break
        if stolen is not None:
            continue
        cand[e_key] = dict(ch=ch, sig=sig, fwhm_ch=fwhm_ch, label=label,
                           span=float(ch_hi - ch_lo), dist=mine)

    # один канал — одна линия
    out, held = {}, {}
    for e_key in sorted(cand, key=lambda k: cand[k]['dist']):
        d = cand[e_key]
        clash = [e for e, o in out.items()
                 if abs(o['ch'] - d['ch']) < 0.5 * min(o['fwhm_ch'], d['fwhm_ch'])]
        if clash:
            continue
        if d['dist'] > DIST_MAX:
            held[e_key] = d
            continue
        out[e_key] = d
    return out, res_a, held


def score(cal, fixed, res_a):
    u"""Σ|промах| и худший промах в долях ПШПВ на НЕПОДВИЖНОМ наборе."""
    tot = 0.0
    worst = 0.0
    for e_ref, d in fixed.items():
        miss = float(cal.energy(d['ch']) - e_ref)
        f = miss / max(res_a * np.sqrt(max(e_ref, 5.0)), 1e-9)
        tot += abs(f)
        if abs(f) > abs(worst):
            worst = f
    return tot, worst


# ---------------------------------------------------------------------------
# C. управляемый опыт: кандидату НЕ меняют кривую, а лишь прячут нижнюю линию
# ---------------------------------------------------------------------------
def pairs_stage12(entries):
    u"""Пары (старая калибровка, кандидат второго прохода) — без приёмки.

    Стадия 1 у всех, по её опорам строится модель разрешения группы (как в
    конвейере), затем второй проход. Приёмка НЕ применяется: пары нужны сырыми,
    чтобы одно и то же решение можно было предъявить всем правилам сразу.
    """
    state = {}
    for e in entries:
        raw = os.path.join(RAW, e['key'] + '.xml')
        if not os.path.isfile(raw):
            continue
        try:
            sp = Spectrum(raw)
            cal, acc, r662, tag = build_corpus.calibrate_one(sp, e)
        except Exception:
            continue
        state[e['key']] = dict(entry=e, det=e['det'], sp=sp, ecal=cal,
                               accepted=acc, r662=r662)
    res_a = {}
    for det in sorted({st['det'] for st in state.values()}):
        pts = build_corpus.resolution_points(state, det)
        if len(pts) >= 2:
            res_a[det] = float(np.median([w / np.sqrt(e) for e, w, _ in pts]))
    out = []
    for key, st in sorted(state.items()):
        hint = res_a.get(st['det'])
        if hint is None:
            continue
        try:
            cal, acc, r662, tag = build_corpus.calibrate_one(
                st['sp'], st['entry'], res_a_hint=hint)
        except Exception:
            continue
        out.append((key, st, cal, acc, r662))
    return out


def experiment(pairs, rules=('legacy', 'union', 'union1', 'old')):
    u"""⚖ ГЛАВНОЕ ДОКАЗАТЕЛЬСТВО `V12`, и оно управляемое, а не наблюдательное.

    Кандидату второго прохода НЕ меняют кривую — у него лишь отнимают самую
    нижнюю опору, и притом такую, которая есть и у старой калибровки. То есть
    кандидат объективно не стал лучше: он просто перестал показывать свой худший
    промах. Честное правило обязано этого не заметить.

    Печатается: у скольких опытов невязка кандидата от этого УЛУЧШИЛАСЬ и у
    скольких ОТКАЗ превратился в ПРИЁМ.
    """
    print()
    print(u'=== C. УПРАВЛЯЕМЫЙ ОПЫТ: КРИВАЯ ТА ЖЕ, НИЖНЯЯ ЛИНИЯ СПРЯТАНА ===')
    for rule in rules:
        n = better = flip = 0
        ds = 0.0
        ex = []
        for key, st, cal, acc, r662 in pairs:
            if len(acc) < 3 or not st['accepted']:
                continue
            lo = min(acc, key=lambda a: float(a['e_ref']))
            old_e = {round(float(a['e_ref']), 2) for a in st['accepted']}
            if round(float(lo['e_ref']), 2) not in old_e:
                continue
            cut = [a for a in acc if a is not lo]
            t0, b0, a0, _ = build_corpus.accept_recalibration(
                st['ecal'], st['accepted'], st['r662'], cal, acc, r662, mode=rule)
            t1, b1, a1, _ = build_corpus.accept_recalibration(
                st['ecal'], st['accepted'], st['r662'], cal, cut, r662, mode=rule)
            n += 1
            ds += a1 - a0
            if a1 < a0 - 1e-9:
                better += 1
            if (not t0) and t1:
                flip += 1
                ex.append((a0 - a1, key, float(lo['e_ref']), b0, a0, a1))
        print(u'  %-7s: опытов %3d; невязка кандидата УЛУЧШИЛАСЬ от выброса '
              u'у %3d; ОТКАЗ стал ПРИЁМОМ у %2d; средний сдвиг %+.4f ПШПВ'
              % (rule, n, better, flip, ds / max(n, 1)))
        for d, key, e, b0, a0, a1 in sorted(ex, reverse=True)[:6]:
            print(u'      %-22s спрятана %7.2f кэВ: after %.4f -> %.4f '
                  u'при before %.4f' % (key, e, a0, a1, b0))


def observe(pairs, rules=('legacy', 'union', 'union1', 'old')):
    u"""Наблюдение В ТОЧКЕ ВЫЗОВА `residual_fwhm`, а не по коду построения.

    Раздел A смотрит на набор, который приёмка ВЕРНУЛА; здесь перехватывается
    то, что она реально СКОРМИЛА невязке, — включая разрешение. Проверять надо
    именно так: набор можно построить правильно и всё равно позвать невязку не с
    ним.
    """
    calls = []
    orig = corpus_calib.residual_fwhm

    def spy(cal, prs, res_a):
        calls.append((frozenset(round(float(a['e_ref']), 3) for a in prs),
                      round(float(res_a), 6)))
        return orig(cal, prs, res_a)

    corpus_calib.residual_fwhm = spy
    build_corpus.corpus_calib.residual_fwhm = spy
    try:
        print()
        print(u'=== D. ЧТО ПРИЁМКА РЕАЛЬНО СКОРМИЛА residual_fwhm ===')
        for rule in rules:
            ss = sr = both = 0
            for key, st, cal, acc, r662 in pairs:
                if not acc or not st['accepted']:
                    continue
                del calls[:]
                build_corpus.accept_recalibration(
                    st['ecal'], st['accepted'], st['r662'], cal, acc, r662,
                    mode=rule)
                if len(calls) != 2:
                    continue
                both += 1
                ss += int(calls[0][0] == calls[1][0])
                sr += int(calls[0][1] == calls[1][1])
            print(u'  %-7s: одинаковый НАБОР ЛИНИЙ у before и after %3d из %3d; '
                  u'одинаковое РАЗРЕШЕНИЕ %3d из %3d'
                  % (rule, ss, both, sr, both))
    finally:
        corpus_calib.residual_fwhm = orig
        build_corpus.corpus_calib.residual_fwhm = orig


def main():
    only = None
    band = BAND_KEV
    min_sig = MIN_SIG
    out_csv = None
    for a in sys.argv[1:]:
        if a.startswith('--only='):
            only = set(a.split('=', 1)[1].split(','))
        elif a.startswith('--band='):
            band = float(a.split('=', 1)[1])
        elif a.startswith('--sig='):
            min_sig = float(a.split('=', 1)[1])
        elif a.startswith('--base='):
            pass
        elif a.startswith('--csv='):
            out_csv = a.split('=', 1)[1]

    entries = [e for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON
               if only is None or e['key'] in only]
    print('спектров: %d, полоса: %.0f кэВ, порог значимости мерки: %.0f sigma'
          % (len(entries), band, min_sig))
    print('база кода фита: %s (%s)'
          % (BASE, 'HEAD, до правки V13' if BASE == 'head' else 'рабочее дерево'))

    print(u'умолчание build_corpus.ECAL_ACCEPT = %r' % build_corpus.ECAL_ACCEPT)

    # C и D идут ПЕРВЫМИ: они не зависят от траектории и потому не могут быть
    # испорчены её выбором.
    prs = pairs_stage12(entries)
    print(u'пар (старая, кандидат): %d' % len(prs))
    observe(prs)
    experiment(prs)

    log = []
    states = {}
    for mode in ('legacy', 'union'):
        states[mode] = stage12(entries, mode, log)
        print('траектория %-7s: спектров с опорами %d из %d'
              % (mode, sum(1 for s in states[mode].values() if s['accepted']),
                 len(states[mode])))

    # --- A. по каким наборам сравнивались невязки ---
    print()
    print('=== A. НАБОРЫ, ПО КОТОРЫМ СЧИТАЛИСЬ before И after ===')
    print('решений стадии 2а записано: %d' % len(log))
    for rule in ('legacy', 'union', 'old'):
        same = sum(r['%s_same' % rule] for r in log)
        print('  %-7s: наборы before и after СОВПАЛИ у %d решений из %d'
              % (rule, same, len(log)))

    dupd = [r for r in log if r.get('union_dup')]
    print('  ⚠ у union один физический пик попал в набор дважды (центроид '
          'бленда зависит от разрешения): %d решений из %d, пар всего %d'
          % (len(dupd), len(log), sum(r.get('union_dup', 0) for r in log)))

    # ⚠ Числа выше сложены по ОБЕИМ траекториям, а решения у них разные. Правило
    # `legacy` живёт на СВОЕЙ траектории, и цену ему надо называть по ней.
    for traj in ('legacy', 'union'):
        R = [r for r in log if r['traj'] == traj]
        d = [r for r in R if not r['legacy_same']]
        t = [r for r in d if r['legacy_take']]
        dr = [r for r in t if r['e_lo_new'] != '' and r['e_lo_old'] != ''
              and r['e_lo_new'] > r['e_lo_old'] + 1e-6]
        fl = [r for r in dr if not r['union_take']]
        print('  траектория %-7s: решений %3d; у legacy наборы разошлись %3d, '
              'принято %3d, нижняя опора потеряна %2d, union переворачивает %2d'
              % (traj, len(R), len(d), len(t), len(dr), len(fl)))

    diff = [r for r in log if not r['legacy_same']]
    took = [r for r in diff if r['legacy_take']]
    print()
    print('РАЗНЫЕ наборы у legacy: %d решений; из них ПРИНЯТО: %d'
          % (len(diff), len(took)))
    dropped = [r for r in took
               if r['e_lo_new'] != '' and r['e_lo_old'] != ''
               and r['e_lo_new'] > r['e_lo_old'] + 1e-6]
    print('из принятых — новый набор ПОТЕРЯЛ нижнюю опору: %d' % len(dropped))
    flip = [r for r in dropped if not r['union_take']]
    print('из них по ОДНОМУ набору (union) решение переворачивается: %d'
          % len(flip))
    seen_d = set()
    for r in sorted(dropped,
                    key=lambda r: -(float(r['e_lo_new']) - float(r['e_lo_old']))):
        tag = (r['key'], r['round'], r['e_lo_old'], r['e_lo_new'])
        if tag in seen_d:
            continue
        seen_d.add(tag)
        if len(seen_d) > 15:
            break
        print('   %-22s пр%d опора %7.1f -> %7.1f, линий %d -> %d; '
              'legacy %.3f -> %.3f (взял %d) | union %.3f -> %.3f (взял %d)'
              % (r['key'], r['round'], r['e_lo_old'], r['e_lo_new'],
                 r['n_old'], r['n_new'],
                 r['legacy_before'], r['legacy_after'], r['legacy_take'],
                 r['union_before'], r['union_after'], r['union_take']))

    # --- B. независимая мерка ---
    print()
    print('=== B. НЕЗАВИСИМАЯ МЕРКА (канал по данным, без gaussfit) ===')
    rows = []
    tot = {'legacy': 0.0, 'union': 0.0}
    worst = {'legacy': (0.0, ''), 'union': (0.0, '')}
    n_lines = 0
    blind = []
    held_all = []
    for key in sorted(set(states['legacy']) & set(states['union'])):
        sl, su = states['legacy'][key], states['union'][key]
        # ⛔ Спектр БЕЗ опор из мерки не выбрасывается. Именно он — главная
        # улика `V13`: у `G1S16_Am241_P5` фит не нашёл НИ ОДНОЙ линии, хотя
        # 59.5 кэВ стоит в спектре сотнями сигм. Выбросив такие спектры, мерка
        # ослепла бы ровно там, где дефект виден лучше всего.
        fixed, res_a, held = fixed_lines(sl, su, band, min_sig)
        for e_ref, d in sorted(held.items()):
            held_all.append((d['dist'], key, e_ref, d['sig']))
        if not fixed:
            continue
        n_lines += len(fixed)
        row = dict(key=key, det=sl['det'], lines=len(fixed),
                   mode_legacy=sl['mode'], mode_union=su['mode'],
                   e_lo_legacy=(round(min(a['e_ref'] for a in sl['accepted']), 1)
                                if sl['accepted'] else -1.0),
                   e_lo_union=(round(min(a['e_ref'] for a in su['accepted']), 1)
                               if su['accepted'] else -1.0),
                   n_anchor_legacy=len(sl['accepted']),
                   n_anchor_union=len(su['accepted']))
        for mode, st in (('legacy', sl), ('union', su)):
            s, w = score(st['ecal'], fixed, res_a)
            tot[mode] += s
            if abs(w) > abs(worst[mode][0]):
                worst[mode] = (w, key)
            row['sum_' + mode] = round(s, 3)
            row['worst_' + mode] = round(w, 3)
        rows.append(row)

        # слепые пятна фита: что видит мерка и НЕ видит match_lines
        seen = set()
        for st in (sl, su):
            found = corpus_calib.match_lines(
                st['sp'].counts, st['ecal'], curated(st, res_a, band), res_a,
                tol_fwhm=6.0, width_lo=0.3, width_hi=3.0)
            seen |= {round(float(a['e_ref']), 2) for a in found}
        for e_ref, d in sorted(fixed.items()):
            if e_ref not in seen:
                blind.append((d['sig'], key, e_ref, d['label']))

    print('спектров в мерке: %d, линий в неподвижном наборе: %d'
          % (len(rows), n_lines))
    for mode in ('legacy', 'union'):
        print('  %-7s: сумма |промаха| %8.2f ПШПВ, худший %6.3f (%s)'
              % (mode, tot[mode], abs(worst[mode][0]), worst[mode][1]))
    better = [r for r in rows if r['sum_union'] < r['sum_legacy'] - 1e-3]
    worse = [r for r in rows if r['sum_union'] > r['sum_legacy'] + 1e-3]
    print('  union лучше на %d спектрах, хуже на %d, поровну на %d'
          % (len(better), len(worse), len(rows) - len(better) - len(worse)))
    lower = [r for r in rows if r['e_lo_union'] < r['e_lo_legacy'] - 1e-6]
    higher = [r for r in rows if r['e_lo_union'] > r['e_lo_legacy'] + 1e-6]
    print('  нижняя опора у union НИЖЕ на %d спектрах, выше на %d'
          % (len(lower), len(higher)))
    for r in sorted(rows, key=lambda r: r['sum_union'] - r['sum_legacy'])[:10]:
        if r['sum_union'] >= r['sum_legacy'] - 1e-3:
            break
        print('     лучше %-22s %6.2f -> %6.2f ПШПВ, опора %7.1f -> %7.1f'
              % (r['key'], r['sum_legacy'], r['sum_union'],
                 r['e_lo_legacy'], r['e_lo_union']))
    for r in sorted(rows, key=lambda r: r['sum_legacy'] - r['sum_union'])[:10]:
        if r['sum_union'] <= r['sum_legacy'] + 1e-3:
            break
        print('     ХУЖЕ  %-22s %6.2f -> %6.2f ПШПВ, опора %7.1f -> %7.1f'
              % (r['key'], r['sum_legacy'], r['sum_union'],
                 r['e_lo_legacy'], r['e_lo_union']))

    print()
    print('отложено сторожем расстояния (обе кривые дальше %.1f ПШПВ от пика '
          'ЛИБО пик чужой): %d' % (DIST_MAX, len(held_all)))
    for dist, key, e_ref, sig in sorted(held_all, reverse=True)[:8]:
        print('   %-22s %8.2f кэВ  %5.2f ПШПВ мимо, %8.1f sigma'
              % (key, e_ref, dist, sig))

    print()
    print('СЛЕПОЕ ПЯТНО ФИТА (`V13`): линия найдена меркой по данным, но '
          '`match_lines` НЕ нашла её ни у одного варианта — %d случаев'
          % len(blind))
    for sig, key, e_ref, label in sorted(blind, reverse=True)[:15]:
        print('   %-22s %8.2f кэВ  %-14s  %8.1f sigma по данным'
              % (key, e_ref, label[:14], sig))

    if out_csv and rows:
        with open(out_csv, 'w', newline='', encoding='utf-8') as h:
            w = csv.DictWriter(h, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print('\nтаблица спектров: %s' % out_csv)
    if out_csv and log:
        p = out_csv.replace('.csv', '_decisions.csv')
        with open(p, 'w', newline='', encoding='utf-8') as h:
            w = csv.DictWriter(h, fieldnames=list(log[0].keys()))
            w.writeheader()
            w.writerows(log)
        print('таблица решений: %s' % p)


if __name__ == '__main__':
    main()
