# -*- coding: utf-8 -*-
u"""ЧЕТЫРЕ ПОДОЗРЕВАЕМЫХ ниже 100 кэВ, разведённые ЧИСЛОМ (`A283`).

    python handover/p9-pogreshnosti/shift.py <каталог дампа> <спектр> [--edge=100]

`A283` называет четырёх виновников — форма пика, континуум у края,
эффективность, отклик — и требует назвать ЗАМЕРОМ, кто из них. Проверка тут
одна и та же: взять модель, применить к ней ОДНО преобразование, отвечающее
подозреваемому, и посмотреть, сколько хи-квадрата уходит. Кто снимает больше —
тот и виноват; кто не снимает ничего — оправдан.

  Ш (ШКАЛА)      модель сдвигается по каналам на δ (линейная интерполяция).
                 Снимает много — виновата ЭНЕРГЕТИЧЕСКАЯ ШКАЛА внизу
                 (непропорциональность сцинтиллятора), а сетка дрейфа её не
                 берёт: она ЛИНЕЙНАЯ (усиление+сдвиг) на ВЕСЬ спектр.

  У (УРОВЕНЬ)    модель домножается на постоянную k только ниже границы.
                 Снимает много — виновата ЭФФЕКТИВНОСТЬ (уровень кривой внизу).

  Р (РАЗМЫТИЕ)   модель сворачивается с гауссианой ширины s каналов.
                 Снимает много — виновата ФОРМА ПИКА (ПШПВ внизу шкалы).

  К (КОНТИНУУМ)  к модели прибавляется ПРОИЗВОЛЬНАЯ гладкая добавка —
                 наилучшая неотрицательная кусочно-постоянная по 8 каналов.
                 Это ВЕРХНЯЯ ОЦЕНКА того, что может дать континуум, какой бы
                 гибкости он ни был. Не снимает — континуум оправдан
                 ОКОНЧАТЕЛЬНО, снимает — он лишь ПОДОЗРЕВАЕМЫЙ (такая добавка
                 умеет и то, чего сплайн не умеет).

⛔ Все четыре считаются на ОДНОЙ И ТОЙ ЖЕ полосе и ОДНИМИ весами, иначе числа
   несравнимы. Веса — отчётные, w = 1/max(model,1) (сырых отсчётов в дампе нет).

⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВСТРОЕН: та же развёртка гонится и на полосе ВЫШЕ
   границы, где невязки почти нет. Преобразование, которое «улучшает» и там,
   меряет свою гибкость, а не беду; настоящий виновник обязан разделять.
"""
import csv
import io
import math
import os
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8')
except AttributeError:
    pass


def load(d, key):
    with io.open(os.path.join(d, key + '_curves.csv'), encoding='utf-8-sig',
                 newline='') as f:
        rows = list(csv.DictReader(f))
    meas = 'fit' if 'fit' in rows[0] else 'net'
    kev = [float(r['keV']) for r in rows]
    y = [float(r[meas]) for r in rows]
    m = [float(r['model']) for r in rows]
    return meas, kev, y, m


def chi2(y, m, sel, mm=None):
    mm = mm if mm is not None else m
    s = 0.0
    for i in sel:
        w = 1.0 / max(abs(m[i]), 1.0)
        s += (y[i] - mm[i]) ** 2 * w
    return s


def shifted(m, d):
    n = len(m)
    out = [0.0] * n
    for i in range(n):
        x = i - d
        j = int(math.floor(x))
        f = x - j
        a = m[j] if 0 <= j < n else 0.0
        b = m[j + 1] if 0 <= j + 1 < n else 0.0
        out[i] = a * (1.0 - f) + b * f
    return out


def blurred(m, s):
    if s <= 0.0:
        return list(m)
    n = len(m)
    r = max(1, int(math.ceil(3.0 * s)))
    ker = [math.exp(-0.5 * (k / s) ** 2) for k in range(-r, r + 1)]
    z = sum(ker)
    ker = [k / z for k in ker]
    out = [0.0] * n
    for i in range(n):
        v = 0.0
        for k in range(-r, r + 1):
            j = i + k
            if 0 <= j < n:
                v += m[j] * ker[k + r]
        out[i] = v
    return out


