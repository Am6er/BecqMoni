# -*- coding: utf-8 -*-
"""П29 12.09.2026, `AMBER22` — сводка плеч Хубера на СЦЕНЕ AMBER (дампы FsaStackShot --dump=).

    python handover/p29-huber/amber_summary.py [плечо ...]

Копия читателя П26 (`handover/p26-amber22/amber/amber_summary.py`) с путями П29: по плечу χ²/ndf и
невязка (из amber/stack_<плечо>.txt), усиление/ноль, доля сплайна в модели 200–2800, model/net − 1 в
узких окнах ±1 ПШПВ вокруг центра ДАННЫХ у пиков 238 / 338 / 583 / 911 / 2614 и в промежутках
252–290 / 400–440 / 610–700 / 1000–1500. Окна и ПШПВ — `arms_summary.py` П26 (только чтение).
"""
import csv
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
AMBER = os.path.join(HERE, 'amber')
sys.path.insert(0, os.path.join(ROOT, 'handover', 'p26-amber22', 'fsa'))
import arms_summary as A  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def head(arm):
    txt = open(os.path.join(AMBER, 'stack_%s.txt' % arm), encoding='utf-8', errors='replace').read()
    m = re.search(r'chi2/ndf ([\d.]+), невязка модели ([\d.]+) %', txt)
    s = re.search(r'усиление ([\d.]+), сдвиг ([-\d.]+)', txt)
    h = re.search(r'HuberM ([\d.]+) → ([\d.]+)', txt)
    return ((float(m.group(1)), float(m.group(2))) if m else (float('nan'),) * 2,
            (float(s.group(1)), float(s.group(2))) if s else (float('nan'),) * 2,
            h.group(2) if h else '3 (умолч.)')


def rows_of(arm):
    txt = open(os.path.join(AMBER, 'stack_%s.txt' % arm), encoding='utf-8', errors='replace').read()
    out = []
    for line in txt.splitlines():
        if line.startswith('ROW\t'):
            f = line.split('\t')
            out.append((f[1], f[2], float(f[3])))
    return out


def main(arms):
    print('%-9s %-9s %6s %6s %6s %7s | %6s | %s | %s' % (
        'плечо', 'HuberM', 'χ²/ndf', 'невяз', 'усил', 'ноль,к', 'сплайн',
        ' '.join('%7s' % ('пик%d' % int(e)) for e in A.PEAKS),
        ' '.join('%9s' % ('%d-%d' % g) for g in A.GAPS)))
    for arm in arms:
        p = os.path.join(AMBER, 'dump_%s.csv' % arm)
        if not os.path.exists(p):
            print('%-9s — нет дампа' % arm)
            continue
        rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
        kev = [float(r['keV']) for r in rows]
        net = [float(r['net']) for r in rows]
        model = [float(r['model']) for r in rows]
        spl = [float(r['continuum_raw']) for r in rows]
        (chi, res), (gain, off), hm = head(arm)
        ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
        share = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
        print('%-9s %-9s %6.3f %5.1f%% %6.4f %7.2f | %5.1f%% | %s | %s' % (
            arm, hm, chi, res, gain, off, share,
            ' '.join('%+6.1f%%' % A.narrow(kev, net, model, e) for e in A.PEAKS),
            ' '.join('%+8.1f%%' % A.band(kev, net, model, lo, hi) for lo, hi in A.GAPS)))
    print()
    print('состав (ROW: доля %, Single/Nuisance):')
    for arm in arms:
        if not os.path.exists(os.path.join(AMBER, 'stack_%s.txt' % arm)):
            continue
        rs = rows_of(arm)
        print('  %-9s %s' % (arm, '; '.join('%s %.2f' % (n, v) for n, k, v in rs)))


if __name__ == '__main__':
    main(sys.argv[1:] or ['infer', 'huber0', 'huber2', 'huber5', 'huber10', 'huber20', 'huber100'])
