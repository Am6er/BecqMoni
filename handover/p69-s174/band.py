# -*- coding: utf-8 -*-
"""П69 (S174): куда лёг сплайн подложки — суммы по полосам энергии из `--dump=` FsaStackShot, плечо А против Б.

    python handover/p69-s174/band.py <curves_a.csv> <curves_b.csv> [lo-hi ...]

По каждой полосе (умолчание: 0-30, 30-40, 40-64, 64-100, 30-100, 100-230 кэВ) печатает отсчёты измерения
показного (net) и фита (`fit`, без подрезки — по нему считано число невязки; у А столбца нет — ноль),
модели (верх стека), невязку (net − model и fit − model), сырой сплайн (`continuum_raw`), отвязанный хвост
(`untied_tail`) и каждый слой стека — плечо А против Б; серый слой `continuum` — среди слоёв. Тождество
стека — отдельной строкой: max по каналам |Σ слоёв − model| (слои + невязка = данные — по построению
дампа). Разделитель — точка.
"""
import csv
import io
import sys


def load(path):
    with io.open(path, encoding='utf-8', newline='') as fh:
        rows = list(csv.DictReader(fh))
    return rows


SERVICE = ('ch', 'keV', 'net', 'model', 'continuum_raw', 'untied_tail', 'fit')


def sums(rows, lo, hi):
    keys = [k for k in rows[0].keys() if k not in ('ch', 'keV')]
    acc = dict((k, 0.0) for k in keys)
    for r in rows:
        e = float(r['keV'])
        if lo <= e < hi:
            for k in keys:
                try:
                    acc[k] += float(r[k])
                except (ValueError, TypeError):
                    pass
    return acc


def identity(rows):
    layers = [k for k in rows[0].keys() if k not in SERVICE]
    worst = 0.0
    for r in rows:
        s = 0.0
        for k in layers:
            try:
                s += float(r[k])
            except (ValueError, TypeError):
                pass
        worst = max(worst, abs(s - float(r['model'])))
    return worst, len(layers)


def main():
    a, b = load(sys.argv[1]), load(sys.argv[2])
    bands = sys.argv[3:] or ['0-30', '30-40', '40-64', '64-100', '30-100', '100-230']
    for band in bands:
        lo, hi = [float(x) for x in band.split('-')]
        sa, sb = sums(a, lo, hi), sums(b, lo, hi)
        print('=== %g-%g кэВ ===' % (lo, hi))
        print('%-16s %14s %14s' % ('', 'А (HEAD)', 'Б (правка)'))
        for k in ('net', 'fit', 'model'):
            print('%-16s %14.0f %14.0f' % (k, sa.get(k, 0.0), sb.get(k, 0.0)))
        print('%-16s %14.0f %14.0f' % ('net-model', sa['net'] - sa['model'], sb['net'] - sb['model']))
        print('%-16s %14.0f %14.0f' % ('fit-model', sa.get('fit', 0.0) - sa['model'], sb.get('fit', 0.0) - sb['model']))
        print('%-16s %14.0f %14.0f' % ('continuum_raw', sa.get('continuum_raw', 0.0), sb.get('continuum_raw', 0.0)))
        print('%-16s %14.0f %14.0f' % ('untied_tail', sa.get('untied_tail', 0.0), sb.get('untied_tail', 0.0)))
        names = set(k for k in sa.keys() if k not in SERVICE) | set(k for k in sb.keys() if k not in SERVICE)
        for k in sorted(names):
            print('%-16s %14.0f %14.0f' % (k, sa.get(k, 0.0), sb.get(k, 0.0)))
    wa, na = identity(a)
    wb, nb = identity(b)
    print('тождество стека max|Σ слоёв − model|: А %.3e (%d слоёв), Б %.3e (%d слоёв)' % (wa, na, wb, nb))


if __name__ == '__main__':
    main()
