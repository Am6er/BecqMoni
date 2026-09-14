# -*- coding: utf-8 -*-
# Контроль: дамп _chi.csv воспроизводит chi2ndf и chi2ndf_pois из runs.csv
import csv, io, os, sys, glob
sys.stdout.reconfigure(encoding='utf-8')
out, dump, ref = sys.argv[1], sys.argv[2], sys.argv[3]
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f): r[row['spectrum']] = row
    return r
R, Rref = runs(out), runs(ref)
bad = 0; n = 0
for k in sorted(R):
    if R[k]['part'] != 'known' or R[k]['error']: continue
    n += 1
    a, b = float(R[k]['chi2ndf']), float(Rref[k]['chi2ndf'])
    if abs(a-b) > 5e-4: print('РАСХОЖДЕНИЕ С ЭТАЛОНОМ', k, a, b); bad += 1
    p = os.path.join(dump, k + '_chi.csv')
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    first, last = int(rows[0]['first']), int(rows[0]['last'])
    ndf_s, ndf_r = float(rows[0]['ndf_sol']), float(rows[0]['ndf_rep'])
    cs = sum(float(x['resid'])**2*float(x['w_sol']) for x in rows[first:last+1])
    cr = sum(float(x['resid'])**2*float(x['w_rep']) for x in rows[first:last+1])
    ap, bp = float(R[k]['chi2ndf_pois']), cr/ndf_r
    if abs(cs/ndf_s - a) > 5e-4 or abs(bp - ap) > 5e-4:
        print('РЕКОНСТРУКЦИЯ НЕ СОШЛАСЬ', k, a, cs/ndf_s, ap, bp); bad += 1
print('спектров понятной части %d, расхождений %d' % (n, bad))
