#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Сравнение двух прогонов pie по корпусу: χ²/ndf, время, состав, recall.

    python tools/pie/compare.py out_base out_p1_shape [--mode spline]
    python tools/pie/compare.py out_base out_p1_shape --groups ASN16,AS80x80

Печатает по группам сумму и медиану χ²/ndf, время, и — отдельно — спектры,
у которых состав «пирога» разошёлся сильнее порога. Recall и фантомы берутся
тем же критерием, что и score.py (доля ≥ 3 %, z ≥ 4).
"""
import argparse
import csv
import glob
import os
import statistics
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import score  # noqa: E402


def load_runs(d, mode):
    out = {}
    for f in glob.glob(os.path.join(d, '*_%s_runs.csv' % mode)):
        group = os.path.basename(f)[:-len('_%s_runs.csv' % mode)]
        for r in csv.DictReader(open(f, encoding='utf-8-sig')):
            out[r['spectrum']] = (group, r)
    return out


def load_comps(d, mode):
    out = {}
    for f in glob.glob(os.path.join(d, '*_%s_components.csv' % mode)):
        for r in csv.DictReader(open(f, encoding='utf-8-sig')):
            if r['kind'] == 'nuisance':
                continue
            out.setdefault(r['spectrum'], {})[r['component']] = float(r['share_pct'])
    return out


def recall(d, mode, sthr=3.0, zthr=4.0, only=None):
    truth = score.load_truth()
    results, groups, _ = score.load_results(mode, d)
    if only:
        groups &= set(only)
    hits = tot = phantom = 0
    per = {}
    for spectrum, t in truth.items():
        if t['det'] not in groups:
            continue
        fams = {score.family(c) for c in t['components']}
        detected = set()
        for row in results.get(spectrum, []):
            if row['kind'] == 'nuisance':
                continue
            if float(row['share_pct']) >= sthr and float(row['z']) >= zthr:
                detected.add(row['component'])
        detfams = {score.family(c) for c in detected}
        hard = [c for c in detected if score.family(c) not in fams
                and not (not t['has_bg'] and c in score.ROOM)]
        acc = per.setdefault(t['det'], [0, 0, 0])
        acc[0] += len(fams & detfams)
        acc[1] += len(fams)
        acc[2] += len(hard)
        hits += len(fams & detfams)
        tot += len(fams)
        phantom += len(hard)
    return hits, tot, phantom, per


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('a')
    ap.add_argument('b')
    ap.add_argument('--mode', default='spline', choices=['snip', 'spline'])
    ap.add_argument('--groups')
    ap.add_argument('--share-eps', type=float, default=5.0,
                    help='печатать спектры, где доля компонента сдвинулась больше, %%')
    args = ap.parse_args()

    only = set(args.groups.split(',')) if args.groups else None
    ra, rb = load_runs(args.a, args.mode), load_runs(args.b, args.mode)
    ca, cb = load_comps(args.a, args.mode), load_comps(args.b, args.mode)

    keys = sorted(set(ra) & set(rb))
    if only:
        keys = [k for k in keys if ra[k][0] in only]

    per = {}
    for k in keys:
        g = ra[k][0]
        va, vb = ra[k][1], rb[k][1]
        if va['chi2ndf'] in ('', 'ERROR') or vb['chi2ndf'] in ('', 'ERROR'):
            continue
        per.setdefault(g, []).append((float(va['chi2ndf']), float(vb['chi2ndf']),
                                      float(va['ms']), float(vb['ms'])))

    print('режим %s: %s  ->  %s' % (args.mode, os.path.basename(args.a), os.path.basename(args.b)))
    print()
    print('%-11s %4s %10s %10s %8s %9s %9s' %
          ('группа', 'n', 'chi2 A', 'chi2 B', 'дельта%', 'мс A', 'мс B'))
    ta = tb = tma = tmb = 0.0
    n = 0
    for g in sorted(per):
        v = per[g]
        sa, sb = sum(x[0] for x in v), sum(x[1] for x in v)
        ma, mb = sum(x[2] for x in v), sum(x[3] for x in v)
        ta += sa; tb += sb; tma += ma; tmb += mb; n += len(v)
        print('%-11s %4d %10.2f %10.2f %+7.1f%% %9.0f %9.0f' %
              (g, len(v), sa, sb, 100 * (sb - sa) / sa if sa else 0, ma, mb))
    print('%-11s %4d %10.2f %10.2f %+7.1f%% %9.0f %9.0f' %
          ('ИТОГО', n, ta, tb, 100 * (tb - ta) / ta if ta else 0, tma, tmb))

    # recall считается по одному и тому же множеству групп: в каталоге ветки
    # может лежать только часть корпуса, и сравнивать её с полным нельзя.
    common = set(per) if not only else (set(per) & only)
    ha, tot_a, pa, _ = recall(args.a, args.mode, only=common)
    hb, tot_b, pb, _ = recall(args.b, args.mode, only=common)
    print()
    print('recall  A %3.0f%% (%d/%d), фантомов %d' % (100.0 * ha / tot_a, ha, tot_a, pa))
    print('recall  B %3.0f%% (%d/%d), фантомов %d' % (100.0 * hb / tot_b, hb, tot_b, pb))

    moved = []
    for k in keys:
        a, b = ca.get(k, {}), cb.get(k, {})
        for comp in set(a) | set(b):
            d = b.get(comp, 0.0) - a.get(comp, 0.0)
            if abs(d) >= args.share_eps:
                moved.append((abs(d), k, comp, a.get(comp, 0.0), b.get(comp, 0.0)))
    if moved:
        print()
        print('сдвиг доли >= %.0f %%:' % args.share_eps)
        for _, k, comp, x, y in sorted(moved, reverse=True)[:40]:
            print('  %-24s %-10s %5.1f%% -> %5.1f%%' % (k, comp, x, y))
        print('  всего таких пар: %d' % len(moved))


if __name__ == '__main__':
    main()
