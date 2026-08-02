# -*- coding: utf-8 -*-
"""Score the deconvolution sweep (no nuclide set): recall vs unexplained peaks.

recall       strong table lines (I_chain >= 1%) of the chains the sample really
             contains that some final peak sits on, on the positive spectra.
unexplained  RJMCMC extra peaks that no physics accounts for: further than
             0.6 FWHM from every table line of every chain present (down to
             0.05%), from K-40 / annihilation / the W K X-rays of the electrode,
             from the single and double escape peaks of the strong high-energy
             lines, and from the backscatter band. These are the deconvolution's
             own false positives.
"""
import os
import csv
import json
import numpy as np
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
from analyze import TRUTH, CALIB, DET_RANGE, U238_HEAD, fwhm_kev, sample_structure
from chains import chain_lines, CHAINS

I_REF = 1.0


def load(subdir, key, what):
    path = os.path.join(HERE, subdir, '%s_%s.csv' % (key, what))
    if not os.path.exists(path):
        return {} if what.endswith('runs') else defaultdict(list)
    if what.endswith('runs'):
        with open(path, newline='') as fh:
            return {int(r['run']): r for r in csv.DictReader(fh)}
    out = defaultdict(list)
    with open(path, newline='') as fh:
        for r in csv.DictReader(fh):
            out[int(r['run'])].append(r)
    return out


def targets(key):
    truth = TRUTH[key]
    lo, hi = DET_RANGE[truth['det']]
    out = []
    for chain, mode in truth['chains'].items():
        if mode not in ('pos', 'head'):
            continue
        for r in chain_lines(CHAINS[chain]):
            if not (lo <= r['energy'] <= hi) or r['i_chain'] < I_REF:
                continue
            if mode == 'head' and r['nucid'] not in U238_HEAD:
                continue
            out.append(r)
    res = next(x['res_kev'] for x in CALIB.values() if x['det'] == truth['det'])
    merged = []
    for r in sorted(out, key=lambda r: r['energy']):
        if merged and abs(r['energy'] - merged[-1]['energy']) < 0.6 * fwhm_kev(res, r['energy']):
            if r['i_chain'] > merged[-1]['i_chain']:
                merged[-1] = r
            continue
        merged.append(r)
    return merged, res


def strict_structure(key):
    """Only what a scintillator can actually show: chain lines at 1% and up,
    the ambient K-40 and annihilation peaks, the W K X-rays, the escape peaks of
    the strong high-energy lines, and the backscatter band. An extra peak away
    from all of this has nothing to explain it."""
    truth = TRUTH[key]
    lo, hi = DET_RANGE[truth['det']]
    out = [1460.82, 511.0, 57.98, 59.32, 67.24, 69.1]
    for chain, mode in truth['chains'].items():
        floor = 1.0 if mode in ('pos', 'head', 'unk') else 3.0
        for r in chain_lines(CHAINS[chain]):
            if r['i_chain'] >= floor:
                out.append(r['energy'])
    for strong in (2614.51, 2204.10, 1764.49):
        out.extend([strong - 511.0, strong - 1022.0])
    out.extend([190.0, 210.0, 230.0, 250.0])
    return np.array(sorted(e for e in out if lo <= e <= hi))


def main(verbose=True):
    chosen = {}
    if verbose:
        print('%-9s %4s %4s %5s | %8s %8s %9s %9s %9s %7s' % (
            'det', 'snr', 'roi', 'extra', 'recall%', 'gain pp', 'extra pk',
            'unexpl', 'unexpl-s', 'ms'))
        print('-' * 94)
    agg = defaultdict(lambda: dict(hit=0, refs=0, base=0, extra=0, unexpl=0,
                                   strict=0, ms=0, n=0))
    for key, truth in TRUTH.items():
        det = truth['det']
        tgt, res = targets(key)
        structure = np.array(sample_structure(key))
        strict = strict_structure(key)
        base_runs = load('out_deconv', key, 'base_runs')
        base_peaks = load('out_deconv', key, 'base_peaks')
        runs = load('out_deconv', key, 'runs')
        peaks = load('out_deconv', key, 'peaks')
        if not runs:
            continue

        base_hit = {}
        for run, meta in base_runs.items():
            e = np.array([float(p['energy']) for p in base_peaks.get(run, [])])
            hit = sum(1 for r in tgt
                      if e.size and np.min(np.abs(e - r['energy'])) <= max(0.5 * fwhm_kev(res, r['energy']), 3.0))
            base_hit[float(meta['snr'])] = hit

        for run, meta in runs.items():
            plist = peaks.get(run, [])
            e = np.array([float(p['energy']) for p in plist])
            hit = sum(1 for r in tgt
                      if e.size and np.min(np.abs(e - r['energy'])) <= max(0.5 * fwhm_kev(res, r['energy']), 3.0))
            extras = [p for p in plist if p['origin'] == 'RJMCMC']
            unexpl = strict_n = 0
            for p in extras:
                pe = float(p['energy'])
                tol = 0.6 * fwhm_kev(res, pe)
                if structure.size == 0 or np.min(np.abs(structure - pe)) > tol:
                    unexpl += 1
                if strict.size == 0 or np.min(np.abs(strict - pe)) > tol:
                    strict_n += 1
            k = (det, float(meta['snr']), float(meta['roi']), int(meta['extra']))
            a = agg[k]
            if tgt:
                a['hit'] += hit
                a['refs'] += len(tgt)
                a['base'] += base_hit.get(float(meta['snr']), 0)
            a['extra'] += len(extras)
            a['unexpl'] += unexpl
            a['strict'] += strict_n
            a['ms'] += int(meta['ms'])
            a['n'] += 1

    for det in ('ASN16', 'AS80x80', 'RC103'):
        rows = [(k, v) for k, v in agg.items() if k[0] == det and v['refs']]
        if not rows:
            continue
        rows.sort(key=lambda kv: (kv[0][1], kv[0][2], kv[0][3]))
        best = None
        for k, v in rows:
            rec = 100.0 * v['hit'] / v['refs']
            base = 100.0 * v['base'] / v['refs']
            if verbose:
                print('%-9s %4.0f %4.1f %5d | %8.1f %8.1f %9.1f %9.2f %9.2f %7.0f' % (
                    k[0], k[1], k[2], k[3], rec, rec - base,
                    v['extra'] / v['n'], v['unexpl'] / v['n'], v['strict'] / v['n'],
                    v['ms'] / v['n']))
            score = (rec - base) - 2.0 * (v['strict'] / v['n'])
            if best is None or score > best[0]:
                best = (score, k, rec - base, v['strict'] / v['n'], v['ms'] / v['n'],
                        v['extra'] / v['n'])
        print('  --> best for %-8s snr=%.0f roi=%.1f extra=%d | gain %+.1f pp, %.1f extras of which '
              '%.2f unexplained, %.0f ms' %
              (det + ':', best[1][1], best[1][2], best[1][3], best[2], best[5], best[3], best[4]))
        chosen[det] = dict(snr=best[1][1], roi=best[1][2], extra=best[1][3])
        print()
    with open(os.path.join(HERE, 'deconv_best.json'), 'w') as fh:
        json.dump(chosen, fh, indent=2)


if __name__ == '__main__':
    import sys
    main(verbose='--quiet' not in sys.argv)
