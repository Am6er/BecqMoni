# П186: сравнить два прогона по runs.csv (chi2ndf known/unknown по спектрам), побитово и суммой
# python cmp.py <out_A> <out_B> [--all]
import csv, sys, glob, os
A, B = sys.argv[1], sys.argv[2]
show = '--all' in sys.argv
def ld(d):
    r = {}
    for f in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for x in csv.DictReader(open(f, encoding='utf-8-sig')):
            r[x['spectrum']] = x
    return r
a, b = ld(A), ld(B)
same = diff = 0
tot = {'known': [0.0, 0.0], 'unknown': [0.0, 0.0]}
rows = []
for k in sorted(a):
    if k not in b: continue
    x, y = a[k], b[k]
    if x['chi2ndf'] == 'ERROR' or y['chi2ndf'] == 'ERROR':
        same += x['chi2ndf'] == y['chi2ndf']; continue
    fa, fb = float(x['chi2ndf']), float(y['chi2ndf'])
    tot[x['part']][0] += fa; tot[x['part']][1] += fb
    if x['chi2ndf'] == y['chi2ndf'] and x['gain'] == y['gain'] and x['offset_ch'] == y['offset_ch']:
        same += 1
    else:
        diff += 1
        rows.append((k, x['part'], fa, fb))
print('спектров %d, побитово %d, расходятся %d' % (same + diff, same, diff))
for p in tot:
    print('  %-8s Σχ²/ndf %.4f -> %.4f (%+.3f %%)' % (p, tot[p][0], tot[p][1], 100 * (tot[p][1] / tot[p][0] - 1) if tot[p][0] else 0))
rows.sort(key=lambda r: (r[3] - r[2]))
for k, p, fa, fb in (rows if show else rows[:6] + rows[-6:]):
    print('  %-26s %-7s %9.4f -> %9.4f (%+.2f %%)' % (k, p, fa, fb, 100 * (fb / fa - 1)))
