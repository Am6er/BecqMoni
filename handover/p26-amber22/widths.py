# -*- coding: utf-8 -*-
"""П26 12.09.2026, `AMBER22` — ширина пиков ДАННЫХ против МОДЕЛИ подгонкой гауссианы (не «грубо»).

    python handover/p26-amber22/widths.py <дамп> [...]

Для линий 238.6 / 338.3 / 583.2 / 911.2 / 2614.5: в окне ±1.6 ПШПВ вокруг максимума данных
подгоняется G(E) = H·exp(−(E−c)²/2σ²) + a + b·E отдельно к `net` и к `model` (перебор c и σ по
сетке, H, a, b — МНК с весами 1/net). Печатает центр, ПШПВ и площадь H·σ·√(2π) у данных и у модели,
отношения модель/данные. ПШПВ прибора (7.65 % на 662 по √E) — только для окна.
"""
import csv
import math
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

LINES = [238.632, 338.320, 583.187, 911.204, 2614.511]
FWHM662 = 0.0765 * 661.657
S2F = 2.0 * math.sqrt(2.0 * math.log(2.0))


def fwhm(e):
    return FWHM662 * math.sqrt(e / 661.657)


def load(path):
    rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
    kev = [float(r['keV']) for r in rows]
    return kev, [float(r['net']) for r in rows], [float(r['model']) for r in rows]


def smooth(y, k=5):
    n = len(y)
    return [sum(y[max(0, i - k):min(n, i + k + 1)]) / (min(n, i + k + 1) - max(0, i - k)) for i in range(n)]


def solve3(rows, w, y):
    m = [[0.0] * 3 for _ in range(3)]
    v = [0.0] * 3
    for f, wi, yi in zip(rows, w, y):
        for i in range(3):
            v[i] += wi * f[i] * yi
            for j in range(3):
                m[i][j] += wi * f[i] * f[j]
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


def gfit(x, y, w, c0, s0):
    best = None
    for ic in range(-20, 21):
        c = c0 + ic * 0.1 * s0
        for isg in range(-15, 16):
            s = s0 * (1.0 + 0.04 * isg)
            if s <= 0:
                continue
            f = [(math.exp(-0.5 * ((xi - c) / s) ** 2), 1.0, xi) for xi in x]
            sol = solve3(f, w, y)
            if sol is None:
                continue
            chi = sum(wi * (yi - sol[0] * fi[0] - sol[1] - sol[2] * fi[2]) ** 2 for wi, yi, fi in zip(w, y, f))
            if best is None or chi < best[0]:
                best = (chi, c, s, sol[0])
    return best


def report(path):
    kev, net, model = load(path)
    print('== %s ==' % path)
    print('%-7s | %8s %7s %9s | %8s %7s %9s | %7s %7s %7s' % (
        'линия', 'центр d', 'ПШПВ d', 'площ d', 'центр m', 'ПШПВ m', 'площ m', 'Δc кэВ', 'ПШПВm/d', 'площ m/d'))
    ys = smooth(net)
    for e in LINES:
        w = fwhm(e)
        ids = [i for i, x in enumerate(kev) if e * 0.94 <= x < e * 1.06]
        cd = kev[max(ids, key=lambda i: ys[i])]
        win = [i for i, x in enumerate(kev) if cd - 1.6 * w <= x < cd + 1.6 * w]
        x = [kev[i] for i in win]
        wts = [1.0 / max(net[i], 1.0) for i in win]
        d = gfit(x, [net[i] for i in win], wts, cd, w / S2F)
        # у модели свой максимум (шкала корпуса промахивается)
        ym = smooth(model)
        cm = kev[max(ids, key=lambda i: ym[i])]
        winm = [i for i, xx in enumerate(kev) if cm - 1.6 * w <= xx < cm + 1.6 * w]
        xm = [kev[i] for i in winm]
        m = gfit(xm, [model[i] for i in winm], [1.0 / max(net[i], 1.0) for i in winm], cm, w / S2F)
        ad = d[3] * d[2] * math.sqrt(2 * math.pi)
        am = m[3] * m[2] * math.sqrt(2 * math.pi)
        print('%-7.1f | %8.1f %7.1f %9.0f | %8.1f %7.1f %9.0f | %+7.1f %7.3f %7.3f' % (
            e, d[1], d[2] * S2F, ad, m[1], m[2] * S2F, am, m[1] - d[1], m[2] / d[2], am / ad))


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
