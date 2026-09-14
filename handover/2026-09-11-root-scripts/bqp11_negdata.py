# -*- coding: utf-8 -*-
# П11: сколько χ² (решателя и сырого) держат каналы полосы фита, где ДАННЫЕ после вычитания фона ОТРИЦАТЕЛЬНЫ
import csv, io, os, sys, glob
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp11_out'
arm = sys.argv[1] if len(sys.argv) > 1 else 'a'
known = set()
for p in glob.glob(os.path.join(root, arm, '*_spline_runs.csv')):
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        for r in csv.DictReader(f):
            if r['part']=='known' and not r['error']: known.add(r['spectrum'])
tot_s = tot_r = 0.0; neg_s = neg_r = 0.0; neg45_s = 0.0; b45_s = 0.0
rows = []
for k in sorted(known):
    with io.open(os.path.join(root, arm + '_dump', k + '_chi.csv'), encoding='utf-8-sig', newline='') as f:
        A = list(csv.DictReader(f))
    first, last = int(A[0]['first']), int(A[0]['last'])
    ndf_s, ndf_r = float(A[0]['ndf_sol']), float(A[0]['ndf_rep'])
    s = r = ns = nr = 0.0; nneg = 0; emax = None; n45 = 0.0
    for i in range(first, last+1):
        x = A[i]; r2 = float(x['resid'])**2
        cs = r2*float(x['w_sol'])/ndf_s; cr = r2*float(x['w_rep'])/ndf_r
        s += cs; r += cr
        if float(x['keV']) < 45: n45 += cs
        if float(x['fit']) < 0:
            ns += cs; nr += cr; nneg += 1; emax = float(x['keV'])
    tot_s += s; tot_r += r; neg_s += ns; neg_r += nr; b45_s += n45
    if ns > 0.05: rows.append((k, first, float(A[first]['keV']), nneg, emax, ns, s, nr, r))
print('плечо %s, 42 спектра: χ² решателя всего %.2f, из них в каналах с отрицательными данными %.2f (%.1f %%); полоса <45 всего %.2f -> из неё отрицательные %.1f %%' % (arm, tot_s, neg_s, 100*neg_s/tot_s, b45_s, 100*neg_s/b45_s))
print('сырой χ²: всего %.1f, в отрицательных %.1f (%.1f %%)' % (tot_r, neg_r, 100*neg_r/tot_r))
print('%-20s %6s %8s %6s %8s %9s %9s %9s %9s' % ('спектр','first','кэВ0','n<0','до кэВ','χ²s_neg','χ²s_all','χ²r_neg','χ²r_all'))
for k, first, e0, nneg, emax, ns, s, nr, r in sorted(rows, key=lambda t: -t[5]):
    print('%-20s %6d %8.1f %6d %8.1f %9.2f %9.2f %9.1f %9.1f' % (k, first, e0, nneg, emax, ns, s, nr, r))
