# -*- coding: utf-8 -*-
"""П75 (S175): сверка rates.csv двух прогонов FsaStackShot — все столбцы, кроме share_pct, дословно;
разделы untied_tail, tail_in_layer и grey (отображение S173/S174/S175) — отдельно, таблицей.

    python compare_rates.py <a.csv> <b.csv> [--tol=1e-6]

Печатает число строк, число расхождений (кроме share_pct), таблицу share_pct по компонентам (и число
долей, разошедшихся больше допуска --tol, в процентных пунктах), хвосты и серый слой обоих плеч.
Код 1 — есть расхождения вне доли. Разделитель — точка."""
import csv
import sys

SKIP = {'share_pct'}
DISPLAY = ('untied_tail', 'tail_in_layer', 'grey')


def load(path):
    rows = {}
    with open(path, encoding='utf-8', newline='') as f:
        for r in csv.DictReader(f):
            rows[(r['section'], r['name'])] = r
    return rows


def main():
    a, b = load(sys.argv[1]), load(sys.argv[2])
    tol = 1e-6
    for arg in sys.argv[3:]:
        if arg.startswith('--tol='):
            tol = float(arg[6:])
    keys = sorted(set(a) | set(b))
    diff = 0
    for k in keys:
        ra, rb = a.get(k), b.get(k)
        if k[0] in DISPLAY:
            continue
        if ra is None or rb is None:
            print('ТОЛЬКО В', 'A' if rb is None else 'B', k)
            diff += 1
            continue
        for col in ra:
            if col in SKIP:
                continue
            if ra[col] != rb.get(col):
                print('РАСХОЖДЕНИЕ', k, col, ra[col], '!=', rb.get(col))
                diff += 1
    print('строк A %d, B %d; расхождений (кроме share_pct): %d' % (len(a), len(b), diff))
    print('share_pct (допуск %g п.п.):' % tol)
    moved = 0
    for k in keys:
        if k[0] != 'component':
            continue
        sa, sb = a.get(k, {}).get('share_pct', ''), b.get(k, {}).get('share_pct', '')
        try:
            d = float(sb) - float(sa)
        except ValueError:
            d = float('nan')
        flag = ''
        if d == d and abs(d) > tol:
            moved += 1
            flag = '  <- %+.4f' % d
        print('  %-14s A %-12s B %-12s%s' % (k[1], sa[:11], sb[:11], flag))
    print('  долей сдвинулось больше допуска: %d' % moved)
    for k in keys:
        if k[0] in ('untied_tail', 'tail_in_layer'):
            print('  %-14s %-10s A %s (%s)  B %s (%s)' % (k[0], k[1],
                  a.get(k, {}).get('peak_counts', '-'), a.get(k, {}).get('kind', '') or '-',
                  b.get(k, {}).get('peak_counts', '-'), b.get(k, {}).get('kind', '') or '-'))
        if k[0] == 'grey':
            print('  grey %-16s A %s  B %s' % (k[1],
                  a.get(k, {}).get('peak_counts') or a.get(k, {}).get('count_rate', '-'),
                  b.get(k, {}).get('peak_counts') or b.get(k, {}).get('count_rate', '-')))
    sys.exit(1 if diff else 0)


if __name__ == '__main__':
    main()
