# -*- coding: utf-8 -*-
"""How separable are real library lines from phantom ones by the Fisher z alone?

Takes the accepted library peaks of the real sets (on spectra where the chain is
present) as the positive class and the accepted library peaks of the decoy sets
as the negative class, and sweeps the acceptance threshold that
LibraryPeakFitter.SignificanceZ currently fixes at 4.
"""
import os
import csv
import json
import numpy as np
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
from analyze import TRUTH, DET_RANGE, U238_HEAD, fwhm_kev, CALIB, parse_set


def peaks_of(subdir, key):
    path = os.path.join(HERE, subdir, '%s_peaks.csv' % key)
    out = defaultdict(list)
    if not os.path.exists(path):
        return out
    with open(path, newline='') as fh:
        for row in csv.DictReader(fh):
            out[int(row['run'])].append(row)
    return out


def runs_of(subdir, key):
    path = os.path.join(HERE, subdir, '%s_runs.csv' % key)
    out = {}
    if not os.path.exists(path):
        return out
    with open(path, newline='') as fh:
        for row in csv.DictReader(fh):
            out[int(row['run'])] = row
    return out


def collect(subdir, manifest_name, want_kind):
    manifest = {(m['det'], m['set_name']): m for m in
                json.load(open(os.path.join(HERE, manifest_name)))}
    bucket = defaultdict(list)     # (det, k, imin) -> [z, ...]
    for key, truth in TRUTH.items():
        det = truth['det']
        runs = runs_of(subdir, key)
        peaks = peaks_of(subdir, key)
        res = next(r['res_kev'] for r in CALIB.values() if r['det'] == det)
        for run, meta in runs.items():
            parsed = parse_set(meta['set'])
            if parsed is None:
                continue
            chain, kind, k, imin = parsed
            if kind != want_kind:
                continue
            if want_kind == 'real' and truth['chains'][chain] not in ('pos', 'head'):
                continue
            info = manifest.get((det, meta['set']), {})
            decoy_e = {round(l['e'], 2) for l in info.get('lines', []) if l.get('decoy')}
            for p in peaks.get(run, []):
                if p['origin'] != 'Library' or p['anchor'] == '1':
                    continue
                z = float(p['peak_snr'])
                e = float(p['nuclide_energy'] or p['energy'])
                if want_kind == 'decoy' and round(e, 2) not in decoy_e:
                    continue
                bucket[(det, k, imin)].append(z)
    return bucket


def main():
    pos = collect('out_sets', 'sets_manifest.json', 'real')
    neg = collect('out_decoy', 'sets_manifest_decoy.json', 'decoy')

    print('=' * 96)
    print('Fisher z of accepted library lines: real chain present vs decoy (phantom) lines')
    print('=' * 96)
    for det in sorted(set(d for d, _, _ in pos)):
        p = np.array([z for (d, k, i), zs in pos.items() if d == det for z in zs])
        n = np.array([z for (d, k, i), zs in neg.items() if d == det for z in zs])
        if p.size == 0 or n.size == 0:
            continue
        print('%-8s real n=%-6d  median z=%6.1f  q25=%6.1f  q75=%7.1f' % (
            det, p.size, np.median(p), np.percentile(p, 25), np.percentile(p, 75)))
        print('%-8s phan n=%-6d  median z=%6.1f  q25=%6.1f  q75=%7.1f' % (
            '', n.size, np.median(n), np.percentile(n, 25), np.percentile(n, 75)))
        print('   z threshold ->   ' + ''.join('%8.0f' % t for t in THRESH))
        print('   real kept   %%    ' + ''.join('%8.1f' % (100 * (p >= t).mean()) for t in THRESH))
        print('   phantom kept%%    ' + ''.join('%8.1f' % (100 * (n >= t).mean()) for t in THRESH))
        print('   phantom/run      ' + ''.join('%8.2f' % ((n >= t).sum() / RUNS[det]) for t in THRESH))
        print()


THRESH = [4, 6, 8, 10, 15, 20, 30, 50, 100]
# number of (spectrum, chain, k, imin) decoy runs per detector, for the
# "phantom lines per run" row
RUNS = {}


def count_runs():
    for key, truth in TRUTH.items():
        det = truth['det']
        runs = runs_of('out_decoy', key)
        n = sum(1 for m in runs.values() if parse_set(m['set']) is not None)
        RUNS[det] = RUNS.get(det, 0) + n


if __name__ == '__main__':
    count_runs()
    main()
