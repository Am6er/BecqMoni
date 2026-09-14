# -*- coding: utf-8 -*-
"""П67: кто держит модель по полосам — суммы net / model / сплайн / компонент в окнах вокруг 238, 583, 609, 911, 2614
по дампу FsaStackShot (`dump.csv`). python bands.py <каталог прогона> [...]"""
import csv
import io
import os
import sys

BANDS = [('238', 215, 262), ('338', 315, 362), ('583', 555, 612), ('609', 612, 650), ('911', 880, 945), ('1461', 1420, 1500), ('1764', 1720, 1810), ('2614', 2540, 2690)]


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    for d in sys.argv[1:]:
        rows = list(csv.DictReader(io.open(os.path.join(d, 'dump.csv'), encoding='utf-8-sig')))
        comps = [k for k in rows[0].keys() if k not in ('ch', 'keV', 'net', 'model', 'continuum_raw')]
        print('== %s' % os.path.basename(d))
        print('   %-6s %9s %9s %7s | %7s | %s' % ('полоса', 'net', 'model', 'm/n−1', 'сплайн', ' '.join('%8s' % c[:8] for c in comps)))
        for name, lo, hi in BANDS:
            sel = [r for r in rows if lo <= float(r['keV']) < hi]
            n = sum(float(r['net']) for r in sel)
            m = sum(float(r['model']) for r in sel)
            s = sum(float(r['continuum_raw']) for r in sel)
            cs = [sum(float(r[c]) for r in sel) for c in comps]
            print('   %-6s %9.0f %9.0f %+6.1f%% | %6.1f%% | %s' % (name, n, m, 100 * (m / n - 1) if n else float('nan'), 100 * s / m if m else 0, ' '.join('%7.1f%%' % (100 * c / m) if m else '' for c in cs)))


if __name__ == '__main__':
    main()
