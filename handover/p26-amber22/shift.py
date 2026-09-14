# -*- coding: utf-8 -*-
"""П26 12.09.2026, `AMBER22` п. 3 — положение против амплитуды НА ВСЕЙ МОДЕЛИ (проще peaks.py).

    python handover/p26-amber22/shift.py <дамп> [...]

Для каждой линии: центр данных (максимум сглаженного net в ±6 % от линии), узкое окно ±1 ПШПВ
вокруг него; «как есть» — model/net − 1 в окне; затем модель СДВИГАЕТСЯ целиком по шкале на δ
(перебор ±1 ПШПВ шагом 0.5 кэВ, критерий — χ² в окне с весами 1/net, амплитуда модели НЕ трогается)
и печатается model(E−δ)/net − 1 при лучшем δ. Разность двух чисел — доля недобора, сделанная
ПОЛОЖЕНИЕМ; остаток при выровненном δ — АМПЛИТУДА.
"""
import csv
import math
import sys

sys.path.insert(0, __file__.rsplit('\\', 1)[0] if '\\' in __file__ else '.')
from peaks import fwhm, load, idx_range, smooth, interp, LINES  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def report(path):
    kev, cols = load(path)
    net, model = cols['net'], cols['model']
    ys = smooth(net)
    print('== %s ==' % path)
    print('%-7s | %7s | %8s | %6s %6s | %8s | %9s' % ('линия', 'центр d', 'как есть', 'δ кэВ', 'δ/ПШПВ', 'выровн.', 'положение'))
    for e, owner in LINES:
        w = fwhm(e)
        ids = idx_range(kev, e * 0.94, e * 1.06)
        cd = kev[max(ids, key=lambda i: ys[i])]
        win = idx_range(kev, cd - w, cd + w)
        sn = sum(net[i] for i in win)
        asis = 100.0 * (sum(model[i] for i in win) / sn - 1.0)
        best = None
        step = 0.5
        for k in range(-int(w / step), int(w / step) + 1):
            d = k * step
            m = [interp(kev, model, kev[i] - d) for i in win]
            chi = sum((net[i] - mi) ** 2 / max(net[i], 1.0) for i, mi in zip(win, m))
            if best is None or chi < best[0]:
                best = (chi, d, 100.0 * (sum(m) / sn - 1.0))
        print('%-7.1f | %7.1f | %+7.1f %% | %+6.1f %+6.2f | %+7.1f %% | %+8.1f %%' % (
            e, cd, asis, best[1], best[1] / w, best[2], asis - best[2]))


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
