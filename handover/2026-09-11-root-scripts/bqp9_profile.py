# -*- coding: utf-8 -*-
# Профиль невязки ниже границы по плечу: шаг в кэВ, данные/модель, доля χ² (отчётные веса)
import csv, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp9_out'
arm, key = sys.argv[1], sys.argv[2]
edge = float(sys.argv[3]) if len(sys.argv) > 3 else 100.0
step = float(sys.argv[4]) if len(sys.argv) > 4 else 5.0
arm2 = sys.argv[5] if len(sys.argv) > 5 else None
def load(a):
    with io.open(os.path.join(root, a + '_dump', key + '_chi.csv'), encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))
A = load(arm); B = load(arm2) if arm2 else None
first, last = int(A[0]['first']), int(A[0]['last'])
ndf = float(A[0]['ndf_rep'])
tot = sum(float(A[i]['resid'])**2*float(A[i]['w_rep']) for i in range(first, last+1))
print('%s / %s: полоса фита %d..%d (%.1f..%.1f кэВ), χ²_pois/ndf = %.2f, ndf %.1f' % (key, arm, first, last, float(A[first]['keV']), float(A[last]['keV']), tot/ndf, ndf))
hdr = '%8s %6s %10s %10s %7s %8s %8s' % ('кэВ', 'кан.', 'данные', 'модель', 'м/д', 'χ²/ndf', 'доля%')
if B: hdr += ' | %10s %8s %8s' % ('модель_'+arm2, 'χ²/ndf', 'Δχ²/ndf')
print(hdr)
e = float(A[first]['keV']); e = step*int(e//step)
while e < edge:
    sel = [i for i in range(first, last+1) if e <= float(A[i]['keV']) < e+step]
    if sel:
        dy = sum(float(A[i]['fit']) for i in sel); dm = sum(float(A[i]['model']) for i in sel)
        c = sum(float(A[i]['resid'])**2*float(A[i]['w_rep']) for i in sel)/ndf
        line = '%4.0f..%-3.0f %6d %10.0f %10.0f %7.3f %8.2f %8.1f' % (e, e+step, len(sel), dy, dm, dm/dy if dy else 0, c, 100*c*ndf/tot)
        if B:
            dm2 = sum(float(B[i]['model']) for i in sel)
            c2 = sum(float(B[i]['resid'])**2*float(B[i]['w_rep']) for i in sel)/float(B[0]['ndf_rep'])
            line += ' | %10.0f %8.2f %+8.2f' % (dm2, c2, c2-c)
        print(line)
    e += step
# вершины: данные и модель, локальные максимумы после сглаживания 3 каналами
def peaks(vals, kev, lo, hi):
    out = []
    for i in range(max(lo,3), hi-3):
        if vals[i] >= max(vals[i-3:i+4]) and vals[i] > 0:
            out.append((kev[i], vals[i]))
    return out
kev = [float(r['keV']) for r in A]
dat = [float(r['fit']) for r in A]; mod = [float(r['model']) for r in A]
sm = lambda v: [ (v[i-1]+v[i]+v[i+1])/3 if 0<i<len(v)-1 else v[i] for i in range(len(v)) ]
lo = first; hi = min(last, max(i for i in range(first, last+1) if kev[i] < edge))
pd = peaks(sm(dat), kev, lo, hi); pm = peaks(sm(mod), kev, lo, hi)
print('вершины данных ниже %.0f кэВ: %s' % (edge, ', '.join('%.1f (%.0f)' % p for p in pd if p[1] > 0.02*max(x[1] for x in pd))))
print('вершины модели ниже %.0f кэВ: %s' % (edge, ', '.join('%.1f (%.0f)' % p for p in pm if p[1] > 0.02*max(x[1] for x in pm))))
