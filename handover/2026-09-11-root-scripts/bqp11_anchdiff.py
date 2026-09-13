# -*- coding: utf-8 -*-
# П11: насколько ПРИВЯЗКА (усиление, ноль, число опор) различается между плечами — плечи мерят ещё и её перерешение
import csv, io, os, sys, glob, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp11_out'
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f): r[row['spectrum']] = row
    return r
x, y = sys.argv[1], sys.argv[2]
X, Y = runs(x), runs(y)
keys = sorted(k for k in X if X[k]['part']=='known' and not X[k]['error'])
dg = [(float(Y[k]['gain'])-float(X[k]['gain']))*100 for k in keys]
do = [float(Y[k]['anchor_offset_kev'] or 0)-float(X[k]['anchor_offset_kev'] or 0) for k in keys]
dn = [int(Y[k]['anchors_used'] or 0)-int(X[k]['anchors_used'] or 0) for k in keys]
print('%s -> %s: Δусиление, %%: медиана |Δ| %.3f, макс |Δ| %.3f; Δноль, кэВ: медиана |Δ| %.3f, макс |Δ| %.3f; число опор изменилось у %d спектров' % (
    x, y, statistics.median(abs(v) for v in dg), max(abs(v) for v in dg), statistics.median(abs(v) for v in do), max(abs(v) for v in do), sum(1 for v in dn if v)))
for k, g, o, n in sorted(zip(keys, dg, do, dn), key=lambda t: -abs(t[1]))[:5]:
    print('   %-20s Δусил. %+.3f %%  Δноль %+.2f кэВ  Δопор %+d   (%s: %s оп., усил. %s, ноль %s)' % (k, g, o, n, x, X[k]['anchors_used'], X[k]['gain'], X[k]['anchor_offset_kev']))
