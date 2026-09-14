# -*- coding: utf-8 -*-
# Кто поглощает изменение отклика: континуум (сплайн) или амплитуда? Плечи x->y по полосам, в ОТСЧЁТАХ.
import csv, io, os, sys, glob
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp9_out'
EDGES = [45.0, 100.0, 300.0, 1000.0]; BANDS = ['<45','45-100','100-300','300-1000','>1000']
def band_of(e):
    for i, x in enumerate(EDGES):
        if e < x: return i
    return 4
x, y = sys.argv[1], sys.argv[2]
def curves(a, k):
    with io.open(os.path.join(root, a + '_dump', k + '_curves.csv'), encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))
def chi(a, k):
    with io.open(os.path.join(root, a + '_dump', k + '_chi.csv'), encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))
known = set()
for p in glob.glob(os.path.join(root, x, '*_spline_runs.csv')):
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        for r in csv.DictReader(f):
            if r['part']=='known' and not r['error']: known.add(r['spectrum'])
keys = sorted(k for k in known)
print('плечи %s -> %s. В отсчётах по полосам: Σ данных, Σ|Δобраз| (модель−сплайн), Σ|Δсплайн|, Σ|Δмодель|, и знаковые ΣΔобраз, ΣΔсплайн' % (x, y))
tot = {b: [0.0]*6 for b in range(5)}
rows = []
for k in keys:
    CA, CB = curves(x, k), curves(y, k); HA = chi(x, k)
    first, last = int(HA[0]['first']), int(HA[0]['last'])
    s = {b: [0.0]*6 for b in range(5)}
    for i in range(first, last+1):
        b = band_of(float(CA[i]['keV']))
        ma, mb = float(CA[i]['model']), float(CB[i]['model'])
        ca, cb = float(CA[i]['continuum_raw']), float(CB[i]['continuum_raw'])
        ia, ib = ma-ca, mb-cb
        s[b][0] += float(CA[i]['fit']); s[b][1] += abs(ib-ia); s[b][2] += abs(cb-ca); s[b][3] += abs(mb-ma)
        s[b][4] += ib-ia; s[b][5] += cb-ca
    rows.append((k, s))
    for b in range(5):
        for j in range(6): tot[b][j] += s[b][j]
print('%-9s %14s %12s %12s %12s %12s %12s' % ('полоса','Σданных','Σ|Δобраз|','Σ|Δсплайн|','Σ|Δмодель|','ΣΔобраз','ΣΔсплайн'))
for b in range(5):
    t = tot[b]
    print('%-9s %14.0f %12.0f %12.0f %12.0f %+12.0f %+12.0f' % (BANDS[b], t[0], t[1], t[2], t[3], t[4], t[5]))
print()
print('поспектрово, полоса <45 и 45-100: Σданных | ΣΔобраз ΣΔсплайн Σ|Δмодель|   (кто с кем сокращается)')
for k, s in rows:
    if s[0][1] + s[1][1] > 2000:
        print('%-20s <45: %10.0f | %+9.0f %+9.0f %9.0f   45-100: %10.0f | %+9.0f %+9.0f %9.0f' % (k, s[0][0], s[0][4], s[0][5], s[0][3], s[1][0], s[1][4], s[1][5], s[1][3]))
