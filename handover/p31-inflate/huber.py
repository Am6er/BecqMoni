#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""ЧТО ИМЕННО ДЕЛАЕТ ХУБЕР С ХИ-КВАДРАТОМ — оснастка полосы П31 (`A281`).

`inflate` строится НЕ по пуассоновскому хи-квадрату, а по хи-квадрату РЕШАТЕЛЯ,
у которого веса переcчитаны по Хуберу (`FsaAnalyzer.FitHuber`, `HuberM` = 3):

    weights[i] = (1/v) * (m*sigma/|r|)  при |r| > m*sigma,  m = HuberM

Вклад подрезанного канала тогда равен не r^2/v, а m*|r|/sigma. У систематической
невязки |r| = eps*y при sigma = sqrt(y) это eps*sqrt(y) вместо eps^2*y, то есть
КОРЕНЬ вместо линейного роста. Скрипт считает по дампу долю подрезанных каналов
и хи-квадрат по формуле Хубера — и сверяет со значением, вернувшим приложение.
"""

import argparse
import csv
import glob
import io
import os
import re
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from read_runs import read_runs, f, manifest_counts  # noqa: E402
from where import spectrum_arrays  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--curves', required=True)
    ap.add_argument('--spectra', required=True)
    ap.add_argument('--runs', required=True)
    ap.add_argument('--corpus', required=True)
    ap.add_argument('--m', type=float, default=3.0)
    a = ap.parse_args()

    runs = read_runs(a.runs)
    cnt = manifest_counts(a.corpus)
    print('%-28s %12s %8s %10s %10s %10s %10s'
          % ('ключ', 'отсчётов', 'подрез%', 'хи2 пуас', 'хи2 хуб', 'мой хуб', 'подрез доля'))
    keys = sorted(runs, key=lambda k: (re.sub(r'_x\d+_s\d+$', '', k), -cnt.get(k, 0)))
    for key in keys:
        p = os.path.join(a.curves, key + '_curves.csv')
        if not os.path.exists(p):
            continue
        rows = list(csv.DictReader(io.open(p, encoding='utf-8-sig')))
        model = np.array([float(r['model']) for r in rows])
        raw, full, scale = spectrum_arrays(os.path.join(a.spectra, key + '.xml'))
        n = min(len(model), len(raw))
        model, raw, full = model[:n], raw[:n], full[:n]
        y = raw - full
        v = np.maximum(raw, 1.0) + np.maximum(np.abs(full) * scale, scale * scale)
        nz = np.nonzero(model > 0.0)[0]
        lo, hi = (int(nz[0]), int(nz[-1])) if len(nz) else (0, n - 1)
        sl = slice(lo, hi + 1)
        r = np.abs(y[sl] - model[sl])
        sg = np.sqrt(v[sl])
        plain = r * r / v[sl]
        cut = r > a.m * sg
        hub = np.where(cut, a.m * r / sg, plain)
        nb = hi - lo + 1
        print('%-28s %12d %7.2f%% %10.3f %10.3f %10.3f %9.1f%%'
              % (key, cnt.get(key, 0), 100.0 * cut.sum() / nb,
                 plain.sum() / nb, f(runs[key]['chi2ndf']), hub.sum() / nb,
                 100.0 * hub[cut].sum() / max(1e-30, hub.sum())))
    return 0


if __name__ == '__main__':
    sys.exit(main())
