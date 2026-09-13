# -*- coding: utf-8 -*-
"""П47 13.09.2026 (A310) — сводка плеч на СЦЕНЕ AMBER (читатель П42 amber_summary.py с путями П47).

    python handover/p47-a310/amber_summary.py --tag=p47 [плечо ...]

Пути: `amber_<tag>/<плечо>/dump.csv`, `amber_<tag>/probe_<плечо>.txt`. χ²/ndf и невязка — как печатает проба
(χ² В ВЕСАХ РЕШАТЕЛЯ), усиление/ноль, доля сплайна в модели 200–2800, model/net − 1 в окнах ±1 ПШПВ у пиков
238 / 338 / 583 / 911 / 2614 и в промежутках; ПУАССОНОВСКИЙ χ²/n по дампу (Σ(net−model)²/max(net,1)) в 15–2800 и
на всех каналах. Окна и ПШПВ — `arms_summary.py` П26 (только чтение).
"""
import csv
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, 'handover', 'p26-amber22', 'fsa'))
import arms_summary as A  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def head(amber, arm):
    txt = open(os.path.join(amber, 'probe_%s.txt' % arm), encoding='utf-8', errors='replace').read()
    m = re.search(r'chi2/ndf ([\d.]+), невязка модели ([\d.]+) %', txt)
    s = re.search(r'усиление ([\d.]+), сдвиг ([-\d.]+)', txt)
    h = re.search(r'HuberM ([\d.]+) → ([\d.]+)', txt)
    return ((float(m.group(1)), float(m.group(2))) if m else (float('nan'),) * 2,
            (float(s.group(1)), float(s.group(2))) if s else (float('nan'),) * 2,
            h.group(2) if h else '3')


def rows_of(amber, arm):
    txt = open(os.path.join(amber, 'probe_%s.txt' % arm), encoding='utf-8', errors='replace').read()
    out = []
    for line in txt.splitlines():
        if line.startswith('ROW\t'):
            f = line.split('\t')
            out.append((f[1], f[2], float(f[3])))
    return out


def pois(kev, net, model, lo, hi):
    ids = [i for i, e in enumerate(kev) if lo <= e < hi]
    return sum((net[i] - model[i]) ** 2 / max(net[i], 1.0) for i in ids) / len(ids)


def main(argv):
    tag = 'p47'
    arms = []
    for a in argv:
        if a.startswith('--tag='):
            tag = a[6:]
        else:
            arms.append(a)
    amber = os.path.join(HERE, 'amber_' + tag)
    if not arms:
        arms = sorted(d for d in os.listdir(amber) if os.path.isdir(os.path.join(amber, d)))
    print('== %s' % amber)
    print('%-10s %-4s %7s %6s %8s %7s | %6s | %s | %s | %9s %9s' % (
        'плечо', 'M', 'χ²/ndf', 'невяз', 'усил', 'ноль,к', 'сплайн',
        ' '.join('%7s' % ('пик%d' % int(e)) for e in A.PEAKS),
        ' '.join('%9s' % ('%d-%d' % g) for g in A.GAPS), 'χ²п/n 15+', 'χ²п/n все'))
    for arm in arms:
        p = os.path.join(amber, arm, 'dump.csv')
        if not os.path.exists(p):
            print('%-10s — нет дампа' % arm)
            continue
        rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
        kev = [float(r['keV']) for r in rows]
        net = [float(r['net']) for r in rows]
        model = [float(r['model']) for r in rows]
        spl = [float(r['continuum_raw']) for r in rows]
        (chi, res), (gain, off), hm = head(amber, arm)
        ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
        share = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
        print('%-10s %-4s %7.3f %5.1f%% %8.6f %7.3f | %5.1f%% | %s | %s | %9.3f %9.3f' % (
            arm, hm, chi, res, gain, off, share,
            ' '.join('%+6.1f%%' % A.narrow(kev, net, model, e) for e in A.PEAKS),
            ' '.join('%+8.1f%%' % A.band(kev, net, model, lo, hi) for lo, hi in A.GAPS),
            pois(kev, net, model, 15, 2800), pois(kev, net, model, -1e9, 1e9)))
    print()
    print('состав (ROW: доля %):')
    for arm in arms:
        if not os.path.exists(os.path.join(amber, 'probe_%s.txt' % arm)):
            continue
        rs = rows_of(amber, arm)
        print('  %-10s %s' % (arm, '; '.join('%s %.2f' % (n, v) for n, k, v in rs)))


if __name__ == '__main__':
    main(sys.argv[1:])
