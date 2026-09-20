# -*- coding: utf-8 -*-
"""П88: варианты подгонки (окно × подложка) для опор фона — где центроид устойчив."""
import re, io, sys, numpy as np
from scipy.optimize import least_squares
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, r'D:\BqMoni_Claude\p88\py')
from bgpeaks2 import load
coef_file, cnt, fwc = load(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else 'EnergySpectrum')
n = len(cnt)
G = 0.3555; Z = -12.0     # грубая линейная шкала по вершинам
def fit(E0, sats, halfw, basedeg, share_sigma=True):
    c0 = (E0 - Z) / G; fw = fwc[0] * c0 ** fwc[1]; s0 = fw / 2.3548
    d = np.array([(Es - E0) / G for Es in sats]); k = len(sats)
    lo = int(max(0, min([c0] + list(c0 + d)) - halfw * fw)); hi = int(min(n - 101, max([c0] + list(c0 + d)) + halfw * fw))
    x = np.arange(lo, hi + 1, dtype=float); y = cnt[lo:hi + 1]; w = 1 / np.sqrt(np.maximum(y, 1))
    u = (x - c0) / fw
    def model(p):
        c, s = p[0], p[1]; base = sum(p[2 + j] * u ** j for j in range(basedeg + 1))
        m = base + p[3 + basedeg] * np.exp(-0.5 * ((x - c) / s) ** 2)
        for j in range(k): m = m + p[4 + basedeg + j] * np.exp(-0.5 * ((x - c - d[j]) / s) ** 2)
        return m
    f = lambda p: (model(p) - y) * w
    ymin = y.min(); A0 = max(y[min(max(int(c0) - lo, 0), len(y) - 1)] - ymin, 1.0)
    p0 = [c0, s0, ymin] + [0.0] * basedeg + [A0] + [0.3 * A0] * k
    lb = [c0 - 0.6 * fw, 0.6 * s0] + [-np.inf] * (basedeg + 1) + [0.0] * (1 + k)
    ub = [c0 + 0.6 * fw, 1.6 * s0] + [np.inf] * (basedeg + 1) + [np.inf] * (1 + k)
    r = least_squares(f, p0, bounds=(lb, ub))
    J = r.jac; cov = np.linalg.pinv(J.T @ J); chi2 = np.sum(r.fun ** 2) / max(len(x) - len(p0), 1)
    A = r.x[3 + basedeg]; net = A * r.x[1] * np.sqrt(2 * np.pi)
    return r.x[0], np.sqrt(cov[0, 0] * max(chi2, 1)), r.x[1] * 2.3548 / fw, chi2, net, list(r.x[4 + basedeg:])
LINES = [(238.632, 'Pb-212', [242.0]), (351.932, 'Pb-214', [338.32]), (511.0, 'annih', []), (609.312, 'Bi-214', [583.187]), (911.204, 'Ac-228', [968.97]),
         (1120.29, 'Bi-214', []), (1460.82, 'K-40', []), (1764.49, 'Bi-214', [1729.6]), (2614.51, 'Tl-208', [])]
for E0, name, sats in LINES:
    print('%-7s %8.2f (сид %.0f)' % (name, E0, (E0 - Z) / G))
    for halfw in (0.8, 1.0, 1.3, 1.6):
        for deg in (1, 2):
            c, dc, fwr, chi2, net, sat = fit(E0, sats, halfw, deg)
            print('    окно ±%.1f ПШПВ, подложка %d ст.: c = %8.2f ± %5.2f, ПШПВ/калибр %.2f, χ²/ndf %.2f, нетто %7.0f, спутн. %s -> E_lin %+.1f' % (halfw, deg, c, dc, fwr, chi2, net, ' '.join('%.0f' % v for v in sat), Z + G * c - E0))
