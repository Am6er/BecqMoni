# -*- coding: utf-8 -*-
# П11: сводка runs.csv по плечам — матрица применена у всех 42? опоры, усиление, ноль
import csv, io, os, sys, glob, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp11_out'
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f): r[row['spectrum']] = row
    return r
arms = sys.argv[1:] or ['a','b','v','g','g_noanchor']
for a in arms:
    R = runs(os.path.join(root, a))
    keys = sorted(k for k in R if R[k]['part']=='known' and not R[k]['error'])
    mx = sum(1 for k in keys if R[k]['matrix_applied'] in ('1','True','true'))
    anch = [int(R[k]['anchors_used'] or 0) for k in keys]
    gains = [float(R[k]['gain']) for k in keys]
    offs = [float(R[k]['anchor_offset_kev'] or 0) for k in keys]
    tot = sum(float(R[k]['chi2ndf']) for k in keys)
    med = statistics.median(float(R[k]['chi2ndf']) for k in keys)
    pois = sum(float(R[k]['chi2ndf_pois']) for k in keys)
    print('%-11s понятных %d, матрица применена у %d, Σχ²/ndf %.1f, медиана %.2f, Σχ²_pois %.1f; опор: 0 у %d, 1 у %d, 2 у %d, >=3 у %d; |усил.-1| медиана %.2f %%, макс %.2f %%; |ноль| медиана %.2f кэВ, макс %.2f' % (
        a, len(keys), mx, tot, med, pois, anch.count(0), anch.count(1), anch.count(2), sum(1 for x in anch if x>=3),
        statistics.median(abs(g-1)*100 for g in gains), max(abs(g-1)*100 for g in gains),
        statistics.median(abs(o) for o in offs), max(abs(o) for o in offs)))
    notes = {}
    for k in keys:
        n = R[k]['matrix_note']
        notes[n] = notes.get(n, 0) + 1
    print('            matrix_note:', notes)
