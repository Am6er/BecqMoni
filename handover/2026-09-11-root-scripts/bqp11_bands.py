# -*- coding: utf-8 -*-
# Разложение χ²/ndf по энергетическим полосам для четырёх плеч (П9 11.09.2026;
# копия П11: корень выходов — вторым аргументом)
# python bqp11_bands.py <sol|rep> [<корень, умолч. C:\Users\moroz\bqp11_out>]
import csv, io, os, sys, glob, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = sys.argv[2] if len(sys.argv) > 2 else r'C:\Users\moroz\bqp11_out'
ARMS = [('A','a'),('B','b'),('V','v'),('G','g')]
EDGES = [45.0, 100.0, 300.0, 1000.0]
BANDS = ['<45','45-100','100-300','300-1000','>1000']
def band_of(e):
    for i, x in enumerate(EDGES):
        if e < x: return i
    return 4
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f): r[row['spectrum']] = row
    return r
def chi(d, k):
    with io.open(os.path.join(d, k + '_chi.csv'), encoding='utf-8-sig', newline='') as f:
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
R = {a: runs(os.path.join(root, d)) for a, d in ARMS}
keys = sorted(k for k in R['A'] if R['A'][k]['part']=='known' and not R['A'][k]['error'])
data = {}
for a, d in ARMS:
    for k in keys:
        data[(a,k)] = chi(os.path.join(root, d + '_dump'), k)
metric = sys.argv[1] if len(sys.argv) > 1 else 'sol'
mi = 0 if metric == 'sol' else 1
print('=== метрика: %s (%s) ===' % (metric, 'χ² РЕШАТЕЛЯ (Хубер), как chi2ndf в runs.csv' if mi==0 else 'χ² ПУАССОНОВСКИЙ (отчётные веса), как chi2ndf_pois'))
print('Σ по 42 спектрам понятной части, по полосам кэВ:')
print('%-6s %9s %9s %9s %9s %9s %9s' % ('плечо', BANDS[0], BANDS[1], BANDS[2], BANDS[3], BANDS[4], 'всего'))
tot = {}
for a, _ in ARMS:
    s = [sum(data[(a,k)][mi][b] for k in keys) for b in range(5)]
    tot[a] = s
    print('%-6s %9.2f %9.2f %9.2f %9.2f %9.2f %9.2f' % (a, s[0], s[1], s[2], s[3], s[4], sum(s)))
print('разности:')
for x, y in (('B','A'),('V','B'),('G','A'),('V','A')):
    d = [tot[x][b]-tot[y][b] for b in range(5)]
    print('%-6s %+9.2f %+9.2f %+9.2f %+9.2f %+9.2f %+9.2f' % (x+'-'+y, d[0], d[1], d[2], d[3], d[4], sum(d)))
# каналы
nch = [sum(data[('A',k)][2][b] for k in keys) for b in range(5)]
print('каналов полосы фита, всего по 42 спектрам: %s  (всего %d)' % (nch, sum(nch)))
# доля ниже 100 кэВ, медиана по спектрам (сравнимо с П9 10.09: там 76.8 % пуассоновского по w=1/max(|model|,1))
for a, _ in ARMS:
    fr = []
    for k in keys:
        v = data[(a,k)][mi]; t = sum(v)
        fr.append(100.0*(v[0]+v[1])/t if t>0 else 0.0)
    print('плечо %s: доля χ² ниже 100 кэВ — медиана %.1f %%, кварт. %.1f .. %.1f' % (a, statistics.median(fr), statistics.quantiles(fr, n=4)[0], statistics.quantiles(fr, n=4)[2]))
# поспектрово: полосы для A и разности
print()
print('поспектрово (%s): значения плеча A по полосам | разности B-A по полосам | V-B по полосам' % metric)
hdr = '%-20s|' % 'spectrum' + ''.join('%8s' % b for b in BANDS) + ' |' + ''.join('%8s' % b for b in BANDS) + ' |' + ''.join('%8s' % b for b in BANDS)
print(hdr)
for k in keys:
    A = data[('A',k)][mi]; B = data[('B',k)][mi]; V = data[('V',k)][mi]
    print('%-20s|' % k + ''.join('%8.2f' % v for v in A) + ' |' + ''.join('%+8.2f' % (B[b]-A[b]) for b in range(5)) + ' |' + ''.join('%+8.2f' % (V[b]-B[b]) for b in range(5)))
