# -*- coding: utf-8 -*-
"""П63 (S171, второе правило): побитовое сравнение двух csv `--rates=` (FsaStackShot).

    python handover/p63-s171/compare.py <a.csv> <b.csv>

Плечо Б пишет на один столбец больше (`unmeasurable_at`, П63): у строк плеча А
он считается пустым. Всё остальное сравнивается дословно, строка к строке.
Код 0 — расхождений нет; 1 — есть (печатаются первые двадцать).
"""
import csv, io, sys

def load(path):
    with io.open(path, encoding='utf-8', newline='') as fh:
        return [row for row in csv.reader(fh)]

def norm(row, width):
    row = list(row)
    if len(row) < width:
        row += [''] * (width - len(row))
    return row

def main():
    a, b = load(sys.argv[1]), load(sys.argv[2])
    width = max(max(len(r) for r in a), max(len(r) for r in b))
    n = max(len(a), len(b))
    diff = 0
    for i in range(n):
        ra = norm(a[i], width) if i < len(a) else None
        rb = norm(b[i], width) if i < len(b) else None
        if i == 0 and ra and rb:
            # шапка: у Б лишний столбец `unmeasurable_at` в конце, у А там пусто
            ha = [c for c in ra if c not in ('', 'unmeasurable_at')]
            hb = [c for c in rb if c not in ('', 'unmeasurable_at')]
            if ha == hb:
                continue
        if ra != rb:
            diff += 1
            if diff <= 20:
                print('  строка %d: %s | %s' % (i + 1, ra[:6] if ra else None, rb[:6] if rb else None))
    print('%s vs %s: строк %d, расхождений %d' % (sys.argv[1], sys.argv[2], n, diff))
    return 0 if diff == 0 else 1

if __name__ == '__main__':
    sys.exit(main())
