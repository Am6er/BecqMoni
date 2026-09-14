# -*- coding: utf-8 -*-
"""П31 `A308` — сверка ДВУХ метрик на дампах сцены Amber: мера П29 (Σ(net−model)²/max(net,1)) против
меры РЕШАТЕЛЯ (Σ(y−model)²/var, y = raw − bg·s, var = max(raw,1) + max(bg·s·s, s²)), по полосам."""
import csv, sys, re
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
xml = open(r'C:\Users\moroz\bqp31_amber\Th-232_amber.xml', encoding='utf-8').read()
def block(tag):
    m = re.search('<%s>(.*?)</%s>' % (tag, tag), xml, re.S)
    return m.group(1)
def spec(tag):
    b = block(tag)
    s = re.search('<Spectrum>(.*?)</Spectrum>', b, re.S).group(1)
    vals = [int(v) for v in re.findall(r'<int>(-?\d+)</int>', s)]
    if not vals:
        vals = [int(v) for v in re.findall(r'<unsignedInt>(\d+)</unsignedInt>', s)]
    if not vals:
        vals = [int(v) for v in re.findall(r'>(-?\d+)<', s)]
    lt = float(re.search('<LiveTime>([^<]*)</LiveTime>', b).group(1))
    return np.array(vals, float), lt
raw, lt = spec('EnergySpectrum'); bg, blt = spec('BackgroundEnergySpectrum')
s = lt / blt
print('channels', len(raw), len(bg), 'scale', round(s, 5))
y = raw - bg * s
var = np.maximum(raw, 1.0) + np.maximum(np.abs(bg * s) * s, s * s)
base = sys.argv[1] if len(sys.argv) > 1 else 'handover/p31-a308-nnls/amber'
for arm in sys.argv[2:] or ['infer', 'huber0', 'h3_noanch', 'h0_noanch']:
    rows = list(csv.DictReader(open('%s/%s/dump.csv' % (base, arm), encoding='utf-8-sig')))
    kev = np.array([float(r['keV']) for r in rows]); net = np.array([float(r['net']) for r in rows]); model = np.array([float(r['model']) for r in rows])
    sel = (kev >= 15) & (kev < 2800)
    print('%-10s net==y max|Δ| %.3f' % (arm, np.abs(net - y)[sel].max()))
    for lo, hi in [(15, 2800), (15, 100), (100, 200), (200, 700), (700, 2000), (2000, 2800)]:
        b = (kev >= lo) & (kev < hi)
        p29 = ((net - model) ** 2 / np.maximum(net, 1.0))[b].sum()
        solv = ((y - model) ** 2 / var)[b].sum()
        print('   %5d-%-5d n=%5d  П29 %10.1f (%.2f/кан)  решатель %10.1f (%.2f/кан)' % (lo, hi, b.sum(), p29, p29 / b.sum(), solv, solv / b.sum()))
