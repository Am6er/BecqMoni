# -*- coding: utf-8 -*-
r"""П103 B31 — оценка правила гейта по дампу точек (--res-points=): отношение медианы FWHM спектра к МОДЕЛИ группы,
построенной без него (leave-one-out), по его же энергиям. python b31_eval.py <res_points.csv> [tol]"""
import csv, sys, os, statistics as st
from collections import defaultdict
sys.path.insert(0, r'D:\BqMoni_Claude\p103\wt\tools\CORPUS\scripts')
import numpy as np
import corpus_calib
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
rows = list(csv.DictReader(open(sys.argv[1], encoding='utf-8')))
tol = float(sys.argv[2]) if len(sys.argv) > 2 else 1.25
byd = defaultdict(lambda: defaultdict(list))
for r in rows:
    byd[r['det']][r['spectrum']].append((float(r['e_kev']), float(r['fwhm_kev']), float(r['weight'])))
for d in sorted(byd):
    specs = byd[d]
    if len(specs) < 3:
        print('%-10s спектров %d — гейт молчит' % (d, len(specs))); continue
    out = {}
    for s, pts in specs.items():
        others = [p for k, v in specs.items() if k != s for p in v]
        if len(others) < 3 or len(set(k for k in specs if k != s)) < 2:
            continue
        coef = corpus_calib.fit_resolution_kev(others)
        rf = corpus_calib.resolution_fn(coef)
        ratio = st.median([f / rf(e) for e, f, w in pts])
        out[s] = ratio
    bad = {k: round(v, 2) for k, v in out.items() if not (1 / tol <= v <= tol)}
    allr = sorted(round(v, 2) for v in out.values())
    print('%-10s спектров %2d; отношения LOO: мин %.2f, медиана %.2f, макс %.2f; вне ×%.2f: %s' % (d, len(specs), allr[0], st.median(allr), allr[-1], tol, bad or '—'))
