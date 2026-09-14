# -*- coding: utf-8 -*-
"""П67: шкала ФОНА по его собственным пикам — центроиды 352 / 609 / 1120 / 1461 / 1764 / 2614 гауссианами в каналах
(окна ±8 % по файловой шкале, линейная подложка), полином 2-й степени ch → кэВ; печатает остатки.
Файловая шкала у встроенных фонов AS80 — копия полинома пробы (у 2026-го фона на 1461/1764 врёт на −78/−105 кэВ),
линейная по двум опорам ошибается на ±20 кэВ у 352/609."""
import math

import numpy as np
from scipy.ndimage import uniform_filter1d
from scipy.optimize import least_squares

import recal

LINES = [351.93, 609.31, 1120.3, 1460.8, 1764.5, 2614.5]


def centroids(B):
    file_coef = list(B.coef)
    recal.apply(B, file_coef)
    sm = uniform_filter1d(B.counts, 7)
    ch = np.arange(B.n, dtype=float)
    out = []
    for E0 in LINES:
        ids = np.where((B.keV >= 0.92 * E0) & (B.keV < 1.08 * E0) & (ch < B.n - 100))[0]
        c0 = ids[np.argmax(sm[ids])]
        half = int(0.06 * c0) + 5
        ids = np.arange(max(c0 - half, 0), min(c0 + half, B.n - 100))
        x = ids.astype(float)
        y = B.counts[ids]
        w = 1.0 / np.sqrt(np.maximum(y, 1.0))

        def f(p):
            A, c, s, a, b = p
            return (A * np.exp(-0.5 * ((x - c) / abs(s)) ** 2) + a + b * (x - c0) - y) * w
        r = least_squares(f, [max(y.max(), 1.0), float(c0), 0.03 * c0, max(y.min(), 0.0), 0.0])
        out.append((r.x[1], E0))
    return out


def bg_scale(B, deg=2, verbose=False):
    pts = centroids(B)
    chs = np.array([p[0] for p in pts])
    E = np.array([p[1] for p in pts])
    coef = list(np.polyfit(chs, E, deg)[::-1])
    res = E - recal.scale_of(coef, chs)
    if verbose:
        print('   фон: полином %d ст. %s; остатки %s' % (deg, ' '.join('%.6g' % c for c in coef), ' '.join('%+.1f' % r for r in res)))
    recal.apply(B, coef)
    return coef, res
