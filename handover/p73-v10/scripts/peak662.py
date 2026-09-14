# -*- coding: utf-8 -*-
"""П73 (V10): независимый счёт пика 662 кэВ в спектре RC103 (своим кодом, для сверки с пробой приложения).

  python peak662.py <spectrum.xml> [--lo=578 --hi=746] [--side=30]

Фон — встроенный (BackgroundEnergySpectrum), вычитается по ЖИВОМУ времени (LiveTime; если 0 — MeasurementTime).
Печатает: калибровку, суммы в зоне (fg, bg приведённый, нетто), ковелловскую подложку по боковым окнам, гауссову
подгонку пика (центроид, ПШПВ, площадь) по нетто-спектру, полную скорость счёта.
"""
import sys, math, re, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
import xml.etree.ElementTree as ET
import numpy as np


def load(path):
    root = ET.parse(path).getroot()
    rd = root.find('ResultDataList/ResultData')
    es = rd.find('EnergySpectrum')
    bg = rd.find('BackgroundEnergySpectrum')

    def spec(node):
        coef = [float(c.text) for c in node.find('EnergyCalibration/Coefficients')]
        data = np.array([float(d.text) for d in node.find('Spectrum')])
        lt = float(node.findtext('LiveTime') or 0)
        mt = float(node.findtext('MeasurementTime') or 0)
        return coef, data, lt, mt

    return spec(es), (spec(bg) if bg is not None else None), rd


def energy(coef, ch):
    ch = np.asarray(ch, dtype=float)
    return sum(c * ch ** i for i, c in enumerate(coef))


N_CH = [1024]


def channel(coef, e):
    ch = np.arange(0, N_CH[0])
    en = energy(coef, ch)
    return float(np.interp(e, en, ch))


def main():
    path = sys.argv[1]
    lo, hi, side = 578.0, 746.0, 30.0
    for a in sys.argv[2:]:
        if a.startswith('--lo='): lo = float(a[5:])
        elif a.startswith('--hi='): hi = float(a[5:])
        elif a.startswith('--side='): side = float(a[7:])
    (cf, fg, lt, mt), bgt, rd = load(path)
    tf = lt if lt > 0 else mt
    N_CH[0] = len(fg)
    print('спектр: %s' % path)
    print('  каналов %d, живое %.2f с (MeasurementTime %.0f), отсчётов %.0f, %.3f cps' % (len(fg), tf, mt, fg.sum(), fg.sum() / tf))
    print('  калибровка: %s' % ' '.join('%.6g' % c for c in cf))
    if bgt is None:
        print('  ФОНА НЕТ')
        bg = np.zeros_like(fg); tb = 1.0
    else:
        cb, bg, lb, mb = bgt
        tb = lb if lb > 0 else mb
        print('  фон: живое %.2f с (MeasurementTime %.0f), отсчётов %.0f, %.3f cps; калибровка %s' % (tb, mb, bg.sum(), bg.sum() / tb, 'та же' if cb == cf else 'ДРУГАЯ: ' + ' '.join('%.6g' % c for c in cb)))
        if cb != cf:
            # перекладка фона в шкалу переднего плана по энергиям границ каналов
            ch = np.arange(len(fg) + 1) - 0.5
            efg = energy(cf, ch)
            ebg = energy(cb, np.arange(len(bg) + 1) - 0.5)
            cum = np.concatenate([[0.0], np.cumsum(bg)])
            bg = np.diff(np.interp(efg, ebg, cum))
    k = tf / tb
    net = fg - bg * k
    ch = np.arange(len(fg))
    en = energy(cf, ch)
    c_lo, c_hi = int(math.ceil(channel(cf, lo))), int(math.floor(channel(cf, hi)))
    zone = slice(c_lo, c_hi + 1)
    fgz, bgz = fg[zone].sum(), bg[zone].sum() * k
    netz = fgz - bgz
    sig = math.sqrt(fgz + bg[zone].sum() * k * k)
    print('зона %.0f-%.0f кэВ = каналы %d..%d (%d кан.)' % (lo, hi, c_lo, c_hi, c_hi - c_lo + 1))
    print('  fg %.0f, фон приведённый %.1f (k=%.5f), нетто %.1f ± %.1f -> %.4f ± %.4f cps' % (fgz, bgz, k, netz, sig, netz / tf, sig / tf))
    # ковелл по нетто-спектру: боковые окна шириной side кэВ снаружи зоны
    l0, l1 = int(math.ceil(channel(cf, lo - side))), c_lo - 1
    r0, r1 = c_hi + 1, int(math.floor(channel(cf, hi + side)))
    L, R = net[l0:l1 + 1].sum(), net[r0:r1 + 1].sum()
    nL, nR, nZ = l1 - l0 + 1, r1 - r0 + 1, c_hi - c_lo + 1
    base = nZ * 0.5 * (L / nL + R / nR)
    cov = netz - base
    sig_cov = math.sqrt(fgz + bg[zone].sum() * k * k + (nZ * 0.5 / nL) ** 2 * (fg[l0:l1 + 1].sum() + bg[l0:l1 + 1].sum() * k * k) + (nZ * 0.5 / nR) ** 2 * (fg[r0:r1 + 1].sum() + bg[r0:r1 + 1].sum() * k * k))
    print('  ковелл (боковые %d/%d кан. по %.0f кэВ): подложка %.1f, нетто над подложкой %.1f ± %.1f -> %.4f ± %.4f cps' % (nL, nR, side, base, cov, sig_cov, cov / tf, sig_cov / tf))
    # гауссова подгонка по нетто-спектру с линейной подложкой в широком окне
    w0, w1 = int(math.ceil(channel(cf, lo - side))), int(math.floor(channel(cf, hi + side)))
    x = en[w0:w1 + 1]; y = net[w0:w1 + 1]
    err = np.sqrt(np.maximum(fg[w0:w1 + 1] + bg[w0:w1 + 1] * k * k, 1.0))
    try:
        from scipy.optimize import curve_fit
        def model(x, A, mu, s, a, b):
            return A * np.exp(-0.5 * ((x - mu) / s) ** 2) + a + b * (x - 662.0)
        i0 = np.argmax(y)
        p0 = [y[i0], x[i0], 25.0, y[:5].mean(), 0.0]
        popt, pcov = curve_fit(model, x, y, p0=p0, sigma=err, absolute_sigma=True)
        A, mu, s, a, b = popt
        dA, dmu, ds = np.sqrt(np.diag(pcov))[:3]
        dx = np.gradient(x)  # кэВ на канал
        area = A * abs(s) * math.sqrt(2 * math.pi) / np.mean(dx[abs(x - mu) < 2 * abs(s)])
        darea = area * math.sqrt((dA / A) ** 2 + (ds / s) ** 2)
        fwhm = 2.3548 * abs(s)
        print('  гаусс+линия (%.0f-%.0f кэВ): центроид %.2f ± %.2f кэВ, ПШПВ %.2f кэВ (%.2f %%), площадь %.1f ± %.1f -> %.4f ± %.4f cps' % (
            x[0], x[-1], mu, dmu, fwhm, fwhm / mu * 100, area, darea, area / tf, darea / tf))
        chi2 = (((y - model(x, *popt)) / err) ** 2).sum() / (len(x) - 5)
        print('  chi2/ndf подгонки %.2f; интервал ±1.5 ПШПВ = %.1f-%.1f кэВ' % (chi2, mu - 1.5 * fwhm, mu + 1.5 * fwhm))
    except Exception as e:
        print('  подгонка не вышла: %s' % e)


if __name__ == '__main__':
    main()
