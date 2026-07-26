# -*- coding: utf-8 -*-
"""Print the (k, imin) grids: recall on real sets, false positives on decoys."""
import os
import json
import numpy as np
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
K_GRID = [0.0, 0.3, 0.5, 0.7, 0.85, 1.0, 1.3, 1.6, 2.0]
I_GRID = [0.0, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0]


def load(name):
    path = os.path.join(HERE, name)
    return json.load(open(path)) if os.path.exists(path) else []


def grid(title, cells, fmt='%6.2f', note=''):
    print('  ' + title + ('   ' + note if note else ''))
    print('     imin ->' + ''.join('%7.2f' % i for i in I_GRID))
    for k in K_GRID:
        line = '     k=%4.2f ' % k
        for i in I_GRID:
            v = cells.get((k, i))
            line += (fmt % v) if v is not None else '      -'
        print(line)


def main():
    real = [r for r in load('scored.json') if r['kind'] == 'real']
    decoy = [r for r in load('scored_decoy.json') if r['kind'] == 'decoy']

    print('=' * 100)
    print('RECALL of strong chain lines (I_chain >= 1%), spectra where the chain is present')
    print('=' * 100)
    dets = sorted(set(r['det'] for r in real))
    for det in dets:
        rows = [r for r in real if r['det'] == det and r['hit'] is not None]
        if not rows:
            continue
        cells, base = {}, {}
        for k in K_GRID:
            for i in I_GRID:
                sel = [r for r in rows if r['k'] == k and r['imin'] == i]
                if not sel:
                    continue
                cells[(k, i)] = 100.0 * sum(r['hit'] for r in sel) / max(sum(r['refs'] for r in sel), 1)
                base[(k, i)] = 100.0 * sum(r['base_hit'] for r in sel) / max(sum(r['refs'] for r in sel), 1)
        b = np.mean([v for v in base.values()]) if base else float('nan')
        grid('%s  recall %%' % det, cells, note='(finder-only baseline %.1f%%)' % b)
        print()

    print('=' * 100)
    print('LIBRARY PEAKS ACCEPTED (real sets, all spectra)')
    print('=' * 100)
    for det in dets:
        rows = [r for r in real if r['det'] == det]
        cells = {}
        for k in K_GRID:
            for i in I_GRID:
                sel = [r for r in rows if r['k'] == k and r['imin'] == i]
                if sel:
                    cells[(k, i)] = np.mean([r['n_lib'] for r in sel])
        grid('%s  mean library peaks per (spectrum,chain)' % det, cells)
        print()

    print('=' * 100)
    print('FINDER PEAKS DESTROYED by the library fit (real sets)')
    print('=' * 100)
    for det in dets:
        rows = [r for r in real if r['det'] == det]
        cells = {}
        for k in K_GRID:
            for i in I_GRID:
                sel = [r for r in rows if r['k'] == k and r['imin'] == i]
                if sel:
                    cells[(k, i)] = np.mean([r['finder_lost'] for r in sel])
        grid('%s  mean finder peaks lost' % det, cells)
        print()

    if not decoy:
        print('(no decoy data yet)')
        return

    print('=' * 100)
    print('FALSE POSITIVE RATE on decoy sets: share of non-existent lines the fit "detects"')
    print('(real anchor, every other chain line displaced onto an energy the sample cannot emit)')
    print('=' * 100)
    for det in sorted(set(r['det'] for r in decoy)):
        rows = [r for r in decoy if r['det'] == det]
        for num, den, label in (('fp', 'n_decoy_lines', 'all displaced lines'),
                                ('fp_clean', 'n_witness', 'witness lines only')):
            cells = {}
            for k in K_GRID:
                for i in I_GRID:
                    sel = [r for r in rows if r['k'] == k and r['imin'] == i]
                    total = sum(r[den] or 0 for r in sel)
                    if sel and total:
                        cells[(k, i)] = 100.0 * sum(r[num] or 0 for r in sel) / total
            grid('%s  FP rate %% - %s' % (det, label), cells)
        w = np.mean([r['n_witness'] or 0 for r in rows if r['k'] == 0.0 and r['imin'] == 0.0])
        d = np.mean([r['n_decoy_lines'] or 0 for r in rows if r['k'] == 0.0 and r['imin'] == 0.0])
        print('     at k=0,imin=0: %.1f displaced lines, of which %.1f are witnesses' % (d, w))
        print()

    print('=' * 100)
    print('COMBINED: recall (real, chain present) minus FP rate (decoy, all displaced lines)')
    print('=' * 100)
    for det in sorted(set(r['det'] for r in decoy)):
        rr = [r for r in real if r['det'] == det and r['hit'] is not None]
        dd = [r for r in decoy if r['det'] == det]
        cells = {}
        for k in K_GRID:
            for i in I_GRID:
                sel = [r for r in rr if r['k'] == k and r['imin'] == i]
                sel_d = [r for r in dd if r['k'] == k and r['imin'] == i]
                den = sum(r['n_decoy_lines'] or 0 for r in sel_d)
                if not sel or not den:
                    continue
                rec = 100.0 * sum(r['hit'] for r in sel) / max(sum(r['refs'] for r in sel), 1)
                fp = 100.0 * sum(r['fp'] or 0 for r in sel_d) / den
                cells[(k, i)] = rec - fp
        grid('%s  recall%% - FPrate%%' % det, cells)
        print()

    print('=' * 100)
    print('ANCHOR FIRING RATE (fraction of runs where the anchor peak was matched)')
    print('=' * 100)
    for src, tag in ((real, 'real'), (decoy, 'decoy')):
        by = defaultdict(list)
        for r in src:
            by[(r['spectrum'], r['chain'], r['mode'])].append(r['n_anchor'] > 0)
        print('  %s sets:' % tag)
        for key in sorted(by):
            print('    %-18s %-8s %-5s %5.0f%%' % (key[0], key[1], key[2], 100 * np.mean(by[key])))
        print()


if __name__ == '__main__':
    main()
