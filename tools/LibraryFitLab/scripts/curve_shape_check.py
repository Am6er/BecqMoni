# -*- coding: utf-8 -*-
"""Насколько свободна кривая эффективности в вето — и что даёт её ограничение.

Расчёт ДО правки фиттера, по полному корпусу.

## Зачем

Вето по согласованности набора решает, легли ли принятые линии на общую кривую
`ln(S/I) = polynom(ln E)`. Полином СВОБОДНЫЙ, порядка 2 при пяти и более точках.
Квадратика по четырём-пяти точкам ложится почти на что угодно — и это отнимает у
теста силу ровно там, где набор короткий: на германии с оборванным рядом U-238
вето разваливает НАСТОЯЩУЮ цепочку, а короткий набор-обманку из случайно
совпавших линий пропускает.

Физическая эффективность так себя не ведёт. Выше пика (около 100-150 кэВ по
энергии) она падает МОНОТОННО, и в двойных логарифмах это почти прямая с
наклоном порядка -0.7...-1.5. Набор, легший на растущую при 2 МэВ кривую,
физически невозможен независимо от разброса. Отнять у подгонки эту степень
свободы ничего не стоит настоящей цепочке — ей она не нужна, — а обманке стоит.

## Что меряется

Единица — ЭКЗЕМПЛЯР НАБОРА, то есть пара «спектр x сет», как и в production:
вето принимает решение на набор. Для каждого экземпляра площади линий меряются
локально, принимаются те, что прошли бы Fisher z >= 4, по ним строится кривая в
пяти вариантах ограничений, и считается разброс.

Сравнение — только при ОДИНАКОВОЙ доле пропущенных настоящих наборов: у
вариантов разные шкалы разброса, и сравнивать их при общем пороге бессмысленно.
Это тот же урок, что и в прошлом разделе журнала.

Покрытие печатается всегда. В прошлый раз именно счётчик покрытия показал, что
красивые числа получены на 38 % корпуса.

Запуск:  python curve_shape_check.py [--per-det] [--knee=150]
"""
import os
import sys
import csv
import re
import json
import xml.etree.ElementTree as ET

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(os.path.dirname(HERE), 'corpus')
sys.path.insert(0, HERE)

BASE_ORDER = 2        # порядок локальной подложки при измерении площади
WIN_SIGMA = 3.5
FLANK_SIGMA = 2.0
ACCEPT_Z = 4.0        # тот же порог, что у Fisher z в production
MIN_LINES = 4         # ниже вето не строит кривую вовсе

# Выше этой энергии эффективность обязана падать. Ниже — растёт, и ограничение
# там неверно; сканируется ключом --knee=.
KNEE_KEV = float(os.environ.get('CS_KNEE', '150'))
# Физический диапазон наклона d ln(eff) / d ln E выше колена.
SLOPE_LO, SLOPE_HI = -2.5, 0.0


def read_csv(path, encoding='utf-8-sig'):
    with open(path, encoding=encoding, newline='') as fh:
        return list(csv.DictReader(fh))


def fwhm_kev(res, e):
    return float(np.sqrt(max(res[0] + res[1] * e + res[2] * e * e, 1e-6)))


def load_spectrum(key):
    rd = ET.parse(os.path.join(CORPUS, 'spectra', key + '.xml')).getroot() \
        .find('ResultDataList/ResultData')
    es = rd.find('EnergySpectrum')
    counts = np.array([int(d.text) for d in es.findall('Spectrum/DataPoint')], dtype=float)
    ecal = [float(x.text) for x in es.findall('EnergyCalibration/Coefficients/Coefficient')]
    ch = np.arange(len(counts), dtype=float)
    return counts, sum(c * ch ** i for i, c in enumerate(ecal))


def net_area(counts, energy, e0, fwhm):
    """(площадь, пуассоновская погрешность) гауссианы табличной ширины."""
    sigma = fwhm / 2.3548
    win = (energy >= e0 - WIN_SIGMA * sigma) & (energy <= e0 + WIN_SIGMA * sigma)
    if win.sum() < 9:
        return None
    x, y = energy[win], counts[win]
    g = np.exp(-0.5 * ((x - e0) / sigma) ** 2)
    flank = np.abs(x - e0) > FLANK_SIGMA * sigma
    if flank.sum() < BASE_ORDER + 3:
        return None
    try:
        base = np.polyval(np.polyfit(x[flank] - e0, y[flank], BASE_ORDER), x - e0)
    except Exception:
        return None
    gg = float((g * g).sum())
    if gg <= 1e-9:
        return None
    amp = float(((y - base) * g).sum()) / gg
    var = float(((np.maximum(y, 0.0) + np.maximum(base, 0.0)) * g * g).sum()) / gg ** 2
    scale = sigma * np.sqrt(2.0 * np.pi)
    return amp * scale, np.sqrt(max(var, 1e-12)) * scale


