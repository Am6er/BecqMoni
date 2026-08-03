#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Сравнение восстановленных кривых эффективности с поставочными.

    python tools/effmaker/plot_curves.py

Читает tools/effmaker/out/<группа>_<режим>_curve.csv и кривые из
%APPDATA%\\BecqMoni\\config\\ROI, рисует их в логарифмических осях вместе с
измеренными точками и печатает таблицу отношений.
"""
import csv
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
ROI = os.path.join(os.environ.get('APPDATA', ''), 'BecqMoni', 'config', 'ROI')

GROUPS = {
    'ASN16': 'Nano - cilinder (close distance).xml',
    'RC103': 'RadiaCode - cilinder.xml',
}
PROBE = [60, 100, 186, 239, 352, 583, 662, 911, 1120, 1461, 1765, 2615]


def load_reference(path):
    text = open(path, encoding='utf-8', errors='replace').read()
    pairs = re.findall(
        r'<Energy>([-0-9.eE+]+)</Energy>\s*<Efficiency>([-0-9.eE+]+)</Efficiency>', text)
    return sorted((float(a), float(b)) for a, b in pairs if float(b) > 0)


def load_curve(path):
    curve, points = [], []
    with open(path, encoding='utf-8') as fh:
        rows = list(csv.reader(fh))
    mode = 'curve'
    for row in rows[1:]:
        if not row:
            continue
        if row[0] == 'spectrum':
            mode = 'obs'
            header = row
            continue
        if mode == 'curve':
            curve.append((float(row[0]), float(row[1])))
        elif len(row) == len(header):
            d = dict(zip(header, row))
            if d['accepted'] == '1':
                points.append((float(d['E_keV']), float(d['eps_measured']), d['chain']))
    return sorted(curve), points


def interp(curve, energy):
    import math
    if energy <= curve[0][0]:
        return curve[0][1]
    if energy >= curve[-1][0]:
        return curve[-1][1]
    for i in range(1, len(curve)):
        if curve[i][0] >= energy:
            x0, y0 = curve[i - 1]
            x1, y1 = curve[i]
            f = (math.log(energy) - math.log(x0)) / (math.log(x1) - math.log(x0))
            return math.exp(math.log(y0) + f * (math.log(y1) - math.log(y0)))
    return curve[-1][1]


def main():
    try:
        import matplotlib
        matplotlib.use('Agg')
        import matplotlib.pyplot as plt
    except ImportError:
        plt = None

    for group, roi_name in GROUPS.items():
        fitted_path = os.path.join(OUT, '%s_withref_curve.csv' % group)
        if not os.path.exists(fitted_path):
            print('нет %s — пропуск' % fitted_path)
            continue
        reference = load_reference(os.path.join(ROI, roi_name))
        curve, points = load_curve(fitted_path)

        print()
        print('=== %s : %s ===' % (group, roi_name))
        print('%8s %14s %14s %8s' % ('E, кэВ', 'поставочная', 'по измерениям', 'отн.'))
        for e in PROBE:
            a, b = interp(reference, e), interp(curve, e)
            print('%8d %14.4e %14.4e %8.2f' % (e, a, b, b / a if a else 0))

        if plt is None:
            continue
        fig, ax = plt.subplots(figsize=(9, 6))
        ax.plot([e for e, _ in reference], [v for _, v in reference],
                '--', color='#909090', label='поставочная (ROI)')
        ax.plot([e for e, _ in curve], [v for _, v in curve],
                '-', color='#1f6fb2', lw=2, label='по измерениям')
        chains = sorted({c for _, _, c in points})
        colours = ['#d9534f', '#5cb85c', '#f0ad4e', '#9b59b6']
        for i, chain in enumerate(chains):
            xs = [e for e, _, c in points if c == chain]
            ys = [v for _, v, c in points if c == chain]
            ax.plot(xs, ys, 'o', ms=5, alpha=0.75,
                    color=colours[i % len(colours)], label=chain)
        ax.set_xscale('log')
        ax.set_yscale('log')
        ax.set_xlabel('E, кэВ')
        ax.set_ylabel('эффективность')
        ax.set_title('%s — кривая регистрации' % group)
        ax.grid(True, which='both', alpha=0.25)
        ax.legend()
        path = os.path.join(OUT, '%s_curves.png' % group)
        fig.savefig(path, dpi=110, bbox_inches='tight')
        plt.close(fig)
        print('график: %s' % path)


if __name__ == '__main__':
    main()
