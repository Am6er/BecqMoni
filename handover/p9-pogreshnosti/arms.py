# -*- coding: utf-8 -*-
"""Сравнение ДВУХ ПЛЕЧ корпусного прогона — поимённо и по частям корпуса.

    python handover/p9-pogreshnosti/arms.py <плечо-A> <плечо-B> [--label-a=…] [--label-b=…]

Читает `*_runs.csv` и `*_components.csv` обоих каталогов и печатает:

  1. ⛔ ДОЕХАЛ ЛИ КЛЮЧ ДО РЕШЕНИЯ — на ПЕРВЫХ ДВУХ спектрах, числом.
     Признак: при `--huber=0` веса решателя равны отчётным, значит
     `chi2ndf` (метрика решателя) ОБЯЗАН совпасть с `chi2ndf_pois`
     (отчётная). При поставочном Хубере они расходятся. Ключ, доехавший
     только до печати, этого признака не даст.
  2. Смещение амплитуд `decay_s` компонента: (B − A)/A, по частям корпуса.
  3. Смещение значимости z и доли слоя share_pct — там же.

⛔ Части корпуса НЕ СКЛАДЫВАЮТСЯ: понятная считана с матрицей отклика,
   непонятная — из одних пиков.
⚠ Спектры с `error` в `runs.csv` выбрасываются ИЗ ОБОИХ плеч разом:
   разбора у них нет, и сравнивать нечего.
"""
import csv
import io
import os
import sys

# ⛔ Консоль здесь cp1251, и `ε` в тексте роняет печать UnicodeEncodeError
#    ПОСРЕДИ таблицы — то есть часть чисел уже напечатана, а вывод оборван.
try:
    sys.stdout.reconfigure(encoding='utf-8')
except AttributeError:
    pass


