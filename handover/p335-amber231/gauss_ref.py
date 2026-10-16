"""Корпус, все спектры с 137CS: FWHM половинной высоты (прежний и новый счёт) против подгонки
гаусс + линия (МНК, веса Пуассона) в том же окне ±1.5 FWHM модели."""
import math, os, sys, csv, statistics
import numpy as np
from scipy.optimize import least_squares
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fwhm_step_corpus import load, energy, channel_of

def half_width(y, a, b, mode):
    s, e = y[a], y[b]
    base = lambda ch: s + (e - s) * (ch - a) / (b - a)
    net = lambda ch: y[ch] - base(ch)
    c = max(range(a, b + 1), key=net)
    top = net(c)
    def cross(half):
        l = r = None
        for j in range(a + 1, c + 1):
            if net(j) > half:
                if not net(j) > net(j-1): return None
                l = (j - 1) + (half - net(j-1)) / (net(j) - net(j-1)); break
        for k in range(b - 1, c - 1, -1):
            if net(k) > half:
                if not net(k) > net(k+1): return None
                r = (k + 1) - (half - net(k+1)) / (net(k) - net(k+1)); break
        return None if l is None or r is None else (l, r)
    lr = cross(top / 2)
    if lr is None: return None
    if mode == 'old': return lr[1] - lr[0]
    w0 = lr[1] - lr[0]; h = max(1, int(round(0.35 * w0)))
    if c - h < a or c + h > b: return w0
    ts = range(-h, h + 1); vals = [net(c + t) for t in ts]
    if min(vals) <= 0: return w0
    ys = [math.log(v) for v in vals]
    s0 = len(vals); s2 = sum(t*t for t in ts); s4 = sum(t**4 for t in ts)
    v0 = sum(ys); v1 = sum(yy*t for yy, t in zip(ys, ts)); v2 = sum(yy*t*t for yy, t in zip(ys, ts))
    det = s0*s4 - s2*s2; p1 = v1/s2; p0 = (v0*s4 - v2*s2)/det; p2 = (s0*v2 - s2*v0)/det
    if not p2 < 0 or abs(-p1/(2*p2)) > h: return w0
    lr2 = cross(math.exp(p0 - p1*p1/(4*p2)) / 2)
    return w0 if lr2 is None else lr2[1] - lr2[0]

corpus = sys.argv[1]
rows = [r for r in csv.DictReader(open(os.path.join(corpus, 'manifest.csv'), encoding='utf-8-sig')) if '137CS' in r['nuclides'].upper()]
parts = {r['spectrum']: r['part'] for r in csv.DictReader(open(os.path.join(corpus, 'parts.csv'), encoding='utf-8-sig'))}
print('{:<24} {:>7} {:>8} {:>8} {:>8} {:>8}'.format('спектр', 'A', 'фит,кан', 'прежн.%', 'нов.%', '±фит %'))
do, dn = [], []
for r in rows:
    key = r['key']
    coeffs, y = load(os.path.join(corpus, 'spectra', key + '.xml'), int(r['result_data'] or 0))
    n = len(y); c0 = channel_of(coeffs, 661.657, n)
    dedc = energy(coeffs, c0 + .5) - energy(coeffs, c0 - .5)
    fw = float(r['fwhm_662_pct']) / 100 * 661.657 / dedc
    lo, hi = int(c0 - .5*fw), int(c0 + .5*fw) + 1
    top = max(range(lo, hi), key=lambda i: (y[i-1] + y[i] + y[i+1]) / 3)
    a, b = int(round(top - 1.5*fw)), int(round(top + 1.5*fw))
    x = np.arange(a, b + 1, dtype=float); yy = np.array(y[a:b+1], dtype=float)
    w = 1.0 / np.sqrt(np.maximum(yy, 1.0))
    def res(p):
        A, mu, sg, b0, b1 = p
        return (A*np.exp(-0.5*((x-mu)/sg)**2) + b0 + b1*(x-top) - yy) * w
    p0 = [max(yy.max() - min(yy[0], yy[-1]), 1), top, fw/2.3548, min(yy[0], yy[-1]), 0.0]
    fit = least_squares(res, p0)
    J = fit.jac; cov = np.linalg.pinv(J.T @ J) * max(1.0, (fit.fun**2).sum() / (len(x) - 5))
    fwfit = 2.3548 * abs(fit.x[2]); efit = 2.3548 * math.sqrt(max(cov[2, 2], 0)) / fwfit
    wo = half_width(y, a, b, 'old'); wn = half_width(y, a, b, 'new')
    d_o = wo / fwfit - 1; d_n = wn / fwfit - 1
    do.append(d_o); dn.append(d_n)
    print('{:<24} {:>7.0f} {:>8.2f} {:>+8.2f} {:>+8.2f} {:>8.2f}  {}'.format(key, fit.x[0], fwfit, 100*d_o, 100*d_n, 100*efit, parts.get(key)))
print()
print('часть known, спектров {}: половинная высота против подгонки гаусса'.format(len(do)))
print('  прежний: медиана {:+.2f} %, средняя {:+.2f} %, медиана |откл.| {:.2f} %'.format(100*statistics.median(do), 100*statistics.fmean(do), 100*statistics.median([abs(v) for v in do])))
print('  новый  : медиана {:+.2f} %, средняя {:+.2f} %, медиана |откл.| {:.2f} %'.format(100*statistics.median(dn), 100*statistics.fmean(dn), 100*statistics.median([abs(v) for v in dn])))
