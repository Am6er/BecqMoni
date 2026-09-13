# -*- coding: utf-8 -*-
# Насколько новая матрица МЕНЯЕТ модель по сравнению с невязкой, по полосам (плечи A и B)
import csv, io, os, sys, glob, math, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp9_out'
EDGES = [45.0, 100.0, 300.0, 1000.0]; BANDS = ['<45','45-100','100-300','300-1000','>1000']
def band_of(e):
    for i, x in enumerate(EDGES):
        if e < x: return i
    return 4
def load(d, k):
    with io.open(os.path.join(root, d + '_dump', k + '_chi.csv'), encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))
x, y = sys.argv[1], sys.argv[2]   # плечи, напр. a b
keys = sorted(n[:-8] for n in os.listdir(os.path.join(root, x + '_dump')) if n.endswith('_chi.csv'))
import glob
known = set()
for p in glob.glob(os.path.join(root, x, '*_spline_runs.csv')):
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        for r in csv.DictReader(f):
            if r['part']=='known' and not r['error']: known.add(r['spectrum'])
keys = [k for k in keys if k in known]
print('плечи %s -> %s: по полосам, Σ по спектрам: χ²_x (отчётные веса), Σw(Δmodel)² — размер сдвига модели в тех же единицах, и χ²_y' % (x, y))
tot = [[0.0,0.0,0.0,0.0] for _ in range(5)]
per = []
for k in keys:
    A = load(x, k); B = load(y, k)
    first, last = int(A[0]['first']), int(A[0]['last'])
    ndf = float(A[0]['ndf_rep'])
    s = [[0.0,0.0,0.0,0.0] for _ in range(5)]
    for i in range(first, last+1):
        a, b = A[i], B[i]
        bb = band_of(float(a['keV']))
        w = float(a['w_rep'])
        ra, rb = float(a['resid']), float(b['resid'])
        dm = float(b['model']) - float(a['model'])
        s[bb][0] += ra*ra*w/ndf; s[bb][1] += dm*dm*w/ndf; s[bb][2] += rb*rb*w/ndf
        s[bb][3] += -2*ra*dm*w/ndf  # если бы амплитуды не менялись: r_b = r_a - dm; r_b² = r_a² - 2 r_a dm + dm²
    per.append((k, s))
    for bb in range(5):
        for j in range(4): tot[bb][j] += s[bb][j]
print('%-9s %12s %12s %12s %12s' % ('полоса', 'χ²_'+x, 'Σw·Δm²', 'χ²_'+y, '−2Σw·r·Δm'))
for bb in range(5):
    print('%-9s %12.1f %12.1f %12.1f %12.1f' % (BANDS[bb], tot[bb][0], tot[bb][1], tot[bb][2], tot[bb][3]))
print()
print('поспектрово, полосы <45 и 45-100: χ²_x | Σw·Δm² | χ²_y')
for k, s in per:
    if s[0][0]+s[1][0] > 20:
        print('%-20s <45: %8.1f %8.1f %8.1f   45-100: %8.1f %8.1f %8.1f' % (k, s[0][0], s[0][1], s[0][2], s[1][0], s[1][1], s[1][2]))
