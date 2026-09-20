# -*- coding: utf-8 -*-
"""П90: A/B малой базы по спектрам — χ²/ndf, состав (доли компонентов), кто сдвинулся."""
import csv, glob, os, sys
REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
for _s in (sys.stdout,): _s.reconfigure(encoding='utf-8', errors='replace')
root = os.path.join(REPO, 'tools', 'pie')
A = os.path.join(root, sys.argv[1] if len(sys.argv) > 1 else 'out_p90_off')
B = os.path.join(root, sys.argv[2] if len(sys.argv) > 2 else 'out_p90_on')

def runs(d):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        with open(p, encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                out[r['spectrum']] = r
    return out

def comps(d):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_components.csv')):
        with open(p, encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                out.setdefault(r['spectrum'], {})[r['component']] = r
    return out

ra, rb = runs(A), runs(B)
ca, cb = comps(A), comps(B)
moved = []; same = 0
print('%-22s %-8s %9s %9s %8s  %s' % ('спектр', 'часть', 'χ²/ndf до', 'после', 'Δ%', 'состав (доля %, до → после; только сдвинувшиеся > 0.05 %)'))
for k in sorted(ra):
    if k not in rb: continue
    x, y = ra[k], rb[k]
    try: ca_, cb_ = float(x['chi2ndf']), float(y['chi2ndf'])
    except ValueError: continue
    ch = []
    for comp in sorted(set(ca.get(k, {})) | set(cb.get(k, {}))):
        sa = float(ca.get(k, {}).get(comp, {}).get('share_pct', 0) or 0)
        sb = float(cb.get(k, {}).get(comp, {}).get('share_pct', 0) or 0)
        if abs(sa - sb) > 0.05:
            ch.append('%s %.2f→%.2f' % (comp, sa, sb))
    if abs(ca_ - cb_) < 5e-5 and not ch and x.get('gain') == y.get('gain'):
        same += 1
        continue
    moved.append(k)
    d = (cb_ / ca_ - 1) * 100 if ca_ else float('nan')
    print('%-22s %-8s %9.3f %9.3f %+7.2f%%  %s' % (k, x['part'], ca_, cb_, d, '; '.join(ch)))
print('\nсдвинулось %d, без изменений (χ², состав, gain) %d' % (len(moved), same))
