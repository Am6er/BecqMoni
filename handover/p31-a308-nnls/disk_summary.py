# -*- coding: utf-8 -*-
"""П31 12.09.2026, `A308` — корпусный диск AS80_Th232Medal по плечам пола полосы фита (как arms_summary.py П26).

    python handover/p31-a308-nnls/disk_summary.py [плечо ...]

χ²/ndf решателя и ПУАССОНОВСКИЙ (chi2ndf_pois — сравнимый) из `tools/pie/out_p31_<плечо>/AS80x80_spline_runs.csv`,
опор/усиление/ноль, доля сплайна в модели 200–2800 и model/net − 1 в узких окнах ±1 ПШПВ вокруг центра
ДАННЫХ у пиков 238 / 338 / 583 / 911 / 2614 и в промежутках (`corpus/curves_<плечо>/AS80_Th232Medal_curves.csv`).
Окна и ПШПВ — `arms_summary.py` П26 (только чтение).
"""
import csv
import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, 'handover', 'p26-amber22', 'fsa'))
import arms_summary as A  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def run_row(arm):
    for p in glob.glob(os.path.join(ROOT, 'tools', 'pie', 'out_p31_' + arm, '*_spline_runs.csv')):
        for r in csv.DictReader(open(p, encoding='utf-8-sig')):
            if r['spectrum'] == 'AS80_Th232Medal':
                return r
    return None


def curves(arm):
    p = os.path.join(HERE, 'corpus', 'curves_' + arm, 'AS80_Th232Medal_curves.csv')
    if not os.path.exists(p):
        return None
    rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
    kev = [float(r['keV']) for r in rows]
    net = [float(r['net']) for r in rows]
    model = [float(r['model']) for r in rows]
    spl = [float(r['continuum_raw']) for r in rows]
    return kev, net, model, spl


def main(arms):
    print('%-5s %8s %9s %6s %5s %7s %7s | %6s | %s | %s' % (
        'плечо', 'χ²solv', 'χ²pois', 'невяз', 'опор', 'усил', 'ноль,к', 'сплайн',
        ' '.join('%7s' % ('пик%d' % int(e)) for e in A.PEAKS),
        ' '.join('%9s' % ('%d-%d' % g) for g in A.GAPS)))
    for arm in arms:
        r = run_row(arm)
        c = curves(arm)
        if r is None or c is None:
            print('%-5s — нет данных' % arm)
            continue
        kev, net, model, spl = c
        ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
        share = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
        print('%-5s %8.3f %9.3f %5.1f%% %5s %7.4f %7.2f | %5.1f%% | %s | %s' % (
            arm, float(r['chi2ndf']), float(r['chi2ndf_pois']), float(r['model_residual_pct']),
            r.get('anchors_used', ''), float(r['gain']), float(r['offset_ch']), share,
            ' '.join('%+6.1f%%' % A.narrow(kev, net, model, e) for e in A.PEAKS),
            ' '.join('%+8.1f%%' % A.band(kev, net, model, lo, hi) for lo, hi in A.GAPS)))


if __name__ == '__main__':
    main(sys.argv[1:] or ['ctl', 'f20', 'adc', 'h0', 'f20_h0'])
