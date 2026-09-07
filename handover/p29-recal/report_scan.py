# -*- coding: utf-8 -*-
u"""Сводка развёртки по усилению: χ²/ndf, z и пиковые отсчёты по ключам."""
import csv, io, os, sys, re

d = sys.argv[1]
want = sys.argv[2].split(',') if len(sys.argv) > 2 else ['Ra-226', 'K-40', 'Xray-Pb']
runs, comp = {}, {}
for fn in sorted(os.listdir(d)):
    p = os.path.join(d, fn)
    if fn.endswith('_runs.csv'):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                runs[r['spectrum']] = r
    elif fn.endswith('_components.csv'):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                comp.setdefault(r['spectrum'], {})[r['component']] = r


def gain(k):
    m = re.search(r'__g(\d{5})$', k)
    if m:
        return int(m.group(1)) / 10000.0
    return 1.0


hdr = u'%-10s %9s %8s %8s %8s' % (u'усиление', u'χ²/ndf', u'gain', u'edge', u'ключ')
cols = u''.join(u' %11s' % w for w in want)
print(hdr + cols + u'   (z / доля % / пик.отсч.)')
for k in sorted(runs, key=lambda k: (gain(k), k)):
    r = runs[k]
    line = u'%-10.4f %9s %8s %8s %-24s' % (gain(k), r['chi2ndf'], r['gain'],
                                           r['gain_edge'] + '/' + r['drift_edge'], k)
    for w in want:
        c = comp.get(k, {}).get(w)
        line += u' | %s' % (u'%s %s%% %s' % (c['z'], c['share_pct'], c['peak_counts'])
                            if c else u'—')
    print(line)
