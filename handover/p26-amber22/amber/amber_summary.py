# -*- coding: utf-8 -*-
"""П26 12.09.2026, `AMBER22` п. 2 — сводка плеч на СЦЕНЕ AMBER (дампы FsaStackShot --dump=).

    python handover/p26-amber22/amber/amber_summary.py [плечо ...]

По плечу: χ²/ndf и невязка (из stack_<плечо>.txt), доля сплайна в модели 200–2800, model/net − 1 в
узких окнах ±1 ПШПВ вокруг центра ДАННЫХ у пиков 238 / 338 / 583 / 911 / 2614 и в промежутках.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'fsa'))
import arms_summary as A  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def head(arm):
    txt = open(os.path.join(HERE, 'stack_%s.txt' % arm), encoding='utf-8', errors='replace').read()
    m = re.search(r'chi2/ndf ([\d.]+), невязка модели ([\d.]+) %', txt)
    s = re.search(r'усиление ([\d.]+), сдвиг ([-\d.]+)', txt)
    return (float(m.group(1)), float(m.group(2))) if m else (float('nan'),) * 2, \
           (float(s.group(1)), float(s.group(2))) if s else (float('nan'),) * 2


def main(arms):
    print('%-13s %6s %6s %6s %7s | %6s | %s | %s' % (
        'плечо', 'χ²/ndf', 'невяз', 'усил', 'ноль,к', 'сплайн',
        ' '.join('%7s' % ('пик%d' % int(e)) for e in A.PEAKS),
        ' '.join('%9s' % ('%d-%d' % g) for g in A.GAPS)))
    for arm in arms:
        p = os.path.join(HERE, 'dump_%s.csv' % arm)
        if not os.path.exists(p):
            print('%-13s — нет дампа' % arm)
            continue
        import csv
        rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
        kev = [float(r['keV']) for r in rows]
        net = [float(r['net']) for r in rows]
        model = [float(r['model']) for r in rows]
        spl = [float(r['continuum_raw']) for r in rows]
        (chi, res), (gain, off) = head(arm)
        ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
        share = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
        print('%-13s %6.3f %5.1f%% %6.4f %7.2f | %5.1f%% | %s | %s' % (
            arm, chi, res, gain, off, share,
            ' '.join('%+6.1f%%' % A.narrow(kev, net, model, e) for e in A.PEAKS),
            ' '.join('%+8.1f%%' % A.band(kev, net, model, lo, hi) for lo, hi in A.GAPS)))


if __name__ == '__main__':
    main(sys.argv[1:] or ['infer', 'infer_nomx2', 'infer_noeq', 'infer_noanch', 'infer_kf2', 'infer_kf8',
                          'infer_noatom', 'infer_nobs', 'infer_z0'])
