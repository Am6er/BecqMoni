# -*- coding: utf-8 -*-
"""Phantom rate conditioned on the fit having actually run.

A decoy run where the anchor never matched contributes zero phantoms and a full
set of decoy lines to the denominator, which makes a spectrum look clean when in
truth nothing was ever fitted. The rate that means something is: of the decoy
lines offered to a fit that DID trigger, how many came back as peaks.
"""
import os
import csv
import json
import numpy as np
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
from analyze import TRUTH

WITH_BG = {'AS80_Th232_v2', 'AS80_UGlass'}


def main():
    manifest = {(m['det'], m['set_name']): m for m in
                json.load(open(os.path.join(HERE, 'sets_manifest_decoy.json')))}
    rows = [r for r in json.load(open(os.path.join(HERE, 'scored_decoy.json')))
            if r['kind'] == 'decoy']

    print('%-16s %-8s %-5s %7s %7s %11s %9s %8s' % (
        'spectrum', 'detector', 'bg', 'runs', 'fired', 'decoy lines', 'phantoms', 'rate %'))
    print('-' * 82)
    per = defaultdict(lambda: dict(n=0, fired=0, decoy=0, fp=0))
    for r in rows:
        g = per[r['spectrum']]
        g['n'] += 1
        if r['n_anchor'] > 0:
            g['fired'] += 1
            g['decoy'] += r['n_decoy_lines'] or 0
            g['fp'] += r['fp'] or 0
    # TRUTH — таблица легаси-девятки, снятая до введения корпуса: спектров,
    # добавленных вместе с ним, в ней нет, и обращение по ключу падало KeyError.
    per = {k: v for k, v in per.items() if k in TRUTH}
    for key in sorted(per, key=lambda k: (TRUTH[k]['det'], k)):
        g = per[key]
        rate = 100.0 * g['fp'] / g['decoy'] if g['decoy'] else float('nan')
        print('%-16s %-8s %-5s %7d %7d %11d %9d %8.1f' % (
            key, TRUTH[key]['det'], 'yes' if key in WITH_BG else 'no',
            g['n'], g['fired'], g['decoy'], g['fp'], rate))

    print()
    print('%-8s %7s %7s %11s %9s %8s' % (
        'detector', 'runs', 'fired', 'decoy lines', 'phantoms', 'rate %'))
    print('-' * 56)
    det = defaultdict(lambda: dict(n=0, fired=0, decoy=0, fp=0))
    for key, g in per.items():
        d = det[TRUTH[key]['det']]
        for f in ('n', 'fired', 'decoy', 'fp'):
            d[f] += g[f]
    for k in sorted(det):
        g = det[k]
        print('%-8s %7d %7d %11d %9d %8.1f' % (
            k, g['n'], g['fired'], g['decoy'], g['fp'],
            100.0 * g['fp'] / max(g['decoy'], 1)))

    print()
    print('anchor firing rate on decoy sets, by chain (the anchor is real, so it')
    print('fires exactly when the sample really contains that chain):')
    by = defaultdict(lambda: [0, 0])
    for r in rows:
        b = by[(r['spectrum'], r['chain'])]
        b[0] += 1
        b[1] += 1 if r['n_anchor'] > 0 else 0
    for key in sorted(by):
        n, f = by[key]
        if f:
            print('   %-16s %-8s %3d%%  (%s)' % (
                key[0], key[1], round(100 * f / n),
        TRUTH.get(key[0], {}).get('chains', {}).get(key[1], '?')))


if __name__ == '__main__':
    main()
