# -*- coding: utf-8 -*-
"""П26 12.09.2026, `AMBER22` п. 3 — остаток диска ПО ПИКАМ: положение отдельно от амплитуды.

    python handover/p26-amber22/peaks.py <дамп *_curves.csv или dump.csv> [ещё дампы]

Дамп — `--dump-curves=` (CorpusFsaProbe) или `--dump=` (FsaStackShot): столбцы keV, net, model,
continuum_raw и по столбцу на образ. Для каждой линии ряда Th-232 в окне ±1.5 ПШПВ вокруг
максимума данных подгоняется
    net(E) ≈ A · образ(E − δ) + [model(E) − образ(E)] + a + b·E
(образ — столбец нуклида-хозяина линии; остальное модели — как есть; a, b — местная подложка,
чтобы ошибка сплайна и соседей не ложилась на A). δ — перебором по сетке (шаг 0.5 кэВ, ±1 ПШПВ),
A, a, b — взвешенный МНК (веса 1/net) при каждом δ; берётся δ с наименьшим χ².
  ПОЛОЖЕНИЕ = δ (кэВ и в ПШПВ) — на сколько образ стоит не там, где данные;
  АМПЛИТУДА = A − 1 — во сколько образ занижен ПРИ ВЫРОВНЕННОМ положении;
  «узкое» = model/net − 1 в ±1 ПШПВ вокруг центра данных — то, что видит полоса П22 (положение
  и амплитуда вместе). Если δ = 0 даёт A такое же — недобор чисто амплитудный.
ПШПВ(E) — 7.65 % на 662 кэВ по √E (DS_Fwhm662 сцены AS80_th_disk).
"""
import csv
import math
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

LINES = [(238.632, 'Pb-212'), (338.320, 'Ac-228'), (583.187, 'Tl-208'), (911.204, 'Ac-228'),
         (968.971, 'Ac-228'), (1588.2, 'Ac-228'), (2614.511, 'Tl-208')]
FWHM662 = 0.0765 * 661.657


def fwhm(e):
    return FWHM662 * math.sqrt(e / 661.657)


def load(path):
    rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
    kev = [float(r['keV']) for r in rows]
    cols = {k: [float(r[k]) for r in rows] for k in rows[0] if k not in ('ch', 'keV')}
    return kev, cols


def idx_range(kev, lo, hi):
    return [i for i, e in enumerate(kev) if lo <= e < hi]


def smooth(y, k=5):
    out = []
    n = len(y)
    for i in range(n):
        a, b = max(0, i - k), min(n, i + k + 1)
        out.append(sum(y[a:b]) / (b - a))
    return out


def interp(kev, y, x):
    """Линейная интерполяция y(x) по сетке kev (возрастающая)."""
    lo, hi = 0, len(kev) - 1
    if x <= kev[0]:
        return y[0]
    if x >= kev[-1]:
        return y[-1]
    while hi - lo > 1:
        m = (lo + hi) // 2
        if kev[m] <= x:
            lo = m
        else:
            hi = m
    t = (x - kev[lo]) / (kev[hi] - kev[lo])
    return y[lo] + t * (y[hi] - y[lo])


def solve3(rows, w, y):
    """Взвешенный МНК: y ≈ A·t + a + b·x. rows — список (t, x)."""
    # нормальные уравнения 3×3
    m = [[0.0] * 3 for _ in range(3)]
    v = [0.0] * 3
    for (t, x), wi, yi in zip(rows, w, y):
        f = (t, 1.0, x)
        for i in range(3):
            v[i] += wi * f[i] * yi
            for j in range(3):
                m[i][j] += wi * f[i] * f[j]
    # решение Гаусса
    n = 3
    a = [m[i][:] + [v[i]] for i in range(n)]
    for c in range(n):
        p = max(range(c, n), key=lambda r: abs(a[r][c]))
        a[c], a[p] = a[p], a[c]
        if abs(a[c][c]) < 1e-300:
            return None
        for r in range(n):
            if r != c:
                k = a[r][c] / a[c][c]
                for cc in range(c, n + 1):
                    a[r][cc] -= k * a[c][cc]
    return [a[i][n] / a[i][i] for i in range(n)]


def fit_line(kev, net, model, owner, e):
    w = fwhm(e)
    ids = idx_range(kev, e * 0.94, e * 1.06)
    ys = smooth(net)
    cd = kev[max(ids, key=lambda i: ys[i])]
    win = idx_range(kev, cd - 1.5 * w, cd + 1.5 * w)
    x = [kev[i] for i in win]
    y = [net[i] for i in win]
    rest = [model[i] - owner[i] for i in win]
    wts = [1.0 / max(net[i], 1.0) for i in win]
    best = None
    step = 0.5
    nsteps = int(w / step)
    for k in range(-nsteps, nsteps + 1):
        d = k * step
        t = [interp(kev, owner, xi - d) for xi in x]
        yy = [yi - ri for yi, ri in zip(y, rest)]
        sol = solve3(list(zip(t, x)), wts, yy)
        if sol is None:
            continue
        A, a, b = sol
        chi2 = sum(wi * (yi - A * ti - a - b * xi) ** 2 for wi, yi, ti, xi in zip(wts, yy, t, x))
        if best is None or chi2 < best[0]:
            best = (chi2, d, A, a, b)
    # без сдвига
    t0 = [interp(kev, owner, xi) for xi in x]
    yy = [yi - ri for yi, ri in zip(y, rest)]
    sol0 = solve3(list(zip(t0, x)), wts, yy)
    chi0 = sum(wi * (yi - sol0[0] * ti - sol0[1] - sol0[2] * xi) ** 2 for wi, yi, ti, xi in zip(wts, yy, t0, x))
    # узкое окно ±1 ПШПВ вокруг центра данных: model/net − 1
    nw = idx_range(kev, cd - w, cd + w)
    sn = sum(net[i] for i in nw)
    sm = sum(model[i] for i in nw)
    so = sum(owner[i] for i in nw)
    return cd, w, best, sol0[0], chi0, len(win), 100.0 * (sm / sn - 1.0), 100.0 * so / sn


def report(path):
    kev, cols = load(path)
    net, model = cols['net'], cols['model']
    print('== %s ==' % path)
    print('%-7s %-7s | %7s | %7s %7s | %8s | %8s %9s | %8s %8s' % (
        'линия', 'образ', 'центр d', 'δ кэВ', 'δ/ПШПВ', 'A−1', 'A−1|δ=0', 'χ²0/χ²δ', 'узкое', 'доля обр'))
    for e, owner in LINES:
        if owner not in cols:
            continue
        cd, w, best, A0, chi0, n, narrow, share = fit_line(kev, net, model, cols[owner], e)
        chi2, d, A, a, b = best
        print('%-7.1f %-7s | %7.1f | %+7.1f %+7.2f | %+7.1f %% | %+7.1f %% %9.2f | %+7.1f %% %7.0f %%   (окно ±1.5 ПШПВ = ±%.0f кэВ, точек %d, χ²/n %.1f)' % (
            e, owner, cd, d, d / w, 100 * (A - 1), 100 * (A0 - 1), chi0 / chi2 if chi2 > 0 else float('nan'),
            narrow, share, 1.5 * w, n, chi2 / n))


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
