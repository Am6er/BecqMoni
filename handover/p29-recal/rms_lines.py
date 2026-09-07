# -*- coding: utf-8 -*-
u"""СКО найденных центров относительно табличных энергий — то, что идёт в
`ecal_rms_kev` манифеста. Берутся только линии, у которых фит сел в обеих
гистограммах (передний план и встроенный фон), чтобы число не зависело от того,
какие окна вообще удалось закрыть."""
import sys, os
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                '..', '..', 'tools', 'CORPUS', 'scripts'))
import xml.etree.ElementTree as ET
import gaussfit

LINES = [(238.632, 'Pb-212'), (295.224, 'Pb-214'), (351.932, 'Pb-214'),
         (583.187, 'Tl-208'), (609.312, 'Bi-214'), (911.204, 'Ac-228'),
         (1120.287, 'Bi-214'), (1460.822, 'K-40'), (1764.494, 'Bi-214'),
         (2614.511, 'Tl-208')]


def load(path):
    r = ET.parse(path).getroot()
    out = {}
    for tag in ('EnergySpectrum', 'BackgroundEnergySpectrum'):
        es = r.find('.//' + tag)
        c = np.array([int(x.text) for x in es.find('Spectrum').findall('DataPoint')], float)
        co = np.array([float(x.text) for x in es.find('EnergyCalibration/Coefficients')])
        out[tag] = dict(counts=c, ecal=co)
    return out


def energy(co, ch):
    return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(co))


def fit(counts, co, e, fw):
    n = len(counts)
    g = np.arange(n, dtype=float)
    ch0 = float(np.interp(e, energy(co, g), g))
    if ch0 < 10 or ch0 > n - 11:
        return None
    sig = fw / 100.0 * 662.0 * np.sqrt(max(e, 1.0) / 662.0) / gaussfit.FWHM_SIGMA
    d = co[1] + 2 * (co[2] if len(co) > 2 else 0) * ch0
    r = gaussfit.fit_peak(counts, ch0, max(sig / d, 1.5), window=2.0)
    return None if r is None else float(energy(co, r['mu']))


path, fw = sys.argv[1], float(sys.argv[2]) if len(sys.argv) > 2 else 7.65
d = load(path)
fg = d['EnergySpectrum']
res = []
for e, nuc in LINES:
    a = fit(fg['counts'], fg['ecal'], e, fw)
    if a is None:
        continue
    res.append((e, a, a - e))
print(u'%-9s %-10s %8s' % (u'E_табл', u'E_найд', u'Δ'))
for e, a, dd in res:
    print(u'%-9.2f %-10.2f %+8.2f' % (e, a, dd))
r = np.array([x[2] for x in res])
print(u'\nлиний: %d   СКО: %.2f кэВ   медиана E_найд/E_табл: %.5f'
      % (len(res), float(np.sqrt((r ** 2).mean())),
         float(np.median([x[1] / x[0] for x in res]))))
