# -*- coding: utf-8 -*-
# Где по шкале расходятся два плеча: полосы шага step кэВ, где |Δχ²/ndf| велика
import csv, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp9_out'
x, y, key = sys.argv[1], sys.argv[2], sys.argv[3]
lo, hi, step = float(sys.argv[4]), float(sys.argv[5]), float(sys.argv[6])
thr = float(sys.argv[7]) if len(sys.argv) > 7 else 0.05
def load(a):
    with io.open(os.path.join(root, a + '_dump', key + '_chi.csv'), encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))
A, B = load(x), load(y)
first, last = int(A[0]['first']), int(A[0]['last'])
na, nb = float(A[0]['ndf_sol']), float(B[0]['ndf_sol'])
print('%s: %s -> %s, χ² РЕШАТЕЛЯ/ndf по полосам %.0f кэВ в %.0f..%.0f (печатаются |Δ| > %.2f)' % (key, x, y, step, lo, hi, thr))
print('%12s %6s %10s %10s %10s %8s %8s %8s %8s' % ('кэВ','кан.','данные','модель_'+x,'модель_'+y,'χ²_'+x,'χ²_'+y,'Δ','Δpois'))
e = lo; tot = [0.0, 0.0]
while e < hi:
    sel = [i for i in range(first, last+1) if e <= float(A[i]['keV']) < e+step]
    if sel:
        ca = sum(float(A[i]['resid'])**2*float(A[i]['w_sol']) for i in sel)/na
        cb = sum(float(B[i]['resid'])**2*float(B[i]['w_sol']) for i in sel)/nb
        pa = sum(float(A[i]['resid'])**2*float(A[i]['w_rep']) for i in sel)/float(A[0]['ndf_rep'])
        pb = sum(float(B[i]['resid'])**2*float(B[i]['w_rep']) for i in sel)/float(B[0]['ndf_rep'])
        tot[0] += ca; tot[1] += cb
        if abs(cb-ca) > thr:
            dy = sum(float(A[i]['fit']) for i in sel); ma = sum(float(A[i]['model']) for i in sel); mb = sum(float(B[i]['model']) for i in sel)
            print('%5.0f..%-5.0f %6d %10.0f %10.0f %10.0f %8.2f %8.2f %+8.2f %+8.1f' % (e, e+step, len(sel), dy, ma, mb, ca, cb, cb-ca, pb-pa))
    e += step
print('итого в %.0f..%.0f: %.2f -> %.2f (Δ %+.2f)' % (lo, hi, tot[0], tot[1], tot[1]-tot[0]))
