# -*- coding: utf-8 -*-
"""П42 13.09.2026, `AMBER22` — положение и ширина групп линий у ДАННЫХ и у МОДЕЛИ одной подгонкой:
сумма гауссиан группы (табличные энергии и интенсивности, общий сдвиг ∝ E, общая ПШПВ ∝ √E) + линейная
подложка, веса 1/max(y,1). Печатает сдвиг (кэВ, %), ПШПВ (кэВ, %) и χ²/n для net и model дампа.

    python handover/p42-amber22/groupfit.py <dump.csv> [...]
"""
import csv
import math
import sys

import numpy as np
from scipy.optimize import least_squares

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
S2F = 2 * math.sqrt(2 * math.log(2))
GROUPS = [('238', 210, 275, [(238.632, 43.6), (240.986, 4.1)]),
          ('338', 300, 380, [(338.32, 11.27), (328.0, 2.95), (332.37, 0.4), (340.96, 0.37)]),
          ('583', 530, 640, [(583.187, 30.5)]),
          ('911/969', 850, 1010, [(911.204, 25.8), (968.971, 15.8), (964.766, 4.99)]),
          ('2614', 2450, 2800, [(2614.511, 35.85)])]


def fit(kev, y, lo, hi, lines):
    ids = np.where((kev >= lo) & (kev < hi))[0]
    x = kev[ids]
    yy = y[ids]
    w = 1 / np.sqrt(np.maximum(yy, 1))
    E0 = lines[0][0]

    def f(p):
        A, sh, fw, a, b = p
        m = a + b * (x - E0)
        for E, I in lines:
            s = fw * math.sqrt(E / E0) / S2F
            m = m + A * I * np.exp(-0.5 * ((x - (E + sh * E / E0)) / s) ** 2) / s
        return (m - yy) * w

    r = least_squares(f, [yy.max() * 20, 0.0, 0.06 * E0, yy.min(), 0.0])
    A, sh, fw, a, b = r.x
    return sh, fw, (r.fun ** 2).sum() / (len(x) - 5), E0


def main(paths):
    for path in paths:
        rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
        kev = np.array([float(r['keV']) for r in rows])
        net = np.array([float(r['net']) for r in rows])
        model = np.array([float(r['model']) for r in rows])
        print('== %s' % path)
        print('%-8s | %14s %14s | %14s %14s | %7s %7s' % ('группа', 'сдвиг d, кэВ', 'сдвиг m, кэВ', 'ПШПВ d, кэВ', 'ПШПВ m, кэВ', 'Δ/ПШПВ', 'ПШПВm/d'))
        for label, lo, hi, lines in GROUPS:
            sd, fd, cd, E0 = fit(kev, net, lo, hi, lines)
            sm, fm, cm, _ = fit(kev, model, lo, hi, lines)
            print('%-8s | %+6.1f (%+5.2f%%) %+6.1f (%+5.2f%%) | %6.1f (%5.2f%%) %6.1f (%5.2f%%) | %+7.2f %7.3f' % (
                label, sd, 100 * sd / E0, sm, 100 * sm / E0, fd, 100 * fd / E0, fm, 100 * fm / E0, (sm - sd) / fd, fm / fd))


if __name__ == '__main__':
    main(sys.argv[1:])
