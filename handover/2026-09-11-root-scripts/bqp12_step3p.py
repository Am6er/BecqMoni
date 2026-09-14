# -*- coding: utf-8 -*-
# bqp12_step3p.py — шаг 3': штатный решатель приложения (Хубер, свои веса, inflate) на истинах,
# плечи А (syna) и Б (synb): chi2ndf, chi2ndf_pois и сырой χ² по полосам (w_rep и w_sol из `_chi.csv`).
import csv, io, os, sys, glob
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
OUT = r'C:\Users\moroz\bqp12_out'
KEYS = ['G1S16_Cs137_P5', 'G1S16_Am241_P5', 'G1S16_Ba133_P5', 'G1S24_Ba133_P5', 'G1S24_Bi207_P5', 'G1S24_Am241_P5']
BANDS = [(-1e9, 45.0), (45.0, 100.0), (100.0, 300.0), (300.0, 1e9)]
BN = ['<45', '45-100', '100-300', '>300']

def runs(d):
    out = {}
    for f in glob.glob(os.path.join(d, '*_runs.csv')):
        for r in csv.DictReader(io.open(f, encoding='utf-8-sig')):
            out[r['spectrum']] = r
    return out

ra = runs(os.path.join(OUT, 'syna')); rb = runs(os.path.join(OUT, 'synb'))
print('%-16s %9s %9s %9s | %9s %9s %9s | %s' % ('истина', 'sol A', 'sol B', 'Δ', 'pois A', 'pois B', 'Δ', 'сырой χ²(w_rep) A по полосам (B)'))
tot = np.zeros(4); totB = np.zeros(4); dsol = dpois = 0.0
for key in KEYS:
    k = key + '_asimov'
    a = ra[k]; b = rb[k]
    sa, sb = float(a['chi2ndf']), float(b['chi2ndf']); pa, pb = float(a['chi2ndf_pois']), float(b['chi2ndf_pois'])
    dsol += sa - sb; dpois += pa - pb
    bands = {}
    for arm in ('syna', 'synb'):
        chi = list(csv.DictReader(io.open(os.path.join(OUT, arm + '_dump', k + '_chi.csv'), encoding='utf-8-sig')))
        kev = np.array([float(r['keV']) for r in chi]); res = np.array([float(r['resid']) for r in chi]); w = np.array([float(r['w_rep']) for r in chi])
        lo, hi = int(chi[0]['first']), int(chi[0]['last']); win = np.zeros(len(kev), bool); win[lo:hi + 1] = True
        bands[arm] = np.array([float((res[m] ** 2 * w[m]).sum()) for m in [win & (kev >= e0) & (kev < e1) for e0, e1 in BANDS]])
    tot += bands['syna']; totB += bands['synb']
    print('%-16s %9.4f %9.4f %+9.4f | %9.4f %9.4f %+9.4f | %s' % (key, sa, sb, sa - sb, pa, pb, pa - pb,
          ' '.join('%8.1f (%5.1f)' % (x, y) for x, y in zip(bands['syna'], bands['synb']))))
print('%-16s %9s %9s %+9.4f | %9s %9s %+9.4f | %s' % ('Σ', '', '', dsol, '', '', dpois, ' '.join('%8.1f (%5.1f)' % (x, y) for x, y in zip(tot, totB))))
