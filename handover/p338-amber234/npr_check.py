"""Непропорциональность в КАНАЛАХ сырого спектра черники: прямая АЦП по 661.66 и 1460.82 кэВ,
куда по ней ложится пик K-рентгена Ba (взвешенная энергия ~32.9 кэВ с Kβ) и пики фона."""
import sys
import numpy as np
from scipy.optimize import curve_fit
sys.stdout.reconfigure(encoding='utf-8')
G = 'D:/BqMoni_Claude/p338/g4/'


def csv(fn):
    c = []
    for line in open(G + fn, encoding='utf-8'):
        if line.startswith('#') or line.startswith('channel'):
            continue
        c.append(float(line.split(',')[1]))
    y = np.array(c); y[-1] = 0
    return y


def g_lin(x, a, mu, s, b0, b1):
    return a * np.exp(-0.5 * ((x - mu) / s) ** 2) + b0 + b1 * (x - mu)


def peak(y, lo, hi, mu0):
    x = np.arange(lo, hi, dtype=float); v = y[lo:hi]
    p0 = [v.max() - v.min(), mu0, (hi - lo) / 6, v.min(), 0.0]
    q, cv = curve_fit(g_lin, x, v, p0=p0, sigma=np.sqrt(np.maximum(v, 1)), maxfev=20000)
    return q[1], np.sqrt(cv[1, 1]), abs(q[2]) * 2.3548


for fn, peaks in (('berry_dried_marinelli_1l_482g_49.8h.csv', {'Ba K (~32.9)': (55, 100, 75), 'Cs 661.66': (1380, 1650, 1513), 'K 1460.82': (3080, 3400, 3240)}),
                  ('bg_marinelli_water_1l_33.2h.csv', {'Pb K / горб (~75–80)': (150, 230, 185), 'Pb-214 351.93': (760, 840, 800), 'Bi-214 609.32': (1300, 1420, 1360), 'K 1460.82': (3080, 3400, 3240)})):
    y = csv(fn)
    res = {k: peak(y, *v) for k, v in peaks.items()}
    hi = [k for k in res if k.startswith('K 1460')][0]
    mid = [k for k in res if k.startswith(('Cs', 'Bi'))][0]
    e1, e2 = float(mid.split()[-1]), 1460.82
    c1, c2 = res[mid][0], res[hi][0]
    gain = (e2 - e1) / (c2 - c1); zero = e1 - gain * c1
    print(f'== {fn}: прямая АЦП по {mid} и K-40: E = {zero:+.2f} + {gain:.5f}·канал; порог (канал 36) → {zero + gain * 36:.1f} кэВ')
    for k, (mu, dmu, fw) in res.items():
        print(f'   {k:22s} канал {mu:8.2f} ± {dmu:.2f}  ПШПВ {fw:6.1f} кан → E по прямой {zero + gain * mu:8.2f} кэВ, ПШПВ {fw * gain:5.1f} кэВ')
