# -*- coding: utf-8 -*-
# П14: χ²/ndf по полосам кэВ для плеч (копия читателя П9/П11, плечи — аргументами)
# python bqp14_bands.py <sol|rep> lin q3 q4 q5
import csv, io, os, sys, glob, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp14_out'
metric = sys.argv[1] if len(sys.argv) > 1 else 'sol'
arms = sys.argv[2:] or ['lin', 'q3', 'q4', 'q5']
EDGES = [45.0, 100.0, 300.0, 1000.0]
BANDS = ['<45', '45-100', '100-300', '300-1000', '>1000']
def band_of(e):
    for i, x in enumerate(EDGES):
        if e < x: return i
    return 4
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f): r[row['spectrum']] = row
    return r
def chi(d, k):
    with io.open(os.path.join(root, d + '_dump', k + '_chi.csv'), encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    first, last = int(rows[0]['first']), int(rows[0]['last'])
    ndf_s, ndf_r = float(rows[0]['ndf_sol']), float(rows[0]['ndf_rep'])
    sol = [0.0]*5; rep = [0.0]*5; nch = [0]*5
    for x in rows[first:last+1]:
        b = band_of(float(x['keV']))
        r2 = float(x['resid'])**2
        sol[b] += r2*float(x['w_sol'])/ndf_s
        rep[b] += r2*float(x['w_rep'])/ndf_r
        nch[b] += 1
    return sol, rep, nch
R = {a: runs(a) for a in arms}
keys = sorted(k for k in R[arms[0]] if R[arms[0]][k]['part'] == 'known' and not R[arms[0]][k]['error'])
data = {}
bad = 0
for a in arms:
    for k in keys:
        data[(a, k)] = chi(a, k)
        s = sum(data[(a, k)][0])
        if abs(s - float(R[a][k]['chi2ndf'])) > 0.01:
            bad += 1
            print('⚠ реконструкция разошлась: %s %s Σ %.3f против chi2ndf %s' % (a, k, s, R[a][k]['chi2ndf']))
print('реконструкция Σ resid²·w_sol/ndf = chi2ndf: расхождений %d из %d' % (bad, len(arms) * len(keys)))
mi = 0 if metric == 'sol' else 1
print('=== метрика: %s (%s) ===' % (metric, 'χ² РЕШАТЕЛЯ (Хубер), как chi2ndf' if mi == 0 else 'χ² ПУАССОНОВСКИЙ (отчётные веса), как chi2ndf_pois'))
print('Σ по %d спектрам понятной части, по полосам кэВ:' % len(keys))
print('%-6s %9s %9s %9s %9s %9s %9s' % ('плечо', *BANDS, 'всего'))
tot = {}
for a in arms:
    s = [sum(data[(a, k)][mi][b] for k in keys) for b in range(5)]
    tot[a] = s
    print('%-6s %9.2f %9.2f %9.2f %9.2f %9.2f %9.2f' % (a, *s, sum(s)))
print('разности к %s:' % arms[0])
for a in arms[1:]:
    d = [tot[a][b] - tot[arms[0]][b] for b in range(5)]
    print('%-6s %+9.2f %+9.2f %+9.2f %+9.2f %+9.2f %+9.2f' % (a + '-' + arms[0], *d, sum(d)))
nch = [sum(data[(arms[0], k)][2][b] for k in keys) for b in range(5)]
print('каналов полосы фита, всего: %s (%d)' % (nch, sum(nch)))
print()
print('поспектрово (%s): плечо %s по полосам | разности %s-%s по полосам' % (metric, arms[0], arms[1] if len(arms) > 1 else '', arms[0]))
sel = [k for k in keys if any(abs(data[(a, k)][mi][b] - data[(arms[0], k)][mi][b]) > 0.005 for a in arms[1:] for b in range(5))]
for k in sel:
    A = data[(arms[0], k)][mi]
    line = '%-18s|' % k + ''.join('%7.2f' % v for v in A)
    for a in arms[1:]:
        B = data[(a, k)][mi]
        line += ' |' + ''.join('%+7.2f' % (B[b] - A[b]) for b in range(5))
    print(line)
