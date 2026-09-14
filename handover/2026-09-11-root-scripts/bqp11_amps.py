# -*- coding: utf-8 -*-
# Гипотеза (г): амплитуды компонентов по плечам
import csv, io, os, sys, glob, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp11_out'
def comps(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_components.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                if row['part'] != 'known': continue
                r[(row['spectrum'], row['component'])] = row
    return r
C = {a: comps(a) for a in 'abvg'}
keys = sorted(C['a'])
print('%-20s %-10s %12s %12s %8s | %12s %8s | %8s %8s' % ('spectrum','component','decay_s A','decay_s B','B/A-1 %','decay_s V','V/B-1 %','share A','share B'))
dl = []; dv = []
for k in keys:
    a = C['a'][k]; b = C['b'].get(k); v = C['v'].get(k)
    if b is None or v is None: print(k, 'нет в другом плече'); continue
    da, db, dv_ = float(a['decay_s']), float(b['decay_s']), float(v['decay_s'])
    if da <= 0: continue
    rb = 100*(db/da-1); rv = 100*(dv_/db-1) if db>0 else float('nan')
    dl.append(rb); dv.append(rv)
    if abs(rb) > 0.5 or abs(rv) > 3:
        print('%-20s %-10s %12.4g %12.4g %+8.2f | %12.4g %+8.2f | %8s %8s' % (k[0], k[1], da, db, rb, dv_, rv, a['share_pct'], b['share_pct']))
print('пар компонентов %d; B/A−1: медиана %+.3f %%, медиана |Δ| %.3f %%, макс |Δ| %.2f %%' % (len(dl), statistics.median(dl), statistics.median([abs(x) for x in dl]), max(abs(x) for x in dl)))
print('V/B−1: медиана %+.3f %%, медиана |Δ| %.3f %%, макс |Δ| %.2f %%' % (statistics.median(dv), statistics.median([abs(x) for x in dv]), max(abs(x) for x in dv)))
