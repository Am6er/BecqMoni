# -*- coding: utf-8 -*-
"""Ширина и положение пиков в ДАННЫХ и в МОДЕЛИ по дампу `--dump-curves=` (П22).

    python handover/p22-th-disk/peakwidth.py <файл *_curves.csv> [...]

Для линий 238.6, 338.3, 583.2, 911.2, 2614.5 кэВ: в окне ±6 % от линии вычитается
прямая по краям окна, берётся центроид и ПШПВ (полуширина по интерполяции на
полувысоте) отдельно у `net` и у `model`. Печатается отношение ширин
модель/данные и сдвиг центроида. Грубо, но одинаково для обоих столбцов.
"""
import csv
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

LINES = [238.63, 338.32, 583.19, 911.20, 1460.8, 2614.51]


def fwhm(x, y):
    n = len(y)
    # линейный фон по крайним 10 % точек
    k = max(2, n // 10)
    b0 = sum(y[:k]) / k
    b1 = sum(y[-k:]) / k
    bg = [b0 + (b1 - b0) * i / (n - 1) for i in range(n)]
    z = [a - b for a, b in zip(y, bg)]
    imax = max(range(n), key=lambda i: z[i])
    top = z[imax]
    if top <= 0:
        return None
    half = top / 2.0
    # влево
    i = imax
    while i > 0 and z[i] > half:
        i -= 1
    xl = x[i] + (x[i + 1] - x[i]) * (half - z[i]) / (z[i + 1] - z[i]) if z[i + 1] != z[i] else x[i]
    j = imax
    while j < n - 1 and z[j] > half:
        j += 1
    xr = x[j - 1] + (x[j] - x[j - 1]) * (z[j - 1] - half) / (z[j - 1] - z[j]) if z[j - 1] != z[j] else x[j]
    lo, hi = i, j
    area = sum(z[lo:hi + 1])
    cen = sum(x[t] * z[t] for t in range(lo, hi + 1)) / area if area > 0 else float('nan')
    return xr - xl, cen, area


def report(path):
    rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
    e = [float(r['keV']) for r in rows]
    print('== %s ==' % os.path.basename(path))
    print('%8s %10s %10s %8s %10s %10s %8s' % ('линия', 'ПШПВ дан', 'ПШПВ мод', 'мод/дан', 'центр дан', 'центр мод', 'сдвиг'))
    for line in LINES:
        w = 0.06 * line
        sel = [i for i, v in enumerate(e) if line - w * 1.8 <= v <= line + w * 1.8]
        if len(sel) < 8:
            continue
        x = [e[i] for i in sel]
        d = fwhm(x, [float(rows[i]['net']) for i in sel])
        m = fwhm(x, [float(rows[i]['model']) for i in sel])
        if not d or not m:
            continue
        print('%8.1f %10.2f %10.2f %8.3f %10.2f %10.2f %+8.2f' % (line, d[0], m[0], m[0] / d[0], d[1], m[1], m[1] - d[1]))


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
