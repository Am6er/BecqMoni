# -*- coding: utf-8 -*-
"""П26 12.09.2026, `AMBER22` п. 2 — сводка плеч абляции на диске AS80_Th232Medal.

    python handover/p26-amber22/fsa/arms_summary.py [плечо ...]

По каждому плечу: χ²/ndf и невязка модели (из `*_spline_runs.csv` каталога `tools/pie/out_p26_<плечо>`),
опор/усиление/ноль, доля сплайна в модели 200–2800 кэВ и model/net − 1 в узких окнах (±1 ПШПВ вокруг
центра ДАННЫХ) пиков 238 / 338 / 583 / 911 / 2614 и в промежутках 252–290 / 400–440 / 610–700
(из `curves_<плечо>/AS80_Th232Medal_curves.csv`). ПШПВ — 7.65 % на 662 по √E.
"""
import csv
import glob
import math
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
PEAKS = [238.632, 338.320, 583.187, 911.204, 2614.511]
GAPS = [(252, 290), (400, 440), (610, 700), (1000, 1500)]
FWHM662 = 0.0765 * 661.657


def fwhm(e):
    return FWHM662 * math.sqrt(e / 661.657)


def smooth(y, k=5):
    n = len(y)
    return [sum(y[max(0, i - k):min(n, i + k + 1)]) / (min(n, i + k + 1) - max(0, i - k)) for i in range(n)]


def run_row(arm):
    for p in glob.glob(os.path.join(ROOT, 'tools', 'pie', 'out_p26_' + arm, '*_spline_runs.csv')):
        for r in csv.DictReader(open(p, encoding='utf-8-sig')):
            if r['spectrum'] == 'AS80_Th232Medal':
                return r
    return None


def curves(arm):
    p = os.path.join(HERE, 'curves_' + arm, 'AS80_Th232Medal_curves.csv')
    if not os.path.exists(p):
        return None
    rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
    kev = [float(r['keV']) for r in rows]
    net = [float(r['net']) for r in rows]
    model = [float(r['model']) for r in rows]
    spl = [float(r['continuum_raw']) for r in rows]
    return kev, net, model, spl


def narrow(kev, net, model, e):
    w = fwhm(e)
    ys = smooth(net)
    ids = [i for i, x in enumerate(kev) if e * 0.94 <= x < e * 1.06]
    cd = kev[max(ids, key=lambda i: ys[i])]
    win = [i for i, x in enumerate(kev) if cd - w <= x < cd + w]
    sn = sum(net[i] for i in win)
    sm = sum(model[i] for i in win)
    return 100.0 * (sm / sn - 1.0) if sn else float('nan')


def band(kev, a, b, lo, hi):
    ids = [i for i, x in enumerate(kev) if lo <= x < hi]
    sa, sb = sum(a[i] for i in ids), sum(b[i] for i in ids)
    return 100.0 * (sb / sa - 1.0) if sa else float('nan')


def main(arms):
    print('%-10s %7s %6s %5s %6s %7s | %6s | %s | %s' % (
        'плечо', 'χ²/ndf', 'невяз', 'опор', 'усил', 'ноль,к', 'сплайн',
        ' '.join('%7s' % ('пик%d' % int(e)) for e in PEAKS),
        ' '.join('%9s' % ('%d-%d' % g) for g in GAPS)))
    for arm in arms:
        r = run_row(arm)
        c = curves(arm)
        if r is None or c is None:
            print('%-10s — нет данных' % arm)
            continue
        kev, net, model, spl = c
        ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
        spline_share = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
        print('%-10s %7.3f %5.1f%% %5s %6.4f %7.2f | %5.1f%% | %s | %s' % (
            arm, float(r['chi2ndf']), float(r['model_residual_pct']), r.get('anchors_used', ''),
            float(r['gain']), float(r['offset_ch']), spline_share,
            ' '.join('%+6.1f%%' % narrow(kev, net, model, e) for e in PEAKS),
            ' '.join('%+8.1f%%' % band(kev, net, model, lo, hi) for lo, hi in GAPS)))


if __name__ == '__main__':
    arms = sys.argv[1:] or ['disk', 'huber0', 'nomatrix', 'nocascade', 'nopileup', 'knots32', 'knots512',
                            'kfwhm8', 'kfwhm2', 'rough10', 'snip', 'noanchor', 'nobg']
    main(arms)
