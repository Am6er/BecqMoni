# -*- coding: utf-8 -*-
# П18 12.09.2026: сверка двух выходов корпусного прогона СТРОКА В СТРОКУ.
#   python handover\p18-fsa-measures\p18_cmp.py <out_A> <out_B> [--tol=1e-9]
# Сверяются *_runs.csv (по ключу спектра: все столбцы) и *_components.csv
# (по паре ключ+компонент: все столбцы). Печатает число строк и число
# расхождений; код 0 — расхождений нет, 1 — есть, 2 — файлы не сошлись по составу.
import csv
import glob
import os
import sys


def rows(path, keycols):
    out = {}
    with open(path, encoding='utf-8-sig', newline='') as f:
        r = csv.reader(f)
        head = next(r)
        idx = [head.index(c) for c in keycols]
        for line in r:
            if not line:
                continue
            k = tuple(line[i] for i in idx)
            out[k] = line
    return head, out


def same(a, b, tol):
    if a == b:
        return True
    try:
        x, y = float(a), float(b)
    except ValueError:
        return False
    return abs(x - y) <= tol * max(1.0, abs(x), abs(y))


def compare(dir_a, dir_b, suffix, keycols, tol, skip=()):
    total = 0
    diff = 0
    missing = 0
    names = sorted(os.path.basename(p) for p in glob.glob(os.path.join(dir_a, '*' + suffix)))
    names_b = sorted(os.path.basename(p) for p in glob.glob(os.path.join(dir_b, '*' + suffix)))
    if names != names_b:
        print('  файлы разошлись: A %s / B %s' % (names, names_b))
        return None
    details = []
    for name in names:
        ha, ra = rows(os.path.join(dir_a, name), keycols)
        hb, rb = rows(os.path.join(dir_b, name), keycols)
        if ha != hb:
            print('  заголовок разошёлся: %s' % name)
            return None
        for k, va in ra.items():
            total += 1
            vb = rb.get(k)
            if vb is None:
                missing += 1
                details.append('%s: нет в B: %s' % (name, k))
                continue
            bad = [ha[i] for i in range(min(len(va), len(vb))) if ha[i] not in skip and not same(va[i], vb[i], tol)]
            if bad or len(va) != len(vb):
                diff += 1
                details.append('%s %s: %s' % (name, k, ', '.join(bad[:6])))
        for k in rb:
            if k not in ra:
                missing += 1
                details.append('%s: нет в A: %s' % (name, k))
    print('  %s: строк %d, расхождений %d, отсутствует %d' % (suffix, total, diff, missing))
    for d in details[:40]:
        print('    ' + d)
    return diff + missing


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    tol = 1e-9
    for a in sys.argv[1:]:
        if a.startswith('--tol='):
            tol = float(a[6:])
    if len(args) != 2:
        print(__doc__ or 'p18_cmp.py <out_A> <out_B>')
        return 2
    a, b = args
    print('A = %s' % a)
    print('B = %s' % b)
    r1 = compare(a, b, '_runs.csv', ['spectrum'], tol, skip=('ms', 'cpu_ms'))
    r2 = compare(a, b, '_components.csv', ['spectrum', 'component'], tol)
    if r1 is None or r2 is None:
        return 2
    return 0 if r1 + r2 == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
