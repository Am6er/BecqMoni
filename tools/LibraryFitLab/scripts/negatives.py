# -*- coding: utf-8 -*-
"""Real sets on samples that do not contain the chain.

The decoy experiment is the controlled null. This is the uncontrolled one: the
genuine Th-232 / Ra-226 sets applied to uranium glass, which contains neither.
Whatever the fit reports there beyond the room background is a false positive on
real data with a real set.
"""
import os
import json
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
K_GRID = [0.0, 0.3, 0.5, 0.7, 0.85, 1.0, 1.3, 1.6, 2.0]
I_GRID = [0.0, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0]

rows = [r for r in json.load(open(os.path.join(HERE, 'scored.json'))) if r['kind'] == 'real']

print('Chain-absent pairs (uranium glass x Th-232 / Ra-226 sets):')
neg = [r for r in rows if r['mode'] == 'neg']
pairs = sorted(set((r['spectrum'], r['chain']) for r in neg))
for s, c in pairs:
    sel = [r for r in neg if r['spectrum'] == s and r['chain'] == c]
    fired = 100.0 * np.mean([r['n_anchor'] > 0 for r in sel])
    print('   %-16s %-8s anchor fires in %3.0f%% of grid points, library peaks: '
          'k=0/i=0 -> %d, k=0.7/i=1 -> %s' % (
              s, c, fired,
              next(r['n_lib'] for r in sel if r['k'] == 0.0 and r['imin'] == 0.0),
              next((r['n_lib'] for r in sel if r['k'] == 0.7 and r['imin'] == 1.0), '-')))

print()
print('Library peaks reported on a chain the sample does not contain (mean over the 4 pairs):')
print('     imin ->' + ''.join('%7.2f' % i for i in I_GRID))
for k in K_GRID:
    line = '     k=%4.2f ' % k
    for i in I_GRID:
        sel = [r for r in neg if r['k'] == k and r['imin'] == i]
        line += '%6.2f ' % (np.mean([r['n_lib'] for r in sel]) if sel else float('nan'))
    print(line)

print()
print('For comparison, on samples that DO contain the chain:')
pos = [r for r in rows if r['mode'] in ('pos', 'head')]
print('     imin ->' + ''.join('%7.2f' % i for i in I_GRID))
for k in K_GRID:
    line = '     k=%4.2f ' % k
    for i in I_GRID:
        sel = [r for r in pos if r['k'] == k and r['imin'] == i]
        line += '%6.2f ' % (np.mean([r['n_lib'] for r in sel]) if sel else float('nan'))
    print(line)
