# -*- coding: utf-8 -*-
# П16: поспектровая таблица плеч — chi2ndf, опоры, β; контроли побитово; хуже/лучше lin
import csv, glob, io, os, sys, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp16_out'
arms = sys.argv[1:] or ['lin', 'model-b0', 'model-b1', 'lit-b1', 'model-free', 'lit-free']
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for row in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            r[row['spectrum']] = row
    return r
R = {a: runs(os.path.join(root, a)) for a in arms}
keys = sorted(k for k in R[arms[0]] if R[arms[0]][k]['part'] == 'known' and not R[arms[0]][k]['error'])
print('спектров понятной части: %d' % len(keys))
if 'model-b0' in R:
    same = sum(1 for k in keys if R['lin'][k]['chi2ndf'] == R['model-b0'][k]['chi2ndf'])
    print('model-b0 == lin по chi2ndf (строка в строку): %d из %d' % (same, len(keys)))
ref = runs(r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\pie\out_rev16_mini')
same = sum(1 for k in keys if k in ref and ref[k]['chi2ndf'] == R['lin'][k]['chi2ndf'])
print('lin == out_rev16_mini дерева по chi2ndf: %d из %d (в базе есть %d)' % (same, len(keys), sum(1 for k in keys if k in ref)))
print()
free = [a for a in arms if a.endswith('free')]
hdr = '%-20s' % 'spectrum' + ''.join('%10s' % a for a in arms) + ' |' + ''.join('%5s' % a[:4] for a in arms) + ' |' + ''.join('%14s' % ('β ' + a) for a in free)
print(hdr)
worse = {a: [] for a in arms}; better = {a: [] for a in arms}
for k in keys:
    chi = [float(R[a][k]['chi2ndf']) for a in arms]
    used = [R[a][k]['anchors_used'] for a in arms]
    for i, a in enumerate(arms):
        if i > 0 and chi[i] > chi[0] + 0.005:
            worse[a].append((k, chi[0], chi[i]))
        if i > 0 and chi[i] < chi[0] - 0.005:
            better[a].append((k, chi[0], chi[i]))
    b = ''
    for a in free:
        r = R[a][k]
        b += '%9s %4s' % (r.get('anchor_beta', ''), 'подб' if r.get('anchor_beta_free') == '1' else 'закр')
    print('%-20s' % k + ''.join('%10.2f' % c for c in chi) + ' |' + ''.join('%5s' % u for u in used) + ' |' + b)
print()
for a in arms:
    chis = [float(R[a][k]['chi2ndf']) for k in keys]
    print('%-11s Σχ²/ndf %.1f  медиана %.2f  хуже lin у %d  лучше у %d' % (a, sum(chis), statistics.median(chis), len(worse[a]), len(better[a])))
print()
for a in arms[1:]:
    w = sorted(worse[a], key=lambda t: t[1] - t[2])
    print('%s: хуже lin у %d: %s' % (a, len(w), '; '.join('%s %.2f→%.2f' % t for t in w[:10])))
    b = sorted(better[a], key=lambda t: t[2] - t[1])
    print('%s: лучше lin у %d; крупнейшие: %s' % (a, len(b), '; '.join('%s %.2f→%.2f' % t for t in b[:8])))
print()
for a in free:
    fitted = [(k, float(R[a][k]['anchor_beta'])) for k in keys if R[a][k].get('anchor_beta_free') == '1']
    vals = sorted(v for _, v in fitted)
    print('%s: β подобран у %d из %d; медиана %.2f, квартили %.2f .. %.2f, min %.2f, max %.2f' % (
        a, len(fitted), len(keys), statistics.median(vals) if vals else float('nan'),
        vals[len(vals) // 4] if vals else float('nan'), vals[3 * len(vals) // 4] if vals else float('nan'),
        min(vals) if vals else float('nan'), max(vals) if vals else float('nan')))
    for k, v in sorted(fitted, key=lambda t: t[1]):
        print('   %-20s β %+7.2f  опор %s  %s' % (k, v, R[a][k]['anchors_used'], R[a][k]['anchor_note'][:150]))
