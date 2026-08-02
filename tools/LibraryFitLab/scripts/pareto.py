# -*- coding: utf-8 -*-
"""Pick (k, imin) from the sweep: what a user actually sees.

Two absolute counts per (spectrum, chain):
  found   strong table lines of the chain recovered (recall, fixed denominator)
  false   phantom lines the same grid point accepts on the decoy set
"""
import os
import json
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
K_GRID = [0.0, 0.3, 0.5, 0.7, 0.85, 1.0, 1.3, 1.6, 2.0]
I_GRID = [0.0, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0]
# fraction of accepted decoy lines that survive an independent two-background
# peak test (verify_phantom.py); the rest are pure continuum artefacts
PHANTOM_PURITY = 0.94


def main():
    real = [r for r in json.load(open(os.path.join(HERE, 'scored.json'))) if r['kind'] == 'real']
    decoy = [r for r in json.load(open(os.path.join(HERE, 'scored_decoy.json'))) if r['kind'] == 'decoy']

    for det in ('ASN16', 'AS80x80', 'RC103'):
        rr = [r for r in real if r['det'] == det and r['hit'] is not None]
        dd = [r for r in decoy if r['det'] == det]
        base = 100.0 * sum(r['base_hit'] for r in rr) / max(sum(r['refs'] for r in rr), 1) \
            if rr else float('nan')
        pts = []
        for k in K_GRID:
            for i in I_GRID:
                s = [r for r in rr if r['k'] == k and r['imin'] == i]
                d = [r for r in dd if r['k'] == k and r['imin'] == i]
                if not s or not d:
                    continue
                rec = 100.0 * sum(r['hit'] for r in s) / max(sum(r['refs'] for r in s), 1)
                fp = PHANTOM_PURITY * np.mean([r['fp'] or 0 for r in d])
                lost = np.mean([r['finder_lost'] for r in
                                [x for x in real if x['det'] == det and x['k'] == k and x['imin'] == i]])
                lines = np.mean([r['set_lines'] for r in s])
                pts.append(dict(k=k, i=i, rec=rec, fp=fp, lost=lost, lines=lines))

        # Pareto front: nothing else has both higher recall and fewer false lines
        front = [p for p in pts
                 if not any(q['rec'] >= p['rec'] + 1e-9 and q['fp'] <= p['fp'] - 1e-9 for q in pts)]
        front.sort(key=lambda p: p['fp'])
        print('=' * 92)
        print('%s   finder-only recall %.1f%%' % (det, base))
        print('=' * 92)
        print('  Pareto front (recall vs phantom lines per spectrum-chain)')
        print('     k     imin   set lines   recall %   phantom lines   finder peaks lost')
        for p in front:
            print('   %4.2f   %4.2f      %5.1f       %6.1f        %6.2f            %5.2f' % (
                p['k'], p['i'], p['lines'], p['rec'], p['fp'], p['lost']))
        best = max(pts, key=lambda p: p['rec'] - 3.0 * p['fp'])
        print('  knee (recall - 3 x phantom): k=%.2f imin=%.2f -> recall %.1f%%, %.2f phantom, %.1f lines'
              % (best['k'], best['i'], best['rec'], best['fp'], best['lines']))
        cheap = max(pts, key=lambda p: p['rec'] - 8.0 * p['fp'])
        print('  strict (recall - 8 x phantom): k=%.2f imin=%.2f -> recall %.1f%%, %.2f phantom, %.1f lines'
              % (cheap['k'], cheap['i'], cheap['rec'], cheap['fp'], cheap['lines']))
        print()


if __name__ == '__main__':
    main()
