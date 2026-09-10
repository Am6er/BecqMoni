# -*- coding: utf-8 -*-
u"""ШКАЛА ВНИЗУ: насколько модель стоит НЕ ТАМ ниже 100 кэВ — по всем дампам (`A283`).

    python handover/p9-pogreshnosti/scale.py <каталог дампа> <runs-каталог> [--edge=100]

Для КАЖДОГО спектра считается наилучший сдвиг модели δ (в каналах), отдельно
на двух полосах:

  НИЗ   — каналы ниже `--edge` (умолчание 100 кэВ), где сидит 85–87 % хи-квадрата;
  ВЕРХ  — каналы выше, где модель, по всем прежним замерам, сходится.

⛔ ВЕРХ — это и есть ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, встроенный в саму мерку. Сдвиг
   меряет ШКАЛУ только тогда, когда он ЕСТЬ внизу и ЕГО НЕТ наверху: сетка
   дрейфа разбора линейная и общая на весь спектр, значит расхождение,
   одинаковое на обоих концах, она бы уже выбрала. Совпавшие концы означали бы,
   что мерка ловит собственную гибкость, а не беду.

Печатается: δ_низ и δ_верх в каналах и в кэВ на середине своей полосы, доля
снятого хи-квадрата, и всё это ПО ЧАСТЯМ КОРПУСА (складывать части нельзя).
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


def load(path):
    with io.open(path, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    meas = 'fit' if 'fit' in rows[0] else 'net'
    kev = [float(r['keV']) for r in rows]
    y = [float(r[meas]) for r in rows]
    m = [float(r['model']) for r in rows]
    return meas, kev, y, m


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


def chi(y, m, sel, mm=None):
    mm = mm if mm is not None else m
    s = 0.0
    for i in sel:
        s += (y[i] - mm[i]) ** 2 / max(abs(m[i]), 1.0)
    return s


def best_shift(y, m, sel, grid):
    base = chi(y, m, sel)
    if base <= 0.0:
        return 0.0, 0.0
    bd, bv = 0.0, base
    for d in grid:
        v = chi(y, m, sel, shifted(m, d))
        if v < bv:
            bd, bv = d, v
    return bd, 100.0 * (1.0 - bv / base)


def parts_of(runs_dir):
    p = {}
    for n in sorted(os.listdir(runs_dir)):
        if n.endswith('_runs.csv'):
            with io.open(os.path.join(runs_dir, n), encoding='utf-8-sig',
                         newline='') as f:
                for r in csv.DictReader(f):
                    p[r['spectrum']] = r['part']
    return p


def quant(v, q):
    v = sorted(v)
    i = q * (len(v) - 1)
    lo = int(i)
    hi = min(lo + 1, len(v) - 1)
    return v[lo] + (v[hi] - v[lo]) * (i - lo)


def main():
    d = sys.argv[1]
    runs = sys.argv[2]
    edge = 100.0
    for a in sys.argv[3:]:
        if a.startswith('--edge='):
            edge = float(a[7:])
    part = parts_of(runs)
    grid = [x * 0.1 for x in range(-60, 61)]
    res = {'known': [], 'unknown': []}
    print(u'%-24s %-8s %9s %9s %9s %9s %8s %8s'
          % (u'спектр', u'часть', u'δ_низ,кан', u'δ_низ,кэВ', u'снято %',
             u'δ_вер,кан', u'снято %', u'χ²низ %'))
    for name in sorted(os.listdir(d)):
        if not name.endswith('_curves.csv'):
            continue
        key = name[:-len('_curves.csv')]
        meas, kev, y, m = load(os.path.join(d, name))
        idx = [i for i in range(len(m)) if m[i] > 0.0]
        if not idx:
            continue
        band = list(range(idx[0], idx[-1] + 1))
        below = [i for i in band if kev[i] < edge]
        above = [i for i in band if kev[i] >= edge]
        if len(below) < 5 or len(above) < 50:
            continue
        cb, ca = chi(y, m, below), chi(y, m, above)
        if cb + ca <= 0.0:
            continue
        db, gb = best_shift(y, m, below, grid)
        da, ga = best_shift(y, m, above, grid)
        step = (kev[band[-1]] - kev[band[0]]) / max(1, len(band) - 1)
        pt = part.get(key, '?')
        print(u'%-24s %-8s %9.1f %9.2f %8.1f %9.1f %8.1f %8.1f'
              % (key, pt, db, db * step, gb, da, ga, 100.0 * cb / (cb + ca)))
        if pt in res:
            res[pt].append((db * step, gb, da * step, ga, 100.0 * cb / (cb + ca)))

    print()
    print(u'=== ПО ЧАСТЯМ (⛔ не складывать) ===')
    for p in ('known', 'unknown'):
        v = res[p]
        if not v:
            print(u'  %-8s — нет' % p)
            continue
        print(u'  %-8s спектров %d' % (p, len(v)))
        print(u'     δ_низ, кэВ: медиана %+.2f, кварт. %+.2f .. %+.2f'
              % (quant([x[0] for x in v], 0.5), quant([x[0] for x in v], 0.25),
                 quant([x[0] for x in v], 0.75)))
        print(u'     δ_верх, кэВ: медиана %+.2f, кварт. %+.2f .. %+.2f   (КОНТРОЛЬ)'
              % (quant([x[2] for x in v], 0.5), quant([x[2] for x in v], 0.25),
                 quant([x[2] for x in v], 0.75)))
        print(u'     сдвигом снято хи-квадрата: НИЗ медиана %.1f %%,'
              u' ВЕРХ медиана %.1f %%   (КОНТРОЛЬ)'
              % (quant([x[1] for x in v], 0.5), quant([x[3] for x in v], 0.5)))
        print(u'     доля хи-квадрата ниже %.0f кэВ: медиана %.1f %%,'
              u' кварт. %.1f .. %.1f %%'
              % (edge, quant([x[4] for x in v], 0.5),
                 quant([x[4] for x in v], 0.25), quant([x[4] for x in v], 0.75)))
        neg = sum(1 for x in v if x[0] < 0.0)
        print(u'     δ_низ ОТРИЦАТЕЛЕН (модель стоит ВЫШЕ по шкале, чем данные)'
              u' у %d из %d' % (neg, len(v)))


if __name__ == '__main__':
    main()
