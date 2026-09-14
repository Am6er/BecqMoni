# -*- coding: utf-8 -*-
# П18: поспектровое сличение chi2ndf двух каталогов прогона (runs.csv всех групп).
#   python C:\Users\moroz\bqp18_cmp.py <dirA> <dirB> [--part=known] [--cols=chi2ndf,gain,...]
import sys, csv, glob, os
sys.stdout.reconfigure(encoding='utf-8')

def load(d, part=None):
    rows = {}
    for f in glob.glob(os.path.join(d, '*_runs.csv')):
        with open(f, encoding='utf-8-sig', newline='') as fh:
            for r in csv.DictReader(fh):
                if part and r.get('part') != part:
                    continue
                if r.get('error'):
                    continue
                rows[r['spectrum']] = r
    return rows

a, b = sys.argv[1], sys.argv[2]
part = None
cols = ['chi2ndf']
for x in sys.argv[3:]:
    if x.startswith('--part='):
        part = x[7:]
    if x.startswith('--cols='):
        cols = x[7:].split(',')
A = load(a, part); B = load(b, part)
common = sorted(set(A) & set(B))
print('A', len(A), 'B', len(B), 'общих', len(common), 'только A', sorted(set(A)-set(B))[:5], 'только B', sorted(set(B)-set(A))[:5])
for c in cols:
    same = 0; diff = []
    for k in common:
        va, vb = A[k].get(c, ''), B[k].get(c, '')
        if va == vb:
            same += 1
        else:
            diff.append((k, va, vb))
    print('%s: строка в строку %d из %d' % (c, same, len(common)))
    for k, va, vb in diff[:12]:
        print('   %-24s %s -> %s' % (k, va, vb))
