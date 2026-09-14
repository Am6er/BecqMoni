# -*- coding: utf-8 -*-
"""П26 12.09.2026, `AMBER22` — кто держит модель по полосам: net, model, сплайн и образы.

    python handover/p26-amber22/stack_bands.py <дамп> [ещё]

Полосы нарезаны по пикам ряда Th-232 и промежуткам между ними (шкала дампа — как есть; у
корпусного спектра верх промахивается на ~1.5 %, полосы выше 1500 кэВ там читать с оглядкой).
Печатает по полосе: Σnet, model/net − 1, доля сплайна в модели, доли образов в модели.
"""
import csv
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

BANDS = [(200, 225), (225, 252), (252, 290), (290, 325), (325, 352), (352, 400), (400, 440),
         (440, 500), (500, 560), (560, 610), (610, 700), (700, 760), (760, 880), (880, 940),
         (940, 1000), (1000, 1200), (1200, 1500), (1500, 1650), (1650, 2400), (2400, 2550),
         (2550, 2700), (2700, 2800)]


def report(path, bands=BANDS):
    rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
    names = [k for k in rows[0] if k not in ('ch', 'keV', 'net', 'fit', 'model', 'continuum_raw')]
    print('== %s ==' % path)
    print('%-10s %10s %8s %6s | %s' % ('полоса', 'net', 'm/n−1', 'сплайн',
                                        ' '.join('%7s' % n[:7] for n in names)))
    for a, b in bands:
        sel = [r for r in rows if a <= float(r['keV']) < b]
        n = sum(float(r['net']) for r in sel)
        m = sum(float(r['model']) for r in sel)
        s = sum(float(r['continuum_raw']) for r in sel)
        parts = [sum(float(r[k]) for r in sel) for k in names]
        if n <= 0 or m <= 0:
            continue
        print('%4d-%-5d %10.0f %+7.1f%% %5.0f%% | %s' % (
            a, b, n, 100 * (m / n - 1), 100 * s / m,
            ' '.join('%6.0f%%' % (100 * p / m) for p in parts)))


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
