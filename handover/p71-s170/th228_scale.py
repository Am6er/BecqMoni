# -*- coding: utf-8 -*-
"""П71 (S170), пункт (в): шкала ОДИНОЧНЫХ квантов у верха диапазона — по Th-228 (Tl-208 583.19, 860.56, 2614.51;
Bi-212 727.33) и Y-88 (898.04, 1836.06): центроиды в каналах, аффинная шкала по двум нижним линиям,
и куда относительно неё ложится верхняя. Если одиночный 2614 ложится ВЫШЕ аффинной экстраполяции так же,
как сумм-пик Co-60 (+11…+14 кан), сдвиг суммы — свойство шкалы одиночных квантов, а не суммирования.
"""
import sys, math
import xml.etree.ElementTree as ET
import numpy as np
from scipy.optimize import curve_fit

def load(path):
    t = ET.parse(path).getroot()
    es = t.find('.//ResultData/EnergySpectrum')
    coef = [float(c.text) for c in es.findall('EnergyCalibration/Coefficients/Coefficient')]
    data = np.array([int(x.text) for x in es.findall('Spectrum/DataPoint')], dtype=float)
    return coef, data

def energy(coef, ch):
    return sum(c * ch ** i for i, c in enumerate(coef))

def channel(coef, e):
    lo, hi = 0.0, 4096.0
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        if energy(coef, mid) < e: lo = mid
        else: hi = mid
    return 0.5 * (lo + hi)

def gauss_lin(ch, A, mu, sig, b0, b1):
    return A * np.exp(-0.5 * ((ch - mu) / sig) ** 2) + b0 + b1 * (ch - ch[0])

def centroid(coef, data, e, half_kev):
    c0 = channel(coef, e); w = channel(coef, e + half_kev) - c0
    lo, hi = int(c0 - w), int(c0 + w) + 1
    ch = np.arange(lo, hi, dtype=float); y = data[lo:hi]
    imax = lo + int(np.argmax(y))
    p0 = [y.max(), imax, w / 3, y[0], 0.0]
    popt, _ = curve_fit(gauss_lin, ch, y, p0=p0, sigma=np.sqrt(np.maximum(y, 1)), maxfev=20000)
    return popt[1], 2.3548 * popt[2], popt[0] * popt[2] * math.sqrt(2 * math.pi)

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    path = sys.argv[1]
    lines = [float(x) for x in sys.argv[2].split(',')]
    half = float(sys.argv[3]) if len(sys.argv) > 3 else 60.0
    coef, data = load(path)
    cs = []
    print('== %s' % path.split('\\')[-1].split('/')[-1])
    for e in lines:
        mu, fw, area = centroid(coef, data, e, half * (e / 662.0) ** 0.5)
        cs.append(mu)
        print('  %8.2f кэВ: центроид %8.2f кан = %8.1f кэВ по калибровке (%+6.1f), ПШПВ %.1f кан, площадь %.0f' % (
            e, mu, energy(coef, mu), energy(coef, mu) - e, fw, area))
    g = (lines[1] - lines[0]) / (cs[1] - cs[0]); z = lines[0] - g * cs[0]
    print('  аффинная по %.0f/%.0f: %.4f кэВ/кан, ноль %.2f' % (lines[0], lines[1], g, z))
    for e, c in zip(lines[2:], cs[2:]):
        print('    %8.2f кэВ: ожидание %8.2f кан, измерено %8.2f: %+6.2f кан = %+6.1f кэВ по аффинной' % (
            e, (e - z) / g, c, c - (e - z) / g, g * c + z - e))
