# -*- coding: utf-8 -*-
# П14: поспектровая таблица плеч lin/q3/q4/q5 — chi2ndf, опоры, квадрат, стрелка; где квадрат включился
import csv, io, os, sys, glob, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp14_out'
arms = sys.argv[1:] or ['lin', 'q3', 'q4', 'q5']
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                r[row['spectrum']] = row
    return r
R = {a: runs(a) for a in arms}
keys = sorted(k for k in R[arms[0]] if R[arms[0]][k]['part'] == 'known' and not R[arms[0]][k]['error'])
print('спектров понятной части: %d' % len(keys))
hdr = '%-20s' % 'spectrum' + ''.join('%9s' % a for a in arms) + ' | ' + ''.join('%6s' % ('оп.' + a) for a in arms) + ' | ' + ''.join('%9s' % ('стр.' + a) for a in arms)
print(hdr)
quad_on = {a: [] for a in arms}
worse = {a: [] for a in arms}
for k in keys:
    chi = [float(R[a][k]['chi2ndf']) for a in arms]
    used = [R[a][k]['anchors_used'] for a in arms]
    sag = [float(R[a][k].get('anchor_sag_kev', 0) or 0) for a in arms]
    q = [float(R[a][k].get('anchor_quad', 0) or 0) for a in arms]
    for i, a in enumerate(arms):
        if q[i] != 0.0:
            quad_on[a].append(k)
        if i > 0 and chi[i] > chi[0] + 0.005:
            worse[a].append((k, chi[0], chi[i]))
    print('%-20s' % k + ''.join('%9.2f' % c for c in chi) + ' | ' + ''.join('%6s' % u for u in used) + ' | ' + ''.join('%9.2f' % s for s in sag))
print()
for a in arms:
    chis = [float(R[a][k]['chi2ndf']) for k in keys]
    print('%-4s Σχ²/ndf %.1f  медиана %.2f  квадрат включился у %d: %s' % (a, sum(chis), statistics.median(chis), len(quad_on[a]), ', '.join(quad_on[a])))
print()
for a in arms[1:]:
    w = sorted(worse[a], key=lambda t: t[1] - t[2])
    print('%s: хуже lin у %d: %s' % (a, len(w), '; '.join('%s %.2f→%.2f' % t for t in w)))
    b = [(k, float(R[arms[0]][k]['chi2ndf']), float(R[a][k]['chi2ndf'])) for k in keys if float(R[a][k]['chi2ndf']) < float(R[arms[0]][k]['chi2ndf']) - 0.005]
    b.sort(key=lambda t: t[2] - t[1])
    print('%s: лучше lin у %d; крупнейшие: %s' % (a, len(b), '; '.join('%s %.2f→%.2f' % t for t in b[:6])))