def read_runs(d):
    out = {}
    for name in sorted(os.listdir(d)):
        if not name.endswith('_runs.csv'):
            continue
        with io.open(os.path.join(d, name), encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out[row['spectrum']] = row
    return out


def read_comps(d):
    out = {}
    for name in sorted(os.listdir(d)):
        if not name.endswith('_components.csv'):
            continue
        with io.open(os.path.join(d, name), encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out[(row['spectrum'], row['component'])] = row
    return out


def num(s):
    try:
        return float(s)
    except (TypeError, ValueError):
        return None


def quant(v, q):
    if not v:
        return None
    v = sorted(v)
    i = q * (len(v) - 1)
    lo = int(i)
    hi = min(lo + 1, len(v) - 1)
    return v[lo] + (v[hi] - v[lo]) * (i - lo)


def main():
    a_dir, b_dir = sys.argv[1], sys.argv[2]
    la, lb = 'A', 'B'
    for arg in sys.argv[3:]:
        if arg.startswith('--label-a='):
            la = arg[10:]
        elif arg.startswith('--label-b='):
            lb = arg[10:]

    ra, rb = read_runs(a_dir), read_runs(b_dir)
    ca, cb = read_comps(a_dir), read_comps(b_dir)

    common = [s for s in sorted(ra) if s in rb]
    live = [s for s in common
            if not (ra[s].get('error') or rb[s].get('error'))]

    print('=== 1. ДОЕХАЛ ЛИ КЛЮЧ ДО РЕШЕНИЯ — первые ДВА спектра ===')
    print('   признак: веса решателя == отчётные  <=>  chi2ndf == chi2ndf_pois')
    print('   %-24s %12s %12s %8s  |  %12s %12s %8s'
          % ('спектр', la + ' реш.', la + ' пуас.', 'равны', lb + ' реш.', lb + ' пуас.', 'равны'))
    for s in live[:2]:
        xa, ya = num(ra[s]['chi2ndf']), num(ra[s]['chi2ndf_pois'])
        xb, yb = num(rb[s]['chi2ndf']), num(rb[s]['chi2ndf_pois'])
        eq = lambda p, q: 'ДА' if (p is not None and q is not None
                                   and abs(p - q) <= 1e-9 * max(1.0, abs(p))) else 'нет'
        print('   %-24s %12.4f %12.4f %8s  |  %12.4f %12.4f %8s'
              % (s, xa, ya, eq(xa, ya), xb, yb, eq(xb, yb)))
    print()

    print('=== 2. ЧИСЛА ПРОГОНА ПО ЧАСТЯМ (спектров с разбором) ===')
    for part in ('known', 'unknown'):
        ss = [s for s in live if ra[s]['part'] == part]
        if not ss:
            continue
        print('  часть %-8s спектров %d' % (part, len(ss)))
        for col, title in (('chi2ndf', 'chi2/ndf решателя'),
                           ('chi2ndf_pois', 'chi2/ndf пуассон'),
                           ('model_residual_pct', 'невязка ε, %')):
            va = [num(ra[s][col]) for s in ss if num(ra[s][col]) is not None]
            vb = [num(rb[s][col]) for s in ss if num(rb[s][col]) is not None]
            if va and vb:
                print('    %-20s %s медиана %10.4f  сумма %12.1f  |  %s медиана %10.4f  сумма %12.1f'
                      % (title, la, quant(va, 0.5), sum(va), lb, quant(vb, 0.5), sum(vb)))
        print()

    print('=== 3. СМЕЩЕНИЕ АМПЛИТУД (decay_s), по частям ===')
    print('   ⛔ части НЕ складывать')
    for part in ('known', 'unknown'):
        ss = set(s for s in live if ra[s]['part'] == part)
        rel = []
        pairs = []
        for key in sorted(ca):
            if key[0] not in ss or key not in cb:
                continue
            xa, xb = num(ca[key]['decay_s']), num(cb[key]['decay_s'])
            if xa is None or xb is None or xa <= 0.0:
                continue
            rel.append((xb - xa) / xa)
            pairs.append((abs((xb - xa) / xa), key, xa, xb))
        if not rel:
            print('  часть %-8s — пар нет' % part)
            continue
        n_big = sum(1 for r in rel if abs(r) > 0.01)
        print('  часть %-8s пар компонентов %d; медиана (B−A)/A %+.4f %%,'
              ' кварт. %+.3f .. %+.3f %%, |Δ|>1 %% у %d (%.0f %%)'
              % (part, len(rel), 100.0 * quant(rel, 0.5),
                 100.0 * quant(rel, 0.25), 100.0 * quant(rel, 0.75),
                 n_big, 100.0 * n_big / len(rel)))
        print('       медиана |Δ| %.3f %%,  90-я процентиль |Δ| %.3f %%,  максимум %.3f %%'
              % (100.0 * quant([abs(r) for r in rel], 0.5),
                 100.0 * quant([abs(r) for r in rel], 0.9),
                 100.0 * max(abs(r) for r in rel)))
        pairs.sort(reverse=True)
        print('       крупнейшие смещения:')
        for d, key, xa, xb in pairs[:8]:
            print('         %-26s %-14s %12.4g -> %12.4g   %+8.2f %%'
                  % (key[0], key[1], xa, xb, 100.0 * (xb - xa) / xa))
        print()

    print('=== 4. СМЕЩЕНИЕ ЗНАЧИМОСТИ z, по частям ===')
    for part in ('known', 'unknown'):
        ss = set(s for s in live if ra[s]['part'] == part)
        rel = []
        for key in sorted(ca):
            if key[0] not in ss or key not in cb:
                continue
            xa, xb = num(ca[key]['z']), num(cb[key]['z'])
            if xa is None or xb is None or xa <= 0.0:
                continue
            rel.append((xb - xa) / xa)
        if rel:
            print('  часть %-8s пар %d; медиана (B−A)/A z %+.2f %%,'
                  ' кварт. %+.2f .. %+.2f %%'
                  % (part, len(rel), 100.0 * quant(rel, 0.5),
                     100.0 * quant(rel, 0.25), 100.0 * quant(rel, 0.75)))
    print()

    dead_a = [s for s in common if ra[s].get('error')]
    dead_b = [s for s in common if rb[s].get('error')]
    print('=== 5. ОСТАЛЬНОЕ ===')
    print('  спектров в обоих плечах %d, с разбором в обоих %d'
          % (len(common), len(live)))
    print('  с ошибкой: %s %d, %s %d' % (la, len(dead_a), lb, len(dead_b)))
    only_a = sorted(set(ra) - set(rb))
    only_b = sorted(set(rb) - set(ra))
    if only_a or only_b:
        print('  ⚠ только в %s: %s' % (la, ', '.join(only_a)))
        print('  ⚠ только в %s: %s' % (lb, ', '.join(only_b)))


if __name__ == '__main__':
    main()
