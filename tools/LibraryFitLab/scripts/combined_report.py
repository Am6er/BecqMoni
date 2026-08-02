# -*- coding: utf-8 -*-
"""Deconvolution + nuclide set together.

The deconvolution-off half comes from the first sweep (out_sets / out_decoy,
which already ran the whole (k, imin) grid at snr = 4 with the deconvolution
disabled); the deconvolution-on half comes from out_comb, run at each detector's
best deconvolution settings. Both halves share the same spectra, sets, decoys and
scoring, so the two rows of each pair differ only in the deconvolution.
"""
import os
import csv
import json
import numpy as np
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
from analyze import TRUTH, CALIB, fwhm_kev, parse_set
from deconv_report import targets

K_SEL = [0.5, 0.7, 0.85]
I_SEL = [0.5, 1.0, 2.0]


def load(subdir, name, what):
    path = os.path.join(HERE, subdir, '%s_%s.csv' % (name, what))
    if not os.path.exists(path):
        return {} if what == 'runs' else defaultdict(list)
    if what == 'runs':
        with open(path, newline='') as fh:
            return {int(r['run']): r for r in csv.DictReader(fh)}
    out = defaultdict(list)
    with open(path, newline='') as fh:
        for r in csv.DictReader(fh):
            out[int(r['run'])].append(r)
    return out


def score(subdir, real_name, decoy_name, key, manifest):
    """-> (per-(k,imin) counters, baseline counters) for one spectrum."""
    truth = TRUTH[key]
    det = truth['det']
    tgt, res = targets(key)
    cells = defaultdict(lambda: dict(hit=0, refs=0, lib=0, lost=0, ms=0, n=0,
                                     fp=0, decoy=0, nd=0))
    base = dict(hit=0, refs=0, ms=0, n=0, finder=0)

    runs = load(subdir, real_name, 'runs')
    peaks = load(subdir, real_name, 'peaks')
    base_finder = None
    for run, meta in runs.items():
        if meta['set'] == '-':
            base_finder = int(meta['n_finder'])
    for run, meta in runs.items():
        e = np.array([float(p['energy']) for p in peaks.get(run, [])])
        hit = sum(1 for r in tgt
                  if e.size and np.min(np.abs(e - r['energy'])) <= max(0.5 * fwhm_kev(res, r['energy']), 3.0))
        if meta['set'] == '-':
            base['hit'] += hit
            base['refs'] += len(tgt)
            base['ms'] += int(meta['ms'])
            base['n'] += 1
            continue
        chain, kind, k, imin = parse_set(meta['set'])
        if k not in K_SEL or imin not in I_SEL:
            continue
        c = cells[(k, imin)]
        if truth['chains'][chain] in ('pos', 'head'):
            c['hit'] += hit
            c['refs'] += len(tgt)
        c['lib'] += int(meta['n_library'])
        if base_finder is not None:
            c['lost'] += base_finder - int(meta['n_finder'])
        c['ms'] += int(meta['ms'])
        c['n'] += 1

    druns = load(subdir, decoy_name, 'runs')
    dpeaks = load(subdir, decoy_name, 'peaks')
    for run, meta in druns.items():
        if meta['set'] == '-':
            continue
        chain, kind, k, imin = parse_set(meta['set'])
        if k not in K_SEL or imin not in I_SEL:
            continue
        info = manifest.get((det, meta['set']), {})
        fp = sum(1 for p in dpeaks.get(run, [])
                 if p['origin'] == 'Library' and p['anchor'] != '1')
        c = cells[(k, imin)]
        c['fp'] += fp
        c['decoy'] += info.get('n_decoy', 0)
        c['nd'] += 1
    return cells, base


def main():
    manifest = {(m['det'], m['set_name']): m for m in
                json.load(open(os.path.join(HERE, 'sets_manifest_decoy.json')))}
    best = json.load(open(os.path.join(HERE, 'deconv_best.json')))

    totals = defaultdict(lambda: defaultdict(lambda: dict(
        hit=0, refs=0, lib=0, lost=0, ms=0, n=0, fp=0, decoy=0, nd=0)))
    bases = defaultdict(lambda: dict(hit=0, refs=0, ms=0, n=0))

    for key, truth in TRUTH.items():
        det = truth['det']
        for tag, subdir, real_name, decoy_name in (
                ('off', None, key, key),
                ('on', 'out_comb', key + '_real', key + '_decoy')):
            if tag == 'off':
                cells, base = score('out_sets', key, key, key, manifest)
                # decoys of the off half live in a different directory
                _, _ = None, None
                dcells, _ = score('out_decoy', key, key, key, manifest)
                for kk, v in dcells.items():
                    cells[kk]['fp'] = v['fp']
                    cells[kk]['decoy'] = v['decoy']
                    cells[kk]['nd'] = v['nd']
            else:
                cells, base = score(subdir, real_name, decoy_name, key, manifest)
            for kk, v in cells.items():
                t = totals[(det, tag)][kk]
                for f in ('hit', 'refs', 'lib', 'lost', 'ms', 'n', 'fp', 'decoy', 'nd'):
                    t[f] += v[f]
            b = bases[(det, tag)]
            for f in ('hit', 'refs', 'ms', 'n'):
                b[f] += base[f]

    print('%-9s %-6s %5s %5s | %8s %8s %8s %9s %8s %8s' % (
        'det', 'deconv', 'k', 'imin', 'recall%', 'vs base', 'lib pks', 'phantom', 'lost', 'ms'))
    print('-' * 92)
    for det in ('ASN16', 'AS80x80', 'RC103'):
        cfg = best.get(det, {})
        for tag in ('off', 'on'):
            b = bases[(det, tag)]
            basev = 100.0 * b['hit'] / b['refs'] if b['refs'] else float('nan')
            label = 'off' if tag == 'off' else 'on'
            note = '' if tag == 'off' else '  (roi=%s extra=%s)' % (cfg.get('roi'), cfg.get('extra'))
            print('%-9s %-6s %5s %5s | %8.1f %8s %8s %9s %8s %8.0f%s' % (
                det, label, '-', '-', basev, '-', '-', '-', '-',
                b['ms'] / max(b['n'], 1), note))
            for kk in sorted(totals[(det, tag)]):
                v = totals[(det, tag)][kk]
                if not v['refs'] or not v['nd']:
                    continue
                rec = 100.0 * v['hit'] / v['refs']
                print('%-9s %-6s %5.2f %5.2f | %8.1f %+8.1f %8.2f %9.2f %8.2f %8.0f' % (
                    '', '', kk[0], kk[1], rec, rec - basev,
                    v['lib'] / v['n'], v['fp'] / v['nd'], v['lost'] / v['n'],
                    v['ms'] / v['n']))
        print()


if __name__ == '__main__':
    main()
