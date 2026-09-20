# -*- coding: utf-8 -*-
"""П88: устойчивость центроида к окну при подложке «линейная + ступенька» (стандарт для NaI)."""
import re, io, sys, numpy as np
from scipy.optimize import least_squares
from scipy.special import erfc
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, r'D:\BqMoni_Claude\p88\py')
from bgpeaks2 import load
coef_file, cnt, fwc = load(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else 'EnergySpectrum')
n = len(cnt)
G = 0.3555; Z = -12.0
def fit(E0, sats, halfw, step):
    c0 = (E0 - Z) / G; fw = fwc[0] * c0 ** fwc[1]; s0 = fw / 2.3548
    d = np.array([(Es - E0) / G for Es in sats]); k = len(sats)
    lo = int(max(0, min([c0] + list(c0 + d)) - halfw * fw)); hi = int(min(n - 101, max([c0] + list(c0 + d)) + halfw * fw))
    x = np.arange(lo, hi + 1, dtype=float); y = cnt[lo:hi + 1]; w = 1 / np.sqrt(np.maximum(y, 1))
    u = (x - c0) / fw
    def model(p):
        c, s, b0, b1, A = p[:5]; m = b0 + b1 * u + A * np.exp(-0.5 * ((x - c) / s) ** 2)
        if step: m = m + p[5] * 0.5 * erfc((x - c) / (s * np.sqrt(2)))
        for j in range(k): m = m + p[5 + step + j] * np.exp(-0.5 * ((x - c - d[j]) / s) ** 2)
        return m
    f = lambda p: (model(p) - y) * w
    ymin = y.min(); A0 = max(y[min(max(int(c0) - lo, 0), len(y) - 1)] - ymin, 1.0)
    p0 = [c0, s0, ymin, 0.0, A0] + ([0.0] if step else []) + [0.3 * A0] * k
    lb = [c0 - 0.6 * fw, 0.6 * s0, -np.inf, -np.inf, 0.0] + ([0.0] if step else []) + [0.0] * k
    ub = [c0 + 0.6 * fw, 1.6 * s0, np.inf, np.inf, np.inf] + ([np.inf] if step else []) + [np.inf] * k
    r = least_squares(f, p0, bounds=(lb, ub))
    J = r.jac; cov = np.linalg.pinv(J.T @ J); chi2 = np.sum(r.fun ** 2) / max(len(x) - len(p0), 1)
    return r.x[0], np.sqrt(cov[0, 0] * max(chi2, 1)), r.x[1] * 2.3548 / fw, chi2, (r.x[5] if step else 0.0), r.x[4]
LINES = [(609.312, 'Bi-214', [583.187]), (911.204, 'Ac-228', [968.97]), (1120.29, 'Bi-214', []), (1460.82, 'K-40', []), (1764.49, 'Bi-214', [1729.6]), (2614.51, 'Tl-208', [])]
for E0, name, sats in LINES:
    print('%-7s %8.2f' % (name, E0))
    for halfw in (0.8, 1.0, 1.3, 1.6, 2.0):
        row = []
        for step in (0, 1):
            c, dc, fwr, chi2, st, A = fit(E0, sats, halfw, step)
            row.append('%s: c=%8.2f±%5.2f w=%.2f χ²=%.2f ступ=%.0f A=%.0f' % ('лин ' if not step else 'лин+ступ', c, dc, fwr, chi2, st, A))
        print('    ±%.1f  %s | %s' % (halfw, row[0], row[1]))