def best_addon(y, m, sel, width=8):
    u"""Наилучшая НЕОТРИЦАТЕЛЬНАЯ кусочно-постоянная добавка по `width` каналов —
    верхняя оценка того, что способен взять на себя континуум."""
    mm = list(m)
    sel = sorted(sel)
    for s in range(0, len(sel), width):
        blk = sel[s:s + width]
        num = den = 0.0
        for i in blk:
            w = 1.0 / max(abs(m[i]), 1.0)
            num += (y[i] - m[i]) * w
            den += w
        c = max(0.0, num / den) if den else 0.0
        for i in blk:
            mm[i] += c
    return mm


def sweep(name, y, m, sel, make, grid, fmt):
    base = chi2(y, m, sel)
    best = (base, None)
    for g in grid:
        v = chi2(y, m, sel, make(g))
        if v < best[0]:
            best = (v, g)
    drop = 100.0 * (1.0 - best[0] / base) if base else 0.0
    return name, best[1], drop, fmt


def main():
    d, key = sys.argv[1], sys.argv[2]
    edge = 100.0
    for a in sys.argv[3:]:
        if a.startswith('--edge='):
            edge = float(a[7:])
    meas, kev, y, m = load(d, key)
    band = [i for i in range(len(m)) if m[i] > 0.0]
    lo, hi = band[0], band[-1]
    band = list(range(lo, hi + 1))
    below = [i for i in band if kev[i] < edge]
    above = [i for i in band if kev[i] >= edge]

    print(u'=== %s ===  столбец `%s`%s' % (key, meas,
          u'' if meas == 'fit' else u'   ⚠ ПРИБЛИЖЁННО (A284)'))
    print(u'  полоса %d..%d; ниже %.0f кэВ каналов %d, выше — %d'
          % (lo, hi, edge, len(below), len(above)))
    print(u'  хи-квадрат: ниже %.4g, выше %.4g (доля низа %.1f %%)'
          % (chi2(y, m, below), chi2(y, m, above),
             100.0 * chi2(y, m, below) / (chi2(y, m, below) + chi2(y, m, above))))
    print()
    print(u'  %-28s %14s %12s %12s' %
          (u'преобразование', u'лучшее', u'снято НИЗ', u'снято ВЕРХ'))

    tests = [
        (u'Ш  сдвиг модели, каналов',
         lambda g: shifted(m, g),
         [x * 0.1 for x in range(-40, 41)], '%+.1f'),
        (u'У  уровень модели, ×',
         lambda g: [m[i] * g for i in range(len(m))],
         [0.2 + 0.02 * k for k in range(0, 141)], '%.2f'),
        (u'Р  размытие, сигма каналов',
         lambda g: blurred(m, g),
         [0.0] + [0.25 * k for k in range(1, 25)], '%.2f'),
    ]
    for name, make, grid, fmt in tests:
        n1, g1, d1, _ = sweep(name, y, m, below, make, grid, fmt)
        n2, g2, d2, _ = sweep(name, y, m, above, make, grid, fmt)
        print(u'  %-28s %14s %11.1f %% %11.1f %%'
              % (name, fmt % g1 if g1 is not None else u'—', d1, d2))

    for w in (8, 32):
        mb = best_addon(y, m, below, w)
        ma = best_addon(y, m, above, w)
        b0, a0 = chi2(y, m, below), chi2(y, m, above)
        print(u'  %-28s %14s %11.1f %% %11.1f %%'
              % (u'К  добавка, шаг %d кан.' % w, u'—',
                 100.0 * (1.0 - chi2(y, m, below, mb) / b0) if b0 else 0.0,
                 100.0 * (1.0 - chi2(y, m, above, ma) / a0) if a0 else 0.0))


if __name__ == '__main__':
    main()
