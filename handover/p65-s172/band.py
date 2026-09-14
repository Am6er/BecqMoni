# -*- coding: utf-8 -*-
"""П65 (S172): куда ушёл избыток низа шкалы — суммы по полосам энергии из `--dump=` FsaStackShot.

    python handover/p65-s172/band.py <curves_a.csv> <curves_b.csv> [lo-hi ...]

По каждой полосе (умолчание: 40-64, 64-100, 100-230, 230-260, 30-300 кэВ) печатает отсчёты измерения (net),
модели, невязку (net − model) и каждого слоя стека — плечо А против Б. Разделитель — точка.
"""
import csv
import io
import sys


def load(path):
    with io.open(path, encoding='utf-8', newline='') as fh:
        rows = list(csv.DictReader(fh))
    return rows


def sums(rows, lo, hi):
    keys = [k for k in rows[0].keys() if k not in ('ch', 'keV')]
    acc = dict((k, 0.0) for k in keys)
    for r in rows:
        e = float(r['keV'])
        if lo <= e < hi:
            for k in keys:
                try:
                    acc[k] += float(r[k])
                except ValueError:
                    pass
    return acc


def main():
    a, b = load(sys.argv[1]), load(sys.argv[2])
    bands = sys.argv[3:] or ['40-64', '64-100', '100-230', '230-260', '30-300']
    for band in bands:
        lo, hi = [float(x) for x in band.split('-')]
        sa, sb = sums(a, lo, hi), sums(b, lo, hi)
        print('=== %g-%g кэВ ===' % (lo, hi))
        print('%-16s %14s %14s' % ('', 'А (HEAD)', 'Б (правка)'))
        for k in ('net', 'model'):
            print('%-16s %14.0f %14.0f' % (k, sa[k], sb[k]))
        print('%-16s %14.0f %14.0f' % ('net-model', sa['net'] - sa['model'], sb['net'] - sb['model']))
        layers = [k for k in sa.keys() if k not in ('net', 'model', 'continuum_raw')]
        for k in sorted(set(layers) | set(k for k in sb.keys() if k not in ('net', 'model', 'continuum_raw'))):
            print('%-16s %14.0f %14.0f' % (k, sa.get(k, 0.0), sb.get(k, 0.0)))


if __name__ == '__main__':
    main()
