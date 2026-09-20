# -*- coding: utf-8 -*-
"""П88: НЕЗАВИСИМАЯ (scipy) реализация той же модели, что у RecalibrateBackgroundProbe — положительный контроль подгонщика C#.
Тот же сид (боксар 21, максимумы в областях файловой шкалы 1150–1800 и 2300–2950 кэВ), те же окна ±w·ПШПВ, та же модель
(гауссиана + спутники той же σ на привязанных смещениях + линейная подложка, пуассоновские веса), те же три прохода с кубикой
по опорам {P·r 59.5/238.6/583.2, 911, 1120, 1461, 2614}. Печатает центроиды — сравнивать с §2 пробы (ожидание ≤ 0.1 кан)."""
import re, io, sys
import numpy as np
from scipy.optimize import least_squares
from scipy.ndimage import uniform_filter1d
sys.stdout.reconfigure(encoding='utf-8')

PEAKS = [(238.632, 'Pb-212', [242.0]), (351.932, 'Pb-214', [338.32]), (511.0, 'annih', []), (609.312, 'Bi-214', [583.187]),
         (911.204, 'Ac-228', [968.97]), (1120.29, 'Bi-214', []), (1460.82, 'K-40', []), (1764.49, 'Bi-214', [1729.6]), (2614.51, 'Tl-208', [])]
ANCHOR_PEAKS = {911.204, 1120.29, 1460.82, 2614.51}
TRANSFER = [59.541, 238.632, 583.187]

def load(path, which='EnergySpectrum'):
    t = io.open(path, encoding='utf-8-sig', newline='').read()
    es = re.search(r'<%s>(.*?)</%s>' % (which, which), t, re.S).group(1)
    coef = [float(x) for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', re.search(r'<EnergyCalibration>(.*?)</EnergyCalibration>', es, re.S).group(1))]
    cnt = np.array([int(x) for x in re.findall(r'<DataPoint>(-?\d+)</DataPoint>', es)], dtype=float)
    fw = re.search(r'<PowerFwhmCalibration>.*?</CalibrationPeaks>\s*<Coefficients>(.*?)</Coefficients>', t, re.S)
    fwc = [float(x) for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', fw.group(1))]
    return coef, cnt, fwc

def poly(c, x): return sum(ci * x ** i for i, ci in enumerate(c))
def ch_of(c, E, n):
    g = np.arange(n, dtype=float); return float(np.interp(E, poly(c, g), g))

def fit_group(cnt, c0, fw, d, w, guard):
    n = len(cnt); lo = int(np.floor(min([c0] + [c0 + x for x in d]) - w * fw)); hi = int(np.ceil(max([c0] + [c0 + x for x in d]) + w * fw))
    lo = max(0, lo); hi = min(n - guard - 1, hi)
    x = np.arange(lo, hi + 1, dtype=float); y = cnt[lo:hi + 1]; wt = 1 / np.sqrt(np.maximum(y, 1))
    s0 = fw / 2.3548; k = len(d); ymin = y.min(); ic = min(max(int(round(c0)) - lo, 0), len(y) - 1); A0 = max(y[ic] - ymin, 1.0)
    def f(p):
        c, s, b0, b1, A = p[:5]; m = b0 + b1 * (x - c0) / fw + A * np.exp(-0.5 * ((x - c) / s) ** 2)
        for j in range(k): m = m + p[5 + j] * np.exp(-0.5 * ((x - c - d[j]) / s) ** 2)
        return (m - y) * wt
    p0 = [c0, s0, ymin, 0.0, A0] + [0.3 * A0] * k
    lb = [c0 - 0.6 * fw, 0.6 * s0, -np.inf, -np.inf, 0] + [0] * k; ub = [c0 + 0.6 * fw, 1.6 * s0, np.inf, np.inf, np.inf] + [np.inf] * k
    r = least_squares(f, p0, bounds=(lb, ub), xtol=1e-12, ftol=1e-12, gtol=1e-12)
    J = r.jac; cov = np.linalg.pinv(J.T @ J); chi2 = np.sum(r.fun ** 2) / max(len(x) - len(p0), 1); sc = max(chi2, 1)
    A = r.x[4]; sA = np.sqrt(cov[4, 4] * sc)
    return dict(c=r.x[0], dc=np.sqrt(cov[0, 0] * sc), fwr=r.x[1] * 2.3548 / fw, A=A, sig=A / sA if sA > 0 else 0, chi2=chi2, lo=lo, hi=hi, c0=c0, fw=fw)

def main(path, w=1.0, order=3):
    P, cnt, fwc = load(path); n = len(cnt); guard = 100
    fwhm = lambda c: fwc[0] * c ** fwc[1]
    sm = uniform_filter1d(cnt, 21, mode='nearest')
    def region(E1, E2):
        a = int(round(ch_of(P, E1, n))); b = min(int(round(ch_of(P, E2, n))), n - guard); return a + int(np.argmax(sm[a:b + 1]))
    cK = region(1150, 1800); cT = region(2300, 2950)
    g = (2614.51 - 1460.82) / (cT - cK); coef = [1460.82 - g * cK, g]
    print('сид: K-40 %d, 2614 %d; E = %.3f + %.6f·ch' % (cK, cT, coef[0], coef[1]))
    for it in range(3):
        fits = []
        for E0, name, sats in PEAKS:
            c0 = ch_of(coef, E0, n); gl = (poly(coef, c0 + 1) - poly(coef, c0 - 1)) / 2; fw = fwhm(c0)
            d = [(Es - E0) / gl for Es in sats]
            r = fit_group(cnt, c0, fw, d, w, guard); r['E'] = E0; r['name'] = name
            r['ok'] = r['sig'] >= 5 and r['dc'] <= 0.1 * fw and abs(r['c'] - c0) <= 0.55 * fw and 0.6 <= r['fwr'] <= 1.5
            fits.append(r)
        t26 = [r for r in fits if r['E'] == 2614.51][0]; ratio = t26['c'] / ch_of(P, 2614.51, n)
        xs = [ch_of(P, E, n) * ratio for E in TRANSFER] + [r['c'] for r in fits if r['ok'] and r['E'] in ANCHOR_PEAKS]
        ys = TRANSFER + [r['E'] for r in fits if r['ok'] and r['E'] in ANCHOR_PEAKS]
        coef = list(np.polyfit(xs, ys, order)[::-1])
    print('отношение усилений %.5f' % ratio)
    for r in fits:
        print('  %-7s %8.2f: окно %5d–%5d канал %9.2f ± %.2f  ПШПВ/калибр %.2f  A %.0f  A/σA %.1f  χ²/ndf %.2f %s' % (r['name'], r['E'], r['lo'], r['hi'], r['c'], r['dc'], r['fwr'], r['A'], r['sig'], r['chi2'], '' if r['ok'] else '← не принята'))
    print('кубика по дробным: %s' % ' '.join('%.12g' % c for c in coef))
    # округлённые каналы, как CalibrationPoint
    xi = [round(x) for x in xs]; p = np.polyfit(xi, ys, order)
    print('по целым каналам %s: %s' % (xi, ' '.join('%.12g' % c for c in p[::-1])))
    print('невязки (E − P(ch_int)): %s' % ' '.join('%+.2f' % (E - np.polyval(p, x)) for x, E in zip(xi, ys)))

if __name__ == '__main__':
    main(sys.argv[1], float(sys.argv[2]) if len(sys.argv) > 2 else 1.0)
