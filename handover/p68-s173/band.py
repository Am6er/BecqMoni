# -*- coding: utf-8 -*-
"""П68 (S173): куда лёг отвязанный хвост — суммы по полосам энергии из `--dump=` FsaStackShot.

    python handover/p68-s173/band.py <curves_a.csv> <curves_b.csv> [lo-hi ...]

По каждой полосе (умолчание: 40-64, 64-100, 100-230, 230-260, 30-300 кэВ) печатает отсчёты измерения
(net), модели (верх стека), невязку (net − model), отвязанный хвост (`untied_tail`, у А столбца нет —
ноль) и каждый слой стека — плечо А против Б. Тождество стека — отдельной строкой: max по каналам
|Σ слоёв − model| и |net − model − невязка| (последнее — ноль по определению, печатается как контроль
чтения). Разделитель — точка.
"""
import csv
import io
import sys


def load(path):
    with io.open(path, encoding='utf-8', newline='') as fh:
        rows = list(csv.DictReader(fh))
    return rows


SERVICE = ('ch', 'keV', 'net', 'model', 'continuum_raw', 'untied_tail')


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
    bands = sys.argv[3:] or ['40-64', '64-100', '100-230', '230-260', '30-300']
    for band in bands:
        lo, hi = [float(x) for x in band.split('-')]
        sa, sb = sums(a, lo, hi), sums(b, lo, hi)
        print('=== %g-%g кэВ ===' % (lo, hi))
        print('%-16s %14s %14s' % ('', 'А (HEAD)', 'Б (правка)'))
        for k in ('net', 'model'):
            print('%-16s %14.0f %14.0f' % (k, sa[k], sb[k]))
        print('%-16s %14.0f %14.0f' % ('net-model', sa['net'] - sa['model'], sb['net'] - sb['model']))
        print('%-16s %14.0f %14.0f' % ('untied_tail', sa.get('untied_tail', 0.0), sb.get('untied_tail', 0.0)))
        names = set(k for k in sa.keys() if k not in SERVICE) | set(k for k in sb.keys() if k not in SERVICE)
        for k in sorted(names):
            print('%-16s %14.0f %14.0f' % (k, sa.get(k, 0.0), sb.get(k, 0.0)))
    wa, na = identity(a)
    wb, nb = identity(b)
    print('тождество стека max|Σ слоёв − model|: А %.3e (%d слоёв), Б %.3e (%d слоёв)' % (wa, na, wb, nb))


if __name__ == '__main__':
    main()
