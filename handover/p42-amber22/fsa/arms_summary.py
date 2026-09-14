# -*- coding: utf-8 -*-
"""П42 13.09.2026, `AMBER22` п. 4 — сводка плеч на корпусном диске AS80_Th232Medal (читатель П26 fsa/arms_summary.py
с путями П42: каталоги `tools/pie/out_p42_<плечо>`, дампы `curves_<плечо>/AS80_Th232Medal_curves.csv`).

    python handover/p42-amber22/fsa/arms_summary.py [плечо ...]
"""
import csv
import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, 'handover', 'p26-amber22', 'fsa'))
import arms_summary as A  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def run_row(arm):
    for p in glob.glob(os.path.join(ROOT, 'tools', 'pie', 'out_p42_' + arm, '*_spline_runs.csv')):
        for r in csv.DictReader(open(p, encoding='utf-8-sig')):
            if r['spectrum'] == 'AS80_Th232Medal':
                return r
    return None


def curves(arm):
    p = os.path.join(HERE, 'curves_' + arm, 'AS80_Th232Medal_curves.csv')
    if not os.path.exists(p):
        return None
    rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
    return ([float(r['keV']) for r in rows], [float(r['net']) for r in rows],
            [float(r['model']) for r in rows], [float(r['continuum_raw']) for r in rows])


def comps(arm):
    out = []
    for p in glob.glob(os.path.join(ROOT, 'tools', 'pie', 'out_p42_' + arm, '*_spline_components.csv')):
        for r in csv.DictReader(open(p, encoding='utf-8-sig')):
            if r.get('spectrum') == 'AS80_Th232Medal':
                out.append(r)
    return out


def main(arms):
    print('%-10s %7s %7s %6s %5s %8s %7s | %6s | %s | %s' % (
        'плечо', 'χ²/ndf', 'χ²пуас', 'невяз', 'опор', 'усил', 'ноль,к', 'сплайн',
        ' '.join('%7s' % ('пик%d' % int(e)) for e in A.PEAKS),
        ' '.join('%9s' % ('%d-%d' % g) for g in A.GAPS)))
    for arm in arms:
        r = run_row(arm)
        c = curves(arm)
        if r is None or c is None:
            print('%-10s — нет прогона/дампа' % arm)
            continue
        kev, net, model, spl = c
        ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
        share = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
        print('%-10s %7.3f %7.2f %5.1f%% %5s %8s %7s | %5.1f%% | %s | %s' % (
            arm, float(r['chi2ndf']), float(r['chi2ndf_pois'] or 'nan'), float(r['model_residual_pct']),
            r['anchors_used'], r['gain'], r['offset_ch'], share,
            ' '.join('%+6.1f%%' % A.narrow(kev, net, model, e) for e in A.PEAKS),
            ' '.join('%+8.1f%%' % A.band(kev, net, model, lo, hi) for lo, hi in A.GAPS)))
    print()
    for arm in arms:
        cs = comps(arm)
        if not cs:
            continue
        keys = [k for k in cs[0].keys()]
        name_k = 'component' if 'component' in keys else keys[1]
        share_k = 'share_pct' if 'share_pct' in keys else ('share' if 'share' in keys else keys[2])
        print('  %-10s %s' % (arm, '; '.join('%s %s' % (c[name_k], c[share_k]) for c in cs[:10])))


if __name__ == '__main__':
    main(sys.argv[1:] or ['disk', 'kfwhm2', 'huber0', 'nomatrix', 'noeq', 'nocascade', 'noanchor', 'nobg'])
