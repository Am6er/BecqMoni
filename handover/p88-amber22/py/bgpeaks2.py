# -*- coding: utf-8 -*-
"""П88: независимая (питон) оценка центроидов опорных пиков фона в каналах — контроль пробы C#.
Опоры — группами: главная линия + спутники с привязанным положением (ΔE / локальное усиление), общая σ, линейная подложка.
Сид: квадратичная шкала по трём пикам согласованного фильтра (238 / 1461 / 2614), затем две итерации «фит → полином → фит».
Печатает центроиды, остатки полинома 2-й степени по дробным и округлённым каналам."""
import re, sys, io
import numpy as np
from scipy.ndimage import gaussian_filter1d
from scipy.signal import find_peaks
from scipy.optimize import least_squares
sys.stdout.reconfigure(encoding='utf-8')

# (главная E, имя, [спутники E], брать в шкалу?)
GROUPS = [
    (238.632, 'Pb-212',  [242.0],          True),
    (351.932, 'Pb-214',  [338.32, 328.0],  False),   # плечо Ac-228 — контроль, не опора
    (511.0,   'annih',   [],               True),
    (609.312, 'Bi-214',  [583.187],        True),
    (911.204, 'Ac-228',  [968.97, 964.77], True),
    (1460.82, 'K-40',    [],               True),
    (1764.49, 'Bi-214',  [1729.6],         True),
    (2614.51, 'Tl-208',  [],               True),
]

