# -*- coding: utf-8 -*-
"""Is the Fisher z blind to phantom lines, or is the "real" class contaminated?

zstudy.py found the z distribution of accepted real-set lines to be almost the
same as that of accepted decoy lines. Two explanations:

  (a) z genuinely cannot tell a peak from a bump on the continuum;
  (b) the positive class is polluted - a "real" set contains many lines that are
      far too weak to be seen, so most of its accepted lines are phantoms too.

This splits the positive class by chain intensity. If (b) were the whole story,
strong lines (I_chain >= 5%, which the detector certainly sees) would show a
clearly higher z than the decoys.

It also tests the physical criterion the fitter does not currently apply: within
one chain in secular equilibrium, amplitude/intensity must follow one smooth
efficiency curve, so a line whose z/I is far off the curve traced by the rest is
not that line.
"""
import os
import csv
import json
import numpy as np
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
from analyze import TRUTH, CALIB, parse_set


def load(subdir, key, what):
    path = os.path.join(HERE, subdir, '%s_%s.csv' % (key, what))
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


def gather(subdir, manifest_name, want_kind):
    manifest = {(m['det'], m['set_name']): m for m in
                json.load(open(os.path.join(HERE, manifest_name)))}
    items = []
    for key, truth in TRUTH.items():
        det = truth['det']
        runs = load(subdir, key, 'runs')
        peaks = load(subdir, key, 'peaks')
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
            by_energy = {round(l['e'], 2): l for l in info.get('lines', [])}
            group = []
            for p in peaks.get(run, []):
                if p['origin'] != 'Library':
                    continue
                e = float(p['nuclide_energy'] or p['energy'])
                line = by_energy.get(round(e, 2))
                if line is None:
                    continue
                if want_kind == 'decoy' and not line.get('decoy'):
                    continue
                group.append(dict(det=det, spectrum=key, chain=chain, k=k, imin=imin,
                                  run=run, e=e, i=line['i'], z=float(p['peak_snr']),
                                  anchor=p['anchor'] == '1'))
            if group:
                items.append(group)
    return items


def curve_residual(group):
    """log(z/I) against log E, straight-line fit, residual in sigma per line."""
    if len(group) < 4:
        return None
    e = np.log(np.array([g['e'] for g in group]))
    y = np.log(np.array([max(g['z'], 1e-6) / max(g['i'], 1e-9) for g in group]))
    a = np.vstack([np.ones_like(e), e]).T
    coef, *_ = np.linalg.lstsq(a, y, rcond=None)
    resid = y - a.dot(coef)
    scale = np.median(np.abs(resid - np.median(resid))) * 1.4826
    if scale <= 0:
        return None
    return np.abs(resid - np.median(resid)) / scale


def main():
    pos_groups = gather('out_sets', 'sets_manifest.json', 'real')
    neg_groups = gather('out_decoy', 'sets_manifest_decoy.json', 'decoy')

    print('=' * 100)
    print('A. z by chain intensity of the line (positives) vs decoys')
    print('=' * 100)
    bands = [(5.0, 1e9, 'I>=5%  (certainly visible)'),
             (1.0, 5.0, 'I 1-5%'),
             (0.2, 1.0, 'I 0.2-1%'),
             (0.0, 0.2, 'I<0.2% (barely emitted)')]
    for det in ('ASN16', 'AS80x80', 'RC103'):
        print('  %s' % det)
        neg = np.array([g['z'] for grp in neg_groups for g in grp
                        if g['det'] == det and not g['anchor']])
        for lo, hi, label in bands:
            zs = np.array([g['z'] for grp in pos_groups for g in grp
                           if g['det'] == det and not g['anchor'] and lo <= g['i'] < hi])
            if zs.size == 0:
                continue
            print('    real %-26s n=%-5d median z=%8.1f  q25=%8.1f' % (
                label, zs.size, np.median(zs), np.percentile(zs, 25)))
        if neg.size:
            print('    %-31s n=%-5d median z=%8.1f  q25=%8.1f' % (
                'DECOY (phantom)', neg.size, np.median(neg), np.percentile(neg, 25)))
        # decoys split the same way, to check the intensity bands are comparable
        for lo, hi, label in bands:
            zs = np.array([g['z'] for grp in neg_groups for g in grp
                           if g['det'] == det and not g['anchor'] and lo <= g['i'] < hi])
            if zs.size:
                print('      decoy %-24s n=%-5d median z=%8.1f' % (label, zs.size, np.median(zs)))
        print()

    print('=' * 100)
    print('B. deviation from the chain efficiency curve, |log(z/I) residual| in robust sigma')
    print('=' * 100)
    for det in ('ASN16', 'AS80x80', 'RC103'):
        for tag, groups in (('real ', pos_groups), ('decoy', neg_groups)):
            vals = []
            for grp in groups:
                if grp[0]['det'] != det:
                    continue
                r = curve_residual([g for g in grp if not g['anchor']])
                if r is not None:
                    vals.extend(r.tolist())
            if not vals:
                continue
            v = np.array(vals)
            print('  %-8s %s n=%-6d median=%5.2f  q75=%5.2f  frac>2s=%4.1f%%  frac>3s=%4.1f%%' % (
                det, tag, v.size, np.median(v), np.percentile(v, 75),
                100 * (v > 2).mean(), 100 * (v > 3).mean()))
        print()


if __name__ == '__main__':
    main()
