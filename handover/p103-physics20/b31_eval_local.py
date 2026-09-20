# -*- coding: utf-8 -*-
r"""П103 B31 — оценка ЛОКАЛЬНОГО гейта по дампу точек: приведённая ширина каждой точки спектра против медианы
приведённых ширин ЧУЖИХ точек группы в окне энергий [E/w, E·w]; отношение спектра — медиана по его точкам с соседями.
python b31_eval_local.py <res_points.csv> [tol] [w] [min_neighbours]"""
import csv, sys, statistics as st
from collections import defaultdict
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
rows = list(csv.DictReader(open(sys.argv[1], encoding='utf-8')))
tol = float(sys.argv[2]) if len(sys.argv) > 2 else 1.25
w = float(sys.argv[3]) if len(sys.argv) > 3 else 1.6
minn = int(sys.argv[4]) if len(sys.argv) > 4 else 2
byd = defaultdict(lambda: defaultdict(list))
for r in rows:
    byd[r['det']][r['spectrum']].append((float(r['e_kev']), float(r['fwhm_kev']), float(r['weight'])))
def red(e, f): return f / (max(e, 1.0) ** 0.5)
for d in sorted(byd):
    specs = byd[d]
    if len(specs) < 3:
        print('%-10s спектров %d — гейт молчит' % (d, len(specs))); continue
    out = {}
    for s, pts in specs.items():
        others = [p for k, v in specs.items() if k != s for p in v]
        ratios = []
        for e, f, _ in pts:
            nb = [red(e2, f2) for e2, f2, _ in others if e / w <= e2 <= e * w]
            if len(nb) >= minn:
                ratios.append(red(e, f) / st.median(nb))
        if ratios:
            out[s] = st.median(ratios)
    bad = {k: round(v, 2) for k, v in out.items() if not (1 / tol <= v <= tol)}
    allr = sorted(round(v, 2) for v in out.values())
    print('%-10s спектров %2d (с соседями %2d); отношения: мин %.2f, медиана %.2f, макс %.2f; вне ×%.2f: %s' % (d, len(specs), len(out), allr[0], st.median(allr), allr[-1], tol, bad or '—'))