def load(path, which='EnergySpectrum'):
    t = io.open(path, encoding='utf-8-sig', newline='').read()
    m = re.search(r'<%s>(.*?)</%s>' % (which, which), t, re.S)
    es = m.group(1)
    coef = [float(x) for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', re.search(r'<EnergyCalibration>(.*?)</EnergyCalibration>', es, re.S).group(1))]
    cnt = np.array([int(x) for x in re.findall(r'<DataPoint>(-?\d+)</DataPoint>', es)], dtype=float)
    fw = re.search(r'<PowerFwhmCalibration>.*?</CalibrationPeaks>\s*<Coefficients>(.*?)</Coefficients>', t, re.S)
    fwc = [float(x) for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', fw.group(1))]
    return coef, cnt, fwc

def matched_peaks(cnt, fwc):
    n = len(cnt); c2 = cnt.copy(); c2[-100:] = 0
    resp = np.zeros(n)
    for lo in range(100, n - 100, 100):
        s = fwc[0] * (lo + 50) ** fwc[1] / 2.3548
        seg = gaussian_filter1d(c2, s) - gaussian_filter1d(c2, 3 * s)
        resp[lo:lo + 100] = seg[lo:lo + 100]
    pk, _ = find_peaks(resp, distance=40)
    return [(int(p), float(resp[p])) for p in pk if p > 200 and resp[p] > 3]

def seed_quadratic(cnt, fwc):
    pk = matched_peaks(cnt, fwc)
    # 2614 — самый правый значимый пик ниже переполнения; K-40 — самый сильный отклик в 0.5…0.6 канала 2614; 238 — самый сильный отклик в 0.08…0.11
    chs = np.array([p[0] for p in pk]); rs = np.array([p[1] for p in pk])
    c26 = chs[np.argmax(chs)]
    sel = (chs > 0.50 * c26) & (chs < 0.62 * c26); c14 = chs[sel][np.argmax(rs[sel])]
    sel = (chs > 0.08 * c26) & (chs < 0.11 * c26); c02 = chs[sel][np.argmax(rs[sel])]
    p = np.polyfit([c02, c14, c26], [238.632, 1460.82, 2614.51], 2)
    return p[::-1], (c02, c14, c26)

def E_of(coef, ch): return np.polyval(coef[::-1], ch)
def ch_of(coef, E, n):
    g = np.arange(n, dtype=float); return float(np.interp(E, E_of(coef, g), g))

def fit_group(cnt, coef, fwc, E0, sats):
    n = len(cnt)
    c0 = ch_of(coef, E0, n)
    g = (E_of(coef, c0 + 1) - E_of(coef, c0 - 1)) / 2.0
    fw = fwc[0] * c0 ** fwc[1]; s0 = fw / 2.3548
    d = np.array([(Es - E0) / g for Es in sats])
    lo = int(max(0, min([c0] + list(c0 + d)) - 1.7 * fw)); hi = int(min(n - 101, max([c0] + list(c0 + d)) + 1.7 * fw))
    x = np.arange(lo, hi + 1, dtype=float); y = cnt[lo:hi + 1]
    w = 1.0 / np.sqrt(np.maximum(y, 1.0))
    k = len(sats)
    def model(p):
        c, s, a, b = p[0], p[1], p[2], p[3]
        A = p[4]; m = A * np.exp(-0.5 * ((x - c) / s) ** 2) + a + b * (x - c0)
        for j in range(k):
            m = m + p[5 + j] * np.exp(-0.5 * ((x - c - d[j]) / s) ** 2)
        return m
    def f(p): return (model(p) - y) * w
    ymin = y.min(); A0 = max(y[int(c0) - lo] - ymin, 1.0) if lo <= int(c0) <= hi else max(y.max() - ymin, 1.0)
    p0 = [c0, s0, ymin, 0.0, A0] + [0.3 * A0] * k
    lb = [c0 - 0.6 * fw, 0.6 * s0, -np.inf, -np.inf, 0.0] + [0.0] * k
    ub = [c0 + 0.6 * fw, 1.6 * s0, np.inf, np.inf, np.inf] + [np.inf] * k
    r = least_squares(f, p0, bounds=(lb, ub))
    J = r.jac; cov = np.linalg.pinv(J.T @ J); chi2 = np.sum(r.fun ** 2) / max(len(x) - len(p0), 1)
    return dict(c=r.x[0], dc=np.sqrt(cov[0, 0] * max(chi2, 1.0)), fwhm=r.x[1] * 2.3548, fwcal=fw, A=r.x[4], sat=list(r.x[5:]), chi2=chi2, win=(lo, hi), c_exp=c0, g=g)

def run(path, which='EnergySpectrum', verbose=True):
    coef_file, cnt, fwc = load(path, which)
    n = len(cnt)
    coef, seeds = seed_quadratic(cnt, fwc)
    if verbose:
        print('файл: %s [%s], каналов %d, сумма %d' % (path, which, n, int(cnt.sum())))
        print('сид (фильтр): 238 @ %d, 1461 @ %d, 2614 @ %d -> %s' % (seeds[0], seeds[1], seeds[2], ' '.join('%.8g' % v for v in coef)))
    res = None
    for it in range(3):
        rows = []
        for E0, name, sats, use in GROUPS:
            r = fit_group(cnt, coef, fwc, E0, sats); r.update(E0=E0, name=name, use=use); rows.append(r)
        use = [r for r in rows if r['use']]
        chs = np.array([r['c'] for r in use]); E = np.array([r['E0'] for r in use])
        coef = list(np.polyfit(chs, E, 2)[::-1])
    if verbose:
        for r in rows:
            print('  %-7s %8.2f кэВ: окно %5d–%5d, центроид %9.2f ± %.2f кан (сид %.1f), ПШПВ %.1f (калибр. %.1f), A %.0f, спутники %s, χ²/ndf %.2f; E_file(c) = %8.1f (%+.1f); E_new(c) = %+.2f' %
                  (r['name'], r['E0'], r['win'][0], r['win'][1], r['c'], r['dc'], r['c_exp'], r['fwhm'], r['fwcal'], r['A'], ' '.join('%.0f' % v for v in r['sat']), r['chi2'],
                   E_of(coef_file, r['c']), E_of(coef_file, r['c']) - r['E0'], E_of(coef, r['c']) - r['E0']))
        for label, x in (('дробные', chs), ('округл.', np.round(chs))):
            p = np.polyfit(x, E, 2); rr = E - np.polyval(p, x)
            print('полином 2 ст. по %d опорам (%s): %s ; остатки: %s' % (len(use), label, ' '.join('%.12g' % v for v in p[::-1]), ' '.join('%s %+.2f' % (r['name'], v) for r, v in zip(use, rr))))
    return rows, coef

if __name__ == '__main__':
    run(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else 'EnergySpectrum')
