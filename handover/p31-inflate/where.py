#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""ГДЕ ЖИВЁТ НЕВЯЗКА по шкале — оснастка полосы П31 (`A281`).

Восстанавливает поканальный вклад в хи-квадрат ТЕМИ ЖЕ отчётными весами, какими
его считает `FsaAnalyzer.FitHuber` (`reportWeights = 1/variance`,
`variance = max(raw,1) + max(|full|*scale, scale^2)`), и раскладывает его:

  * по сгущению — какая доля хи-квадрата приходится на 1 %, 5 %, 10 % худших
    каналов; у чисто пуассоновской невязки сгущения нет по построению;
  * по устройству канала — «пик» (линейная часть модели выше континуума) против
    «континуум»;
  * по шкале — по декадам энергии.

⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВОССТАНОВЛЕНИЯ: посчитанный здесь хи2/ndf печатается
   рядом с тем, что вернуло само приложение (`chi2ndf_pois` из `*_runs.csv`).
   Расходятся — читать нечего, и это видно сразу.

  python handover/p31-inflate/where.py --curves=<каталог> --spectra=<копия>/spectra
         --runs=<каталог прогона> --keys=A,B,C
"""

import argparse
import csv
import io
import math
import os
import sys
import xml.etree.ElementTree as ET

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from read_runs import read_runs, f  # noqa: E402


def spectrum_arrays(path):
    root = ET.parse(path).getroot()
    fore = root.find('.//EnergySpectrum')
    back = root.find('.//BackgroundEnergySpectrum')

    def counts(es):
        return np.array([int(p.text or '0')
                         for p in es.find('Spectrum').findall('DataPoint')], dtype=np.float64)

    def live(es):
        for tag in ('LiveTime', 'MeasurementTime'):
            el = es.find(tag)
            if el is not None and el.text and float(el.text) > 0.0:
                return float(el.text)
        return 0.0

    raw = counts(fore)
    full = np.zeros_like(raw)
    scale = 0.0
    if back is not None and live(back) > 0.0:
        scale = live(fore) / live(back)
        full = counts(back) * scale
    return raw, full, scale


def analyse(key, curves_dir, spectra_dir, runs):
    rows = list(csv.DictReader(io.open(os.path.join(curves_dir, key + '_curves.csv'),
                                       encoding='utf-8-sig')))
    kev = np.array([float(r['keV']) for r in rows])
    model = np.array([float(r['model']) for r in rows])
    cont = np.array([float(r['continuum_raw']) for r in rows])
    raw, full, scale = spectrum_arrays(os.path.join(spectra_dir, key + '.xml'))
    n = min(len(model), len(raw))
    kev, model, cont, raw, full = kev[:n], model[:n], cont[:n], raw[:n], full[:n]

    y = raw - full
    variance = np.maximum(raw, 1.0) + np.maximum(np.abs(full) * scale, scale * scale)
    w = 1.0 / variance
    nz = np.nonzero(model > 0.0)[0]
    lo, hi = (int(nz[0]), int(nz[-1])) if len(nz) else (0, n - 1)
    sl = slice(lo, hi + 1)
    r = y[sl] - model[sl]
    c = r * r * w[sl]
    total = float(c.sum())
    nb = hi - lo + 1

    row = runs.get(key, {})
    said = f(row.get('chi2ndf_pois', 'nan'))
    mine = total / nb
    order = np.argsort(-c)
    def share(frac):
        k = max(1, int(round(frac * nb)))
        return 100.0 * float(c[order[:k]].sum()) / total if total > 0 else 0.0

    peak = (model[sl] - cont[sl]) > 0.5 * model[sl]
    out = {
        'key': key, 'band': (lo, hi), 'nb': nb,
        'mine': mine, 'said': said,
        'top1': share(0.01), 'top5': share(0.05), 'top10': share(0.10),
        'peak_chi2': 100.0 * float(c[peak].sum()) / total if total > 0 else 0.0,
        'peak_ch': 100.0 * float(peak.sum()) / nb,
        'peak_counts': 100.0 * float(y[sl][peak].sum()) / max(1.0, float(y[sl].sum())),
        'excess': 100.0 * float((r > 0).sum()) / nb,
    }
    # по декадам энергии
    bands = [(0, 100), (100, 300), (300, 700), (700, 1500), (1500, 3000)]
    out['by_kev'] = []
    for a, b in bands:
        m = (kev[sl] >= a) & (kev[sl] < b)
        if not m.any():
            continue
        out['by_kev'].append((a, b, 100.0 * float(c[m].sum()) / total if total > 0 else 0.0,
                              100.0 * float(m.sum()) / nb))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--curves', required=True)
    ap.add_argument('--spectra', required=True)
    ap.add_argument('--runs', required=True)
    ap.add_argument('--keys', required=True)
    a = ap.parse_args()
    runs = read_runs(a.runs)
    print('%-28s %6s %10s %10s %7s %7s %7s %8s %8s %8s'
          % ('ключ', 'ndf~', 'мой хи2', 'сказано', 'top1%', 'top5%', 'top10%',
             'пик хи2', 'пик кан', 'пик отсч'))
    res = []
    for key in a.keys.split(','):
        o = analyse(key, a.curves, a.spectra, runs)
        res.append(o)
        print('%-28s %6d %10.4f %10.4f %6.1f%% %6.1f%% %6.1f%% %7.1f%% %7.1f%% %7.1f%%'
              % (o['key'], o['nb'], o['mine'], o['said'], o['top1'], o['top5'], o['top10'],
                 o['peak_chi2'], o['peak_ch'], o['peak_counts']))
    print()
    print('доля хи-квадрата по шкале (в скобках — доля каналов):')
    for o in res:
        s = '  '.join('%d-%d: %.1f%% (%.1f%%)' % (a_, b_, p, q) for a_, b_, p, q in o['by_kev'])
        print('  %-28s %s' % (o['key'], s))
    return 0


if __name__ == '__main__':
    sys.exit(main())
