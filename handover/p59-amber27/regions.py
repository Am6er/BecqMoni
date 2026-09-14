# -*- coding: utf-8 -*-
"""П59: проба против фона по областям энергии — сырые отсчёты файла спектра, без модели.
    python handover/p59-amber27/regions.py <спектр.xml> [...]
"""
import io, math, re, sys

def load(p):
    s = io.open(p, encoding='utf-8').read()
    def block(tag):
        i = s.find('<' + tag + '>'); j = s.find('</' + tag + '>', i)
        return s[i:j]
    def parse(es):
        mt = float(re.search('<MeasurementTime>(.*?)</MeasurementTime>', es).group(1))
        sp = [int(x) for x in re.findall('<DataPoint>(.*?)</DataPoint>', re.search('<Spectrum>(.*?)</Spectrum>', es, re.S).group(1))]
        cal = re.search('<EnergyCalibration.*?</EnergyCalibration>', es, re.S).group(0)
        coef = [float(x) for x in re.findall('<Coefficient>(.*?)</Coefficient>', cal)]
        return mt, sp, coef
    return parse(block('EnergySpectrum')), parse(block('BackgroundEnergySpectrum'))

for p in sys.argv[1:]:
    (mt, sp, coef), (bmt, bsp, bcoef) = load(p)
    print(p, 'MT', mt, 'sum', sum(sp), 'cps %.2f' % (sum(sp) / mt), '| bg MT', bmt, 'sum', sum(bsp), 'cps %.2f' % (sum(bsp) / bmt))
    print(' cal', coef, ' bgcal', bcoef, 'channels', len(sp), len(bsp))
    E = lambda ch, c=coef: sum(a * ch ** k for k, a in enumerate(c))
    scale = mt / bmt
    print('  %13s %9s %10s %9s %7s' % ('keV', 'sample', 'bg*t', 'net', 'net/σ'))
    for lo, hi in [(20, 40), (40, 60), (60, 100), (100, 200), (200, 280), (280, 400), (400, 550), (550, 620), (620, 700),
                   (700, 760), (760, 1000), (1000, 1500), (1500, 2000), (2000, 2500), (2500, 2700), (2700, 3000)]:
        a = sum(sp[c] for c in range(len(sp)) if lo <= E(c) < hi)
        b = sum(bsp[c] for c in range(len(bsp)) if lo <= E(c, bcoef) < hi) * scale
        print('  %5d-%5d keV: %9d %10.0f %9.0f %7.1f' % (lo, hi, a, b, a - b, (a - b) / math.sqrt(a + b * scale) if a + b > 0 else 0))
