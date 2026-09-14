# -*- coding: utf-8 -*-
"""П56: подгонка области спектра суммой компонентов (каждый — набор линий (E, I) со связанными интенсивностями через
кривую эффективности сцены, одна свободная амплитуда >= 0) + полиномиальная подложка; ширина — ПШПВ(E) модели шкалы
(общий множитель k свободен или зажат). Веса пуассон по raw + фон·k². Печатает площадь опорной линии, σ, z, χ²/n."""
import math
import numpy as np
from scipy.optimize import least_squares

S2F = 2 * math.sqrt(2 * math.log(2))


class Comp:
    def __init__(self, name, lines, ref=None, fixed_shift=0.0):
        self.name = name
        self.lines = lines
        self.ref = ref if ref is not None else max(lines, key=lambda l: l[1])[0]
        self.shift = fixed_shift


def fit_region(S, ab, eff, lo, hi, comps, deg=2, kfree=True, shiftfree=False, verbose=True, label=''):
    """S — Spec с исправленной шкалой; ab — ПШПВ²=a+b·E; eff — Eff; comps — список Comp.
    Возвращает dict name→(A, σA, z): A — площадь опорной линии компонента (отсчёты); плюс χ²/n."""
    net, bgc = S.net()
    k = S.live / S.bg.live if S.bg else 0.0
    var = S.counts + (bgc * k if S.bg is not None else 0.0)   # дисперсия net: raw + k²·bg = raw + k·(bg·k)
    ids = np.where((S.keV >= lo) & (S.keV < hi) & (np.arange(S.n) < S.n - 100))[0]
    x = S.keV[ids]
    y = net[ids]
    w = 1 / np.sqrt(np.maximum(var[ids], 1.0))
    dx = np.diff(S.keVb)[ids]          # ширина канала, кэВ (площадь гауссианы = A → отсчёты)
    Em = 0.5 * (lo + hi)
    nc = len(comps)
    npoly = deg + 1

    def model(p):
        amps = p[:nc]
        kf = p[nc]
        sh = p[nc + 1]
        poly = p[nc + 2:]
        m = np.zeros_like(x)
        for j in range(npoly):
            m += poly[j] * ((x - Em) / 100.0) ** j
        for c, A in zip(comps, amps):
            eref = eff(c.ref)
            Iref = [I for E, I in c.lines if E == c.ref][0]
            for E, I in c.lines:
                s = kf * math.sqrt(max(ab[0] + ab[1] * E, 1.0)) / S2F
                area = A * I * eff(E) / (Iref * eref)
                m += area * np.exp(-0.5 * ((x - (E + sh + c.shift)) / s) ** 2) / (s * math.sqrt(2 * math.pi)) * dx
        return m

    def f(p):
        return (model(p) - y) * w

    p0 = [max(y.max(), 1.0) * 10.0] * nc + [1.0, 0.0] + [max(y.min(), 0.0)] + [0.0] * deg
    lb = [0.0] * nc + [0.6 if kfree else 1.0, -8.0 if shiftfree else -1e-9] + [-np.inf] * npoly
    ub = [np.inf] * nc + [1.6 if kfree else 1.0 + 1e-9, 8.0 if shiftfree else 1e-9] + [np.inf] * npoly
    r = least_squares(f, p0, bounds=(lb, ub))
    J = r.jac
    n = len(x)
    ndf = max(n - len(p0), 1)
    chi2n = (r.fun ** 2).sum() / ndf
    try:
        cov = np.linalg.inv(J.T @ J) * max(chi2n, 1.0)
        sig = np.sqrt(np.maximum(np.diag(cov), 0))
    except np.linalg.LinAlgError:
        sig = np.full(len(p0), np.nan)
    out = {'chi2n': chi2n, 'k': r.x[nc], 'shift': r.x[nc + 1], 'n': n}
    if verbose:
        print('-- %s [%g..%g кэВ], n=%d, χ²/n=%.2f, k(ПШПВ)=%.3f, сдвиг=%+.2f кэВ' % (label, lo, hi, n, chi2n, r.x[nc], r.x[nc + 1]))
    for j, c in enumerate(comps):
        A, sA = r.x[j], sig[j]
        z = A / sA if sA > 0 else float('nan')
        out[c.name] = (A, sA, z)
        if verbose:
            print('   %-26s опора %7.1f кэВ: площадь %10.0f ± %8.0f  z=%6.1f' % (c.name, c.ref, A, sA, z))
    out['model'] = model(r.x)
    out['x'] = x
    out['y'] = y
    return out
