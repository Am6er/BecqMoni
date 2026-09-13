# -*- coding: utf-8 -*-
import csv, io, os, sys, glob, math
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp11_out'
EDGES = [45.0, 100.0, 300.0, 1000.0]; BANDS = ['<45','45-100','100-300','300-1000','>1000']
def band_of(e):
    for i, x in enumerate(EDGES):
        if e < x: return i
    return 4
x = sys.argv[1]; y = sys.argv[2]
known = set()
for p in glob.glob(os.path.join(root, x, '*_spline_runs.csv')):
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        for r in csv.DictReader(f):
            if r['part']=='known' and not r['error']: known.add(r['spectrum'])
tot = {b: [0.0]*5 for b in range(5)}
for k in sorted(known):
    with io.open(os.path.join(root, x + '_dump', k + '_chi.csv'), encoding='utf-8-sig', newline='') as f: A = list(csv.DictReader(f))
    with io.open(os.path.join(root, y + '_dump', k + '_chi.csv'), encoding='utf-8-sig', newline='') as f: B = list(csv.DictReader(f))
    first, last = int(A[0]['first']), int(A[0]['last'])
    for i in range(first, last+1):
        b = band_of(float(A[i]['keV']))
        r = float(A[i]['resid']); w = float(A[i]['w_rep'])
        dm = float(B[i]['model']) - float(A[i]['model'])
        tot[b][0] += float(A[i]['fit']); tot[b][1] += abs(r); tot[b][2] += r*r*w; tot[b][3] += 1; tot[b][4] += abs(dm)
print('плечо %s, 42 спектра: по полосам Σданных, Σ|невязка| (и %% от данных), χ²_pois на канал, Σ|Δмодель %s−%s| (и %% от данных)' % (x, y, x))
print('%-9s %12s %12s %7s %10s %12s %7s' % ('полоса','Σданных','Σ|r|','%','χ²/канал','Σ|Δm|','%'))
for b in range(5):
    t = tot[b]
    print('%-9s %12.0f %12.0f %7.2f %10.1f %12.0f %7.2f' % (BANDS[b], t[0], t[1], 100*t[1]/t[0], t[2]/t[3], t[4], 100*t[4]/t[0]))
