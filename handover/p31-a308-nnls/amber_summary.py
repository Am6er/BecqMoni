# -*- coding: utf-8 -*-
"""П31 12.09.2026, `A308` — сводка плеч на СЦЕНЕ AMBER (дампы FsaStackShot через FsaNnlsDumpProbe).

    python handover/p31-a308-nnls/amber_summary.py [плечо ...]

Читатель П29 (`amber_summary.py`) с путями П31 (`amber/<плечо>/dump.csv`, `amber/probe_<плечо>.txt`):
χ²/ndf и невязка (как печатает проба — χ² В ВЕСАХ РЕШАТЕЛЯ), усиление/ноль, доля сплайна в модели
200–2800, model/net − 1 в окнах ±1 ПШПВ у пиков 238 / 338 / 583 / 911 / 2614 и в промежутках; плюс
ПУАССОНОВСКИЙ χ²/n по дампу (мера П29: Σ(net−model)²/max(net,1), 15–2800) и та же мера на ПОЛНОМ
диапазоне каналов 0…8191 — там, где решатель на самом деле считает (полоса фита `0…8191` при
поставочном `FitFloor = Off`). Окна и ПШПВ — `arms_summary.py` П26 (только чтение).
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
    txt = open(os.path.join(AMBER, 'probe_%s.txt' % arm), encoding='utf-8', errors='replace').read()
    m = re.search(r'chi2/ndf ([\d.]+), невязка модели ([\d.]+) %', txt)
    s = re.search(r'усиление ([\d.]+), сдвиг ([-\d.]+)', txt)
    h = re.search(r'HuberM ([\d.]+) → ([\d.]+)', txt)
    return ((float(m.group(1)), float(m.group(2))) if m else (float('nan'),) * 2,
            (float(s.group(1)), float(s.group(2))) if s else (float('nan'),) * 2,
            h.group(2) if h else '3')


def rows_of(arm):
    txt = open(os.path.join(AMBER, 'probe_%s.txt' % arm), encoding='utf-8', errors='replace').read()
    out = []
    for line in txt.splitlines():
        if line.startswith('ROW\t'):
            f = line.split('\t')
            out.append((f[1], f[2], float(f[3])))
    return out


def pois(kev, net, model, lo, hi):
    ids = [i for i, e in enumerate(kev) if lo <= e < hi]
    return sum((net[i] - model[i]) ** 2 / max(net[i], 1.0) for i in ids) / len(ids)


def main(arms):
    print('%-14s %-5s %7s %6s %6s %7s | %6s | %s | %s | %9s %9s' % (
        'плечо', 'M', 'χ²/ndf', 'невяз', 'усил', 'ноль,к', 'сплайн',
        ' '.join('%7s' % ('пик%d' % int(e)) for e in A.PEAKS),
        ' '.join('%9s' % ('%d-%d' % g) for g in A.GAPS), 'χ²п/n 15+', 'χ²п/n все'))
    for arm in arms:
        p = os.path.join(AMBER, arm, 'dump.csv')
        if not os.path.exists(p):
            print('%-14s — нет дампа' % arm)
            continue
        rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
        kev = [float(r['keV']) for r in rows]
        net = [float(r['net']) for r in rows]
        model = [float(r['model']) for r in rows]
        spl = [float(r['continuum_raw']) for r in rows]
        (chi, res), (gain, off), hm = head(arm)
        ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
        share = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
        print('%-14s %-5s %7.3f %5.1f%% %6.4f %7.2f | %5.1f%% | %s | %s | %9.3f %9.3f' % (
            arm, hm, chi, res, gain, off, share,
            ' '.join('%+6.1f%%' % A.narrow(kev, net, model, e) for e in A.PEAKS),
            ' '.join('%+8.1f%%' % A.band(kev, net, model, lo, hi) for lo, hi in A.GAPS),
            pois(kev, net, model, 15, 2800), pois(kev, net, model, -1e9, 1e9)))
    print()
    print('состав (ROW: доля %):')
    for arm in arms:
        if not os.path.exists(os.path.join(AMBER, 'probe_%s.txt' % arm)):
            continue
        rs = rows_of(arm)
        print('  %-14s %s' % (arm, '; '.join('%s %.2f' % (n, v) for n, k, v in rs)))


if __name__ == '__main__':
    main(sys.argv[1:] or ['infer', 'huber05', 'huber0', 'h3_noanch', 'h0_noanch',
                          'adc_h3', 'adc_h0', 'adc_h3_noanch', 'adc_h0_noanch', 'f20_h3', 'f20_h0'])
