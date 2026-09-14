# -*- coding: utf-8 -*-
"""Площадь пика прямым суммированием, с вычитанием встроенного фона —
ровно как это делает real.cs, чтобы числа были сравнимы."""
import sys, xml.etree.ElementTree as ET

def spectra(path):
    r = ET.parse(path).getroot()
    out = []
    for el in r.iter():
        t = el.tag.split('}')[-1]
        if t in ('EnergySpectrum', 'BackgroundEnergySpectrum'):
            ch, coef, live, meas = None, None, 0.0, 0.0
            for c in el.iter():
                tt = c.tag.split('}')[-1]
                if tt == 'Spectrum' and ch is None:
                    ch = [int(x.text) for x in c]
                    if not ch and c.text: ch = [int(v) for v in c.text.split()]
                elif tt == 'Coefficients' and coef is None:
                    coef = [float(x.text) for x in c]
                elif tt == 'LiveTime': live = float(c.text)
                elif tt == 'MeasurementTime': meas = float(c.text)
            out.append((t, ch, coef, live if live > 0 else meas))
    return out

def energy(coef, c): return sum(a*c**i for i, a in enumerate(coef))
def chan(coef, e, n):
    lo, hi = 0.0, float(n-1)
    for _ in range(80):
        m = 0.5*(lo+hi)
        if energy(coef, m) < e: lo = m
        else: hi = m
    return 0.5*(lo+hi)

path, e_line, fwhm_pct = sys.argv[1], float(sys.argv[2]), float(sys.argv[3])
sp = spectra(path)
main = [s for s in sp if s[0] == 'EnergySpectrum'][0]
bg = [s for s in sp if s[0] == 'BackgroundEnergySpectrum']
ch, coef, live = main[1], main[2], main[3]
n = len(ch)
net = [float(v) for v in ch]
if bg and bg[0][1] and len(bg[0][1]) == n and bg[0][3] > 0:
    k = live / bg[0][3]
    net = [net[i] - k*bg[0][1][i] for i in range(n)]
    print('вычтен встроенный фон, коэффициент %.4f' % k)
c0 = chan(coef, e_line, n)
per = energy(coef, c0+0.5) - energy(coef, c0-0.5)
f = e_line*fwhm_pct/100.0/per
print('линия %.1f кэВ -> канал %.1f, ПШПВ %.1f кан (%.1f кэВ), живое %.0f с'
      % (e_line, c0, f, f*per, live))
for w in (1.5, 2.0, 2.5, 3.0):
    lo, hi = int(round(c0-w*f)), int(round(c0+w*f))
    g = int(round(0.8*f))
    l0, l1, r0, r1 = lo-2*g, lo-1, hi+1, hi+2*g
    if l0 < 0 or r1 >= n: continue
    gross = sum(net[lo:hi+1])
    left = sum(net[l0:l1+1])/(l1-l0+1)
    right = sum(net[r0:r1+1])/(r1-r0+1)
    base = 0.5*(left+right)*(hi-lo+1)
    print('  ±%.1f ПШПВ [%5d..%5d]  брутто %8.0f  подложка %8.0f  НЕТТО %8.0f'
          % (w, lo, hi, gross, base, gross-base))
