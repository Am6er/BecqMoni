# -*- coding: utf-8 -*-
"""П75 (S175): куда лёг отвязанный хвост — суммы по полосам энергии из `--dump=` FsaStackShot, плечо А против Б.

    python band.py <curves_a.csv> <curves_b.csv> [lo-hi ...]

По каждой полосе (умолчание: 0-13, 13-56, 56-100, 30-100, 0-100, 100-230 кэВ) печатает отсчёты измерения
показного (net) и фита (`fit`), модели (верх стека), невязку (fit − model), сырой сплайн (`continuum_raw`),
отвязанный хвост в невязке (`untied_tail`), хвост в слоях (`tail`, S175) и каждый слой стека — плечо А
против Б; серый слой `continuum` — среди слоёв. Тождество стека — отдельной строкой: max по каналам
|Σ слоёв − model|. Разделитель — точка.
"""
import csv
import io
import sys


def load(path):
    with io.open(path, encoding='utf-8', newline='') as fh:
        rows = list(csv.DictReader(fh))
    return rows


SERVICE = ('ch', 'keV', 'net', 'model', 'continuum_raw', 'untied_tail', 'fit', 'tail')


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
    bands = sys.argv[3:] or ['0-13', '13-56', '56-100', '30-100', '0-100', '100-230']
    keys = []
    for k in a[0].keys():
        if k not in ('ch', 'keV') and k not in keys:
            keys.append(k)
    for k in b[0].keys():
        if k not in ('ch', 'keV') and k not in keys:
            keys.append(k)
    for band in bands:
        lo, hi = [float(x) for x in band.split('-')]
        sa, sb = sums(a, lo, hi), sums(b, lo, hi)
        print('--- %s кэВ' % band)
        for k in keys:
            va, vb = sa.get(k), sb.get(k)
            print('  %-16s A %14s   B %14s' % (k, '-' if va is None else '%.1f' % va, '-' if vb is None else '%.1f' % vb))
        print('  %-16s A %14.1f   B %14.1f' % ('fit-model', sa.get('fit', 0.0) - sa['model'], sb.get('fit', 0.0) - sb['model']))
    wa, na = identity(a)
    wb, nb = identity(b)
    print('тождество стека max|Σ слоёв − model|: A %.3e (слоёв %d), B %.3e (слоёв %d)' % (wa, na, wb, nb))


if __name__ == '__main__':
    main()
