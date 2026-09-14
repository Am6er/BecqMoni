# -*- coding: utf-8 -*-
r"""П66 — кривые 17 сцен маринелли ДО (узел `<Efficiency>` из git HEAD корпуса — сосуд «из объёма») и ПОСЛЕ
(узел в рабочем дереве — сосуд ОМАСН): отношение ε_после/ε_до по линиям (лог-лог интерполяция, как П64 `vessels.py`)
и по узлам сетки; ожидание П64 §7 — 0.812 плоско по шкале (0.79…0.83).

    python handover/p66-rev23/curves_ab.py [--rev=HEAD] [--csv=<файл>]
"""
import csv
import io
import math
import os
import re
import subprocess
import sys

import numpy as np

sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
SPECTRA = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'spectra')
INDEX = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries', 'index.csv')
LINES = [242.0, 295.2, 351.9, 609.3, 661.7, 1120.3, 1460.8, 1764.5, 2614.5]
PTS = re.compile(r'<Energy>([^<]+)</Energy><Efficiency>([^<]+)</Efficiency><ErrorPercent>([^<]+)</ErrorPercent>')


def curve_of(text):
    m = re.search(r'<Efficiency>\s*<Guid>.*?<Curve>(.*?)</Curve>.*?<ComputeStamp>(.*?)</ComputeStamp>', text, re.S)
    if not m:
        return None
    pts = PTS.findall(m.group(1))
    e = np.array([float(a) for a, _, _ in pts]); f = np.array([float(b) for _, b, _ in pts])
    return e, f, m.group(2)


def interp(e, f, x):
    return math.exp(np.interp(math.log(x), np.log(e), np.log(np.maximum(f, 1e-30))))


def main():
    rev = 'HEAD'; out = None
    for a in sys.argv[1:]:
        if a.startswith('--rev='): rev = a[6:]
        if a.startswith('--csv='): out = a[6:]
    idx = list(csv.DictReader(io.open(INDEX, encoding='utf-8-sig', newline='')))
    keys = sorted(r['spectrum'] for r in idx if r['geometry'].startswith('G1S_mar1l_oisn') or r['geometry'].startswith('G1S_mar1l_risn'))
    rows = []
    print('%-20s ' % 'спектр' + ' '.join('%7.0f' % x for x in LINES) + '   среднее по узлам 30…3000')
    for k in keys:
        old = subprocess.run(['git', 'show', '%s:tools/CORPUS/corpus/spectra/%s.xml' % (rev, k)], cwd=ROOT, stdout=subprocess.PIPE).stdout.decode('utf-8-sig')
        new = io.open(os.path.join(SPECTRA, k + '.xml'), encoding='utf-8-sig').read()
        co, cn = curve_of(old), curve_of(new)
        if co is None or cn is None:
            print('%-20s нет узла (до %s, после %s)' % (k, co is not None, cn is not None)); continue
        ratios = [interp(cn[0], cn[1], x) / interp(co[0], co[1], x) for x in LINES]
        mask = co[0] >= 30
        node_ratio = np.mean(cn[1][mask] / co[1][mask]) if len(cn[0]) == len(co[0]) else float('nan')
        print('%-20s ' % k + ' '.join('%7.3f' % r for r in ratios) + '   %.3f' % node_ratio)
        rows.append([k] + ['%.4f' % r for r in ratios] + ['%.4f' % node_ratio, co[2], cn[2]])
    if rows:
        arr = np.array([[float(x) for x in r[1:len(LINES) + 1]] for r in rows])
        print('%-20s ' % 'медиана' + ' '.join('%7.3f' % x for x in np.median(arr, axis=0)))
        print('%-20s ' % 'мин' + ' '.join('%7.3f' % x for x in arr.min(axis=0)))
        print('%-20s ' % 'макс' + ' '.join('%7.3f' % x for x in arr.max(axis=0)))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            w = csv.writer(fh)
            w.writerow(['spectrum'] + ['r_%g' % x for x in LINES] + ['r_nodes_mean', 'stamp_before', 'stamp_after'])
            w.writerows(rows)
    return 0


if __name__ == '__main__':
    sys.exit(main())
