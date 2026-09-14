# -*- coding: utf-8 -*-
"""П67 (AMBER22): выбор плотности данными — по трём геометриям.

Из сводки `collect.py` (плечо `--arm=`): для каждого спектра (геометрии) — A(Th-232), невязки на 238 и 2614 и их
разность d(ρ) = pk238 − pk2614 (модель/данные − 1; положительная d = модель поглощает 238 СЛАБЕЕ данных →
плотность мала). У `edge93` d исправляется на цену коробки против кабошона по трассировке (`tracer_tune.txt`:
модель даёт на 238 больше точной на 0.0 / 0.3 / 0.8 / 1.2 % при ρ 2.8 / 3.3 / 3.8 / 4.2 → из d вычитается).
Плотность, выбранная геометрией, — корень d(ρ) = 0 линейной интерполяцией между соседними точками развёртки;
A(Th) в этой точке — интерполяцией A(ρ). Печатает таблицу «геометрия × плотность» и итог.

    python choose.py [--arm=eq] [--md=table.md]
"""
import csv
import io
import math
import sys

sys.path.insert(0, r'D:\BqMoni_Claude\p67\py')
import collect  # noqa: E402

EDGE_BIAS = {2.8: -0.0002, 3.3: 0.0033, 3.8: 0.0078, 4.2: 0.0118, 4.345: 0.022}
PEAKS = ['pk238', 'pk338', 'pk583', 'pk911', 'pk2614']


def interp_root(xs, ys):
    """корень ломаной y(x) = 0; None, если знака не меняет"""
    for (x1, y1), (x2, y2) in zip(zip(xs, ys), zip(xs[1:], ys[1:])):
        if y1 == 0:
            return x1
        if (y1 < 0) != (y2 < 0):
            return x1 + (x2 - x1) * (0 - y1) / (y2 - y1)
    return None


def interp_at(xs, ys, x):
    for (x1, y1), (x2, y2) in zip(zip(xs, ys), zip(xs[1:], ys[1:])):
        if x1 <= x <= x2:
            return y1 + (y2 - y1) * (x - x1) / (x2 - x1)
    return None


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    arm = 'eq'
    mdp = None
    for a in sys.argv[1:]:
        if a.startswith('--arm='):
            arm = a[6:]
        elif a.startswith('--md='):
            mdp = a[5:]
    import glob
    import os
    runs = []
    for d in sorted(glob.glob(os.path.join(collect.OUT, '*__*'))):
        pk = collect.parse_key(os.path.basename(d))
        if not pk:
            continue
        r = collect.read_run(d)
        if r is None:
            continue
        r['spectrum'], r['geom'], r['dens'], r['rho'], r['ring'], r['arm'] = pk
        if r['arm'] == arm and r['ring'] == 'ring10' and r['dens'] != 'p13':
            runs.append(r)
    lines = []
    lines.append('| спектр | ρ | A(Th-232), Бк | z | A(Ra-226), Бк | z | χ²/ndf | невязка | 238 | 338 | 583 | 911 | 2614 | d = 238−2614 (испр.) | сплайн |')
    lines.append('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    per = {}
    for r in sorted(runs, key=lambda r: (r['spectrum'], r['rho'])):
        d = r['pk238'] - r['pk2614']
        if r['geom'] == 'edge93':
            d -= EDGE_BIAS.get(r['rho'], 0.0)
        per.setdefault(r['spectrum'], []).append((r['rho'], r.get('A_Th-232', float('nan')), d, r))
        lines.append('| `%s` | %.1f | %.0f | %.0f | %.0f | %.1f | %.3f | %.1f %% | %s | %+.1f %% | %.0f %% |' % (
            r['spectrum'], r['rho'], r.get('A_Th-232', float('nan')), r.get('z_Th-232', float('nan')),
            r.get('A_Ra-226', float('nan')), r.get('z_Ra-226', float('nan')), r['chi2ndf'], 100 * r['resid'],
            ' | '.join('%+.1f %%' % (100 * r[k]) for k in PEAKS), 100 * d, r.get('spline', float('nan'))))
    lines.append('')
    lines.append('| спектр | ρ по d = 0 | A(Th) при ней, Бк | A(Th) по развёртке, Бк (2.8 … 4.2) | A(Ra-226), Бк |')
    lines.append('|---|---|---|---|---|')
    chosen = {}
    for sp, rows in per.items():
        rows.sort()
        xs = [x[0] for x in rows]
        As = [x[1] for x in rows]
        ds = [x[2] for x in rows]
        root = interp_root(xs, ds)
        A_at = interp_at(xs, As, root) if root is not None else None
        chosen[sp] = (root, A_at)
        ra = [x[3].get('A_Ra-226', float('nan')) for x in rows]
        lines.append('| `%s` | %s | %s | %s | %s |' % (
            sp, ('%.2f' % root) if root is not None else 'нет корня (d: %s)' % ' '.join('%+.1f' % (100 * v) for v in ds),
            ('%.0f' % A_at) if A_at is not None else '—',
            ' / '.join('%.0f' % a for a in As), ' / '.join('%.0f' % a for a in ra)))
    lines.append('')
    roots = [v[0] for v in chosen.values() if v[0] is not None]
    if roots:
        lines.append('плотности по геометриям: %s → среднее %.2f, размах %.2f' % (
            ', '.join('%s %.2f' % (k, v[0]) for k, v in chosen.items() if v[0] is not None), sum(roots) / len(roots), max(roots) - min(roots)))
    for rho in sorted({x[0] for rows in per.values() for x in rows}):
        As = [x[1] for rows in per.values() for x in rows if x[0] == rho]
        if len(As) >= 2:
            m = sum(As) / len(As)
            lines.append('ρ = %.1f: A(Th) по геометриям %s → среднее %.0f Бк, размах %.1f %%' % (
                rho, ' / '.join('%.0f' % a for a in As), m, 100 * (max(As) - min(As)) / m))
    text = '\n'.join(lines)
    print(text)
    if mdp:
        io.open(mdp, 'w', encoding='utf-8', newline='\n').write(text + '\n')


if __name__ == '__main__':
    main()
