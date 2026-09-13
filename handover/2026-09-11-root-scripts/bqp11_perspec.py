# -*- coding: utf-8 -*-
# П11: поспектрово chi2ndf по плечам, счёт лучше/хуже, крупнейшие |Δ|
import csv, io, os, sys, glob
sys.stdout.reconfigure(encoding='utf-8')
root = sys.argv[3] if len(sys.argv) > 3 else r'C:\Users\moroz\bqp11_out'
x, y = sys.argv[1], sys.argv[2]
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f): r[row['spectrum']] = row
    return r
X, Y = runs(x), runs(y)
keys = sorted(k for k in X if X[k]['part']=='known' and not X[k]['error'])
d = {k: float(Y[k]['chi2ndf']) - float(X[k]['chi2ndf']) for k in keys}
better = sum(1 for k in keys if d[k] < -0.005); worse = sum(1 for k in keys if d[k] > 0.005)
print('%s-%s: Σ %+.2f; лучше %d / хуже %d / равно %d (порог 0.005); крупнейшие:' % (y, x, sum(d.values()), better, worse, len(keys)-better-worse))
for k in sorted(keys, key=lambda k: -abs(d[k]))[:8]:
    print('   %-20s %+.2f  (%s %.2f -> %s %.2f)' % (k, d[k], x, float(X[k]['chi2ndf']), y, float(Y[k]['chi2ndf'])))
