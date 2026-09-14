# -*- coding: utf-8 -*-
"""П71 (S170): площадь сумм-пика Co-60 в данных БЕЗ модели — гауссиана + ступенчатая подложка
(совпадательный континуум пары кончается ровно на энергии суммы, то есть под пиком он обрывается в его центре,
размытый разрешением: классическая «комптоновская ступень» под фотопиком) + постоянная (случайные наложения,
фон), МНК по каналам с пуассоновскими весами; для сравнения — экспоненциальная подложка (та, что первой была
в этом скрипте: она не держит плоский континуум слева от пика и занижает подложку) и линейная по боковым полосам.
Запуск: python sumfit.py <spectrum.xml> [lo_ch hi_ch]
"""
import sys, io, math
import xml.etree.ElementTree as ET
import numpy as np
from scipy.optimize import curve_fit
from scipy.special import erf

def load(path):
    t = ET.parse(path).getroot()
    es = t.find('.//ResultData/EnergySpectrum')
    coef = [float(c.text) for c in es.findall('EnergyCalibration/Coefficients/Coefficient')]
    data = np.array([int(x.text) for x in es.findall('Spectrum/DataPoint')], dtype=float)
    return coef, data

def energy(coef, ch):
    return sum(c * ch ** i for i, c in enumerate(coef))

def g_step(ch, A, mu, sig, B, C):
    return A * np.exp(-0.5 * ((ch - mu) / sig) ** 2) + B * 0.5 * (1.0 - erf((ch - mu) / (sig * math.sqrt(2.0)))) + C

def g_exp(ch, A, mu, sig, B, lam, C):
    return A * np.exp(-0.5 * ((ch - mu) / sig) ** 2) + B * np.exp(-(ch - ch[0]) / lam) + C

def report(name, ch, y, coef, popt, pcov, bgfun):
    A, mu, sig = popt[0], popt[1], popt[2]
    err = np.sqrt(np.diag(pcov))
    area = A * sig * math.sqrt(2 * math.pi)
    darea = area * math.hypot(err[0] / A, err[2] / sig)
    model = g_step(ch, *popt) if bgfun == 'step' else g_exp(ch, *popt)
    chi2 = float((((y - model) / np.sqrt(np.maximum(y, 1.0))) ** 2).sum()) / (len(y) - len(popt))
    bg = model - A * np.exp(-0.5 * ((ch - mu) / sig) ** 2)
    m3 = (ch > mu - 3 * sig) & (ch < mu + 3 * sig)
    print('  %-5s χ²/ndf %.2f: площадь %.0f ± %.0f, центр %.2f кан = %.1f кэВ, σ %.2f кан → ПШПВ %.1f кан = %.1f кэВ; подложка под ±3σ %.0f, gross ±3σ %.0f; уровни слева %.1f / справа %.1f на кан' % (
        name, chi2, area, darea, mu, energy(coef, mu), sig, 2.3548 * sig,
        energy(coef, mu + 1.1774 * sig) - energy(coef, mu - 1.1774 * sig), float(bg[m3].sum()), float(y[m3].sum()),
        float(bg[:5].mean()), float(bg[-5:].mean())))
    return area

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    path = sys.argv[1]
    lo = int(sys.argv[2]) if len(sys.argv) > 2 else 790
    hi = int(sys.argv[3]) if len(sys.argv) > 3 else 930
    coef, data = load(path)
    ch = np.arange(lo, hi, dtype=float)
    y = data[lo:hi]
    imax = lo + int(np.argmax(y[30:-30])) + 30
    sigma = np.sqrt(np.maximum(y, 1.0))
    print('%s: каналы %d..%d' % (path.split('\\')[-1].split('/')[-1], lo, hi))
    p0 = [y.max(), imax, 12.0, max(y[:10].mean() - y[-10:].mean(), 1.0), max(y[-10:].mean(), 0.1)]
    popt, pcov = curve_fit(g_step, ch, y, p0=p0, sigma=sigma, absolute_sigma=True, maxfev=20000,
                           bounds=([0, lo, 3, 0, 0], [np.inf, hi, 60, np.inf, 100]))
    a_step = report('СТУП', ch, y, coef, popt, pcov, 'step')
    p0 = [y.max(), imax, 12.0, max(y[0], 1.0), 30.0, 0.5]
    popt, pcov = curve_fit(g_exp, ch, y, p0=p0, sigma=sigma, absolute_sigma=True, maxfev=20000,
                           bounds=([0, lo, 3, 0, 3, 0], [np.inf, hi, 60, np.inf, 500, 50]))
    a_exp = report('ЭКСП', ch, y, coef, popt, pcov, 'exp')
    print('  ступень/экспонента: %.3f' % (a_step / a_exp))