# ---------------------------------------------------------------------------
# подгонка с линейными неравенствами
# ---------------------------------------------------------------------------
def _fit_equality(A, y, C):
    """min ||A a - y|| при C a = 0. Через базис нуль-пространства C."""
    if C is None or len(C) == 0:
        coef, _r, _rank, _s = np.linalg.lstsq(A, y, rcond=None)
        return coef
    C = np.atleast_2d(np.asarray(C, dtype=float))
    # базис нуль-пространства: правые сингулярные векторы с нулевыми числами
    _u, s, vt = np.linalg.svd(C)
    rank = int((s > 1e-12).sum())
    N = vt[rank:].T
    if N.shape[1] == 0:
        return None
    z, _r, _rank, _s = np.linalg.lstsq(A.dot(N), y, rcond=None)
    return N.dot(z)


def fit_constrained(x, y, order, constraints):
    """min ||A a - y|| при c*a <= 0 для всех c из constraints.

    Оптимум лежит на какой-то грани допустимой области, поэтому перебираются
    все подмножества ограничений, объявленные активными (равенствами), и среди
    ДОПУСТИМЫХ решений берётся то, у которого сумма квадратов меньше. При трёх
    ограничениях это восемь задач наименьших квадратов — считается мгновенно, и
    в отличие от штрафа даёт точный ответ.
    """
    A = np.vander(x, order + 1, increasing=True)
    if A.shape[0] < A.shape[1]:
        return None
    best, best_rss = None, None
    n = len(constraints)
    for mask in range(1 << n):
        active = [constraints[i] for i in range(n) if mask & (1 << i)]
        coef = _fit_equality(A, y, active)
        if coef is None:
            continue
        # допустимость по ВСЕМ ограничениям, а не только по активным
        ok = True
        for c in constraints:
            if float(np.dot(c[:order + 1], coef)) > 1e-9:
                ok = False
                break
        if not ok:
            continue
        rss = float(((A.dot(coef) - y) ** 2).sum())
        if best_rss is None or rss < best_rss:
            best, best_rss = coef, rss
    if best is None:
        return None
    return best, np.sqrt(best_rss / len(x))


def scatter_of(x, y, variant):
    """Разброс вокруг кривой: exp(rms) - 1, как в LibraryPeakFitter."""
    n = len(x)
    if n < MIN_LINES:
        return None
    lo, hi = float(np.min(x)), float(np.max(x))
    knee = np.log(KNEE_KEV)
    # точки, в которых требуется падение: края диапазона выше колена
    nodes = [v for v in (max(lo, knee), hi) if v >= knee]

    if variant == 'свободный-2':
        order = 2 if n >= 5 else 1
        cons = []
    elif variant == 'линия':
        order, cons = 1, []
    elif variant == 'монотон':
        order = 2 if n >= 5 else 1
        cons = [np.array([0.0, 1.0, 2.0 * v]) for v in nodes]
    elif variant == 'монотон+вогн':
        order = 2 if n >= 5 else 1
        cons = [np.array([0.0, 1.0, 2.0 * v]) for v in nodes]
        cons.append(np.array([0.0, 0.0, 1.0]))
    elif variant == 'наклон':
        order = 2 if n >= 5 else 1
        cons = []
        for v in nodes:
            cons.append(np.array([0.0, 1.0, 2.0 * v]))            # <= SLOPE_HI = 0
            cons.append(np.array([0.0, -1.0, -2.0 * v]) + np.array([0.0, 0.0, 0.0]))
        # нижняя граница наклона: -(a1 + 2 a2 v) <= -SLOPE_LO
        cons = [c for c in cons]
    elif variant == 'монотон-1':
        order, cons = 1, [np.array([0.0, 1.0])] if hi >= knee else []
    else:
        raise ValueError(variant)

    if order == 1:
        cons = [c[:2] for c in cons]
    if n < order + 2:
        order = 1
        cons = [c[:2] for c in cons]
        if n < 3:
            return None

    if variant == 'наклон':
        # нижняя граница задаётся сдвигом свободного члена, поэтому решается
        # отдельно: приводим к виду c*a <= b переносом b в невязку через
        # дополнительную переменную не нужно — достаточно проверить постфактум.
        res = fit_constrained(x, y, order,
                              [np.array([0.0, 1.0, 2.0 * v])[:order + 1] for v in nodes])
        if res is None:
            return None
        coef, rms = res
        for v in nodes:
            slope = coef[1] + (2.0 * coef[2] * v if order >= 2 else 0.0)
            if slope < SLOPE_LO:
                return None            # физически невозможная крутизна
        return float(np.exp(rms) - 1.0)

    res = fit_constrained(x, y, order, cons)
    if res is None:
        return None
    _coef, rms = res
    return float(np.exp(rms) - 1.0)


# ---------------------------------------------------------------------------
def load_sets():
    path = os.path.join(HERE, 'sets_manifest.json')
    if not os.path.exists(path):
        sys.exit('нет sets_manifest.json — сначала gate_study.py --run')
    out = {}
    for m in json.load(open(path, encoding='utf-8')):
        out.setdefault((m['det'], m['chain'], m['kind']), []).append(m)
    return out


