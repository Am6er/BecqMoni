# -*- coding: utf-8 -*-
"""Невязка модели по полосам энергии из дампа `--dump-curves=` (П22, 12.09.2026).

    python handover/p22-th-disk/bands.py <каталог или файл *_curves.csv> [...]

Правило — ТО ЖЕ, каким П13 считала таблицу на сцене Amber (проверено на её
`dump_after.csv`: −17.5 % в 200–400, −23.5 % в 2000–3100 воспроизводятся):
по полосе суммируются столбцы `net` (измерено, за вычетом фона) и `model`,
печатается `model/net − 1`. Полосы — те же восемь, что у П13. Если дамп несёт
столбец `fit` и он расходится с `net` (подрезка показной кривой, `A284`),
печатается и `model/fit − 1`.
"""
import csv
import glob
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

BANDS = [(15, 40), (40, 70), (70, 95), (95, 200), (200, 400), (400, 1000),
         (1000, 2000), (2000, 3100)]


def report(path):
    rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
    has_fit = 'fit' in rows[0]
    print('== %s ==' % os.path.basename(path))
    print('%-11s %10s %10s %9s%s' % ('полоса', 'net', 'model', 'model/net',
                                    '  model/fit' if has_fit else ''))
    for a, b in BANDS:
        sel = [r for r in rows if a <= float(r['keV']) < b]
        n = sum(float(r['net']) for r in sel)
        m = sum(float(r['model']) for r in sel)
        line = '%4d-%-6d %10.0f %10.0f %+8.1f %%' % (a, b, n, m, 100 * (m / n - 1) if n else float('nan'))
        if has_fit:
            f = sum(float(r['fit']) for r in sel)
            line += '   %+8.1f %%' % (100 * (m / f - 1) if f else float('nan'))
        print(line)


if __name__ == '__main__':
    for arg in sys.argv[1:]:
        paths = sorted(glob.glob(os.path.join(arg, '*_curves.csv'))) if os.path.isdir(arg) else [arg]
        for p in paths:
            report(p)
