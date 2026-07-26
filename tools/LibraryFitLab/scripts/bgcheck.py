# -*- coding: utf-8 -*-
"""Does a measured background reduce the phantom rate?

The claim in the report is that phantom lines come from the fixed background
under-fitting a curved continuum, so the amplitude - clamped at zero - absorbs a
systematic positive residual. The 28.08.2025 pair carries a real measured
background, which BuildFixedBackground folds into the envelope; every other
spectrum has only the SNIP estimate. If the mechanism is right, the phantom rate
on the two background-bearing spectra should be visibly lower.
"""
import os
import json
import numpy as np
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
WITH_BG = {'AS80_Th232_v2', 'AS80_UGlass'}


def main():
    rows = [r for r in json.load(open(os.path.join(HERE, 'scored_decoy.json')))
            if r['kind'] == 'decoy']
    print('%-9s %-12s %6s %10s %10s %9s' % (
        'detector', 'background', 'runs', 'decoy lines', 'phantoms', 'rate %'))
    print('-' * 62)
    groups = defaultdict(lambda: dict(fp=0, decoy=0, n=0))
    for r in rows:
        key = (r['det'], 'measured' if r['spectrum'] in WITH_BG else 'SNIP only')
        g = groups[key]
        g['fp'] += r['fp'] or 0
        g['decoy'] += r['n_decoy_lines'] or 0
        g['n'] += 1
    for key in sorted(groups):
        g = groups[key]
        print('%-9s %-12s %6d %10d %10d %8.1f' % (
            key[0], key[1], g['n'], g['decoy'], g['fp'],
            100.0 * g['fp'] / max(g['decoy'], 1)))

    print()
    print('per spectrum (AS80x80 only):')
    per = defaultdict(lambda: dict(fp=0, decoy=0, n=0))
    for r in rows:
        if r['det'] != 'AS80x80':
            continue
        g = per[r['spectrum']]
        g['fp'] += r['fp'] or 0
        g['decoy'] += r['n_decoy_lines'] or 0
        g['n'] += 1
    for k in sorted(per):
        g = per[k]
        print('   %-16s %-10s %6d runs  %6d decoy lines  %5d phantoms  %5.1f %%' % (
            k, 'measured' if k in WITH_BG else 'SNIP', g['n'], g['decoy'], g['fp'],
            100.0 * g['fp'] / max(g['decoy'], 1)))


if __name__ == '__main__':
    main()