def at_rate(pairs, target):
    """Доля обманок, прошедших порог, при котором проходит target настоящих."""
    pairs = sorted(pairs, key=lambda t: t[0])          # меньше разброс = лучше
    nr = sum(1 for _v, k in pairs if k == 'real')
    nd = len(pairs) - nr
    r = d = 0
    for _v, k in pairs:
        if k == 'real':
            r += 1
        else:
            d += 1
        if nr and r / nr >= target:
            return d / max(nd, 1)
    return None


VARIANTS = ('свободный-2', 'линия', 'монотон', 'монотон+вогн', 'наклон', 'монотон-1')


def main():
    per_det = '--per-det' in sys.argv
    manifest = read_csv(os.path.join(CORPUS, 'manifest.csv'))
    dets = {d['det']: d for d in read_csv(os.path.join(CORPUS, 'detectors.csv'))}
    sets = load_sets()

    pairs = {v: [] for v in VARIANTS}
    per = {}
    n_inst = n_short = 0

    for row in manifest:
        key, det = row['key'], row['det']
        chains = [c for c in re.split(r'[;|]', row.get('chains') or '') if c]
        d = dets.get(det)
        if not chains or not d:
            continue
        res = (float(d['res_c0']), float(d['res_c1']), float(d['res_c2']))
        lo, hi = float(d['e_lo']), float(d['e_hi'])
        try:
            counts, energy = load_spectrum(key)
        except Exception:
            continue

        for chain in chains:
            for kind in ('real', 'decoy'):
                for m in sets.get((det, chain, kind), []):
                    x, y = [], []
                    for line in m['lines']:
                        e0, inten = float(line['e']), float(line['i'])
                        if not (lo <= e0 <= hi) or inten <= 0:
                            continue
                        r = net_area(counts, energy, e0, fwhm_kev(res, e0))
                        if r is None:
                            continue
                        a, sp = r
                        if a <= 0 or a / max(sp, 1e-9) < ACCEPT_Z:
                            continue
                        x.append(np.log(e0))
                        y.append(np.log(a / inten))
                    n_inst += 1
                    if len(x) < MIN_LINES:
                        n_short += 1
                        continue
                    x, y = np.array(x), np.array(y)
                    for v in VARIANTS:
                        s = scatter_of(x, y, v)
                        # вариант отверг набор по форме кривой — это худший
                        # возможный разброс, набор не проходит ни при каком пороге
                        s = float('inf') if s is None else s
                        pairs[v].append((s, kind))
                        per.setdefault(det, {q: [] for q in VARIANTS})[v].append((s, kind))

    nr = sum(1 for _v, k in pairs['свободный-2'] if k == 'real')
    nd = len(pairs['свободный-2']) - nr
    print('экземпляров набора: %d, из них короче %d линий: %d (%.0f %%)'
          % (n_inst, MIN_LINES, n_short, 100.0 * n_short / max(n_inst, 1)))
    print('в сравнении: настоящих %d, обманок %d; колено %.0f кэВ' % (nr, nd, KNEE_KEV))
    print()
    TARGETS = (0.60, 0.70, 0.80, 0.90, 0.95)
    print('доля ПРОШЕДШИХ обманок при одинаковой доле пропущенных настоящих наборов')
    print('%-14s' % 'кривая' + ''.join('%9s' % ('%.0f%%' % (t * 100)) for t in TARGETS))
    print('-' * (14 + 9 * len(TARGETS)))
    for v in VARIANTS:
        cells = []
        for t in TARGETS:
            r = at_rate(pairs[v], t)
            cells.append('%8.1f%%' % (100.0 * r) if r is not None else '       -')
        print('%-14s' % v + ''.join(cells))

    print()
    print('при рабочем пороге 1.25:')
    print('%-14s %10s %10s' % ('кривая', 'настоящих', 'обманок'))
    for v in VARIANTS:
        rp = sum(1 for s, k in pairs[v] if k == 'real' and s <= 1.25)
        dp = sum(1 for s, k in pairs[v] if k == 'decoy' and s <= 1.25)
        print('%-14s %9.1f%% %9.1f%%' % (v, 100.0 * rp / max(nr, 1), 100.0 * dp / max(nd, 1)))

    if per_det:
        print()
        print('%-12s %6s %12s %12s' % ('детектор', 'наб.', 'своб. обм.', 'монотон обм.'))
        for det in sorted(per):
            a = at_rate(per[det]['свободный-2'], 0.80)
            b = at_rate(per[det]['монотон'], 0.80)
            n = sum(1 for _v, k in per[det]['свободный-2'] if k == 'real')
            if a is None or b is None:
                continue
            print('%-12s %6d %11.1f%% %11.1f%%' % (det, n, 100.0 * a, 100.0 * b))


main()
