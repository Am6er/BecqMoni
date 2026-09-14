# -*- coding: utf-8 -*-
"""П26 12.09.2026, `AMBER22` п. 2 — ОФЛАЙН-ОПЫТ «кто держит амплитуду»: образы × (1+x), сплайн перефитить.

    python handover/p26-amber22/amber/rescale.py <дамп> [knotFwhm=4] [Elo=30] [Ehi=2800]

Из дампа: net, model, continuum_raw. Сумма образов images = model − continuum_raw (без сплайна).
Для x из сетки −0.2…+0.6: модель_x = (1+x)·images + spline_x, где spline_x — неотрицательный
(по узлам) шапочный сплайн с шагом узлов max(K·ПШПВ(E), 8 кэВ), подобранный NNLS по весам 1/net
в полосе [Elo, Ehi]. Печатает χ²(x)/n, невязку в окнах пиков и между пиками — где сидит минимум.
Если минимум при x ≈ 0 — оптимум фита именно такой, и «держит» структура задачи (континуум образов
против сплайна с этим шагом узлов); если при K=2 минимум уходит к x ≈ +0.3 — держит ШАГ УЗЛОВ.
⚠ Это приближение: у настоящего фита ещё Хубер, дрейф, каскад; образы здесь масштабируются ВСЕ вместе
(равновесие ряда), веса — чистый пуассон.
"""
import csv
import math
import sys

import numpy as np
from scipy.optimize import nnls

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

FWHM662 = 0.0765 * 661.657
PEAKS = [238.632, 338.320, 583.187, 911.204, 2614.511]
GAPS = [(252, 290), (400, 440), (610, 700), (1000, 1500), (1650, 2400)]


def fwhm(e):
    return FWHM662 * math.sqrt(max(e, 1.0) / 661.657)


def hats(kev, k_fwhm, lo, hi):
    knots = [lo]
    while knots[-1] < hi:
        knots.append(knots[-1] + max(k_fwhm * fwhm(knots[-1]), 8.0))
    knots.append(knots[-1] + max(k_fwhm * fwhm(knots[-1]), 8.0))
    knots = np.array(knots)
    B = np.zeros((len(kev), len(knots)))
    for j in range(len(knots)):
        c = knots[j]
        l = knots[j - 1] if j > 0 else c - (knots[1] - c)
        r = knots[j + 1] if j + 1 < len(knots) else c + (c - knots[j - 1])
        B[:, j] = np.clip(np.where(kev < c, (kev - l) / (c - l), (r - kev) / (r - c)), 0, None)
    return B, knots


def main():
    path = sys.argv[1]
    k_fwhm = float(sys.argv[2]) if len(sys.argv) > 2 else 4.0
    lo = float(sys.argv[3]) if len(sys.argv) > 3 else 30.0
    hi = float(sys.argv[4]) if len(sys.argv) > 4 else 2800.0
    rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
    kev = np.array([float(r['keV']) for r in rows])
    net = np.array([float(r['net']) for r in rows])
    model = np.array([float(r['model']) for r in rows])
    spl = np.array([float(r['continuum_raw']) for r in rows])
    sel = (kev >= lo) & (kev < hi)
    kev, net, model, spl = kev[sel], net[sel], model[sel], spl[sel]
    images = model - spl
    w = 1.0 / np.sqrt(np.maximum(net, 1.0))
    B, knots = hats(kev, k_fwhm, lo, hi)
    print('== %s: K=%g ПШПВ, узлов %d, каналов %d, полоса %g..%g' % (path, k_fwhm, len(knots), len(kev), lo, hi))
    smooth = np.convolve(net, np.ones(11) / 11, mode='same')

    def win(e):
        wd = fwhm(e)
        ids = np.where((kev >= e * 0.94) & (kev < e * 1.06))[0]
        cd = kev[ids[np.argmax(smooth[ids])]]
        return (kev >= cd - wd) & (kev < cd + wd)

    pw = [win(e) for e in PEAKS]
    gw = [(kev >= a) & (kev < b) for a, b in GAPS]
    print('%6s %9s | %s | %s' % ('x', 'χ²/n', ' '.join('%7s' % ('пик%d' % int(e)) for e in PEAKS),
                                 ' '.join('%9s' % ('%d-%d' % g) for g in GAPS)))
    for x in [-0.2, -0.1, 0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6]:
        y = net - (1 + x) * images
        c, _ = nnls(B * w[:, None], y * w, maxiter=20000)
        m = (1 + x) * images + B @ c
        chi = float(np.sum(((net - m) * w) ** 2) / len(kev))
        pk = ' '.join('%+6.1f%%' % (100 * (m[s].sum() / net[s].sum() - 1)) for s in pw)
        gp = ' '.join('%+8.1f%%' % (100 * (m[s].sum() / net[s].sum() - 1)) for s in gw)
        print('%+6.2f %9.3f | %s | %s' % (x, chi, pk, gp))


if __name__ == '__main__':
    main()
