# -*- coding: utf-8 -*-
"""Are the decoy lines the fitter accepts really phantoms?

At each accepted position the net area of a Gaussian of the KNOWN model width is
measured twice, over a linear and over a quadratic local background fitted on
the flanks only. Amplitude is linear in both, so unlike a free fit it always
returns a number, with an honest Poisson error bar.

A genuine photopeak is positive under both background models: it is a real
excess, not an artefact of how the continuum is drawn. Structure that is only
continuum curvature flips sign between the two.

Real strong chain lines of the same spectra serve as the positive control, so
the test is calibrated rather than asserted.
"""
import os
import csv
import json
import numpy as np
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
from spectrum import Spectrum
from gaussfit import fit_peak, FWHM_SIGMA
from analyze import TRUTH, CALIB, parse_set
from chains import chain_lines, CHAINS


def net_area(counts, ch0, sigma0, order, window=2.6):
    n = len(counts)
    half = int(round(window * sigma0))
    lo, hi = max(0, int(ch0) - half), min(n - 1, int(ch0) + half)
    if hi - lo < 8:
        return None
    x = np.arange(lo, hi + 1, dtype=float)
    y = counts[lo:hi + 1]
    g = np.exp(-0.5 * ((x - ch0) / sigma0) ** 2)
    flank = np.abs(x - ch0) > 1.5 * sigma0
    if flank.sum() < order + 3:
        return None
    base = np.polyval(np.polyfit(x[flank], y[flank], order), x)
    gg = max((g * g).sum(), 1e-9)
    amp = float(((y - base) * g).sum() / gg)
    var = float(((y + np.maximum(base, 0.0)) * g * g).sum()) / gg ** 2
    err = np.sqrt(max(var, 1e-9))
    return amp / err if err > 0 else 0.0


def robust(counts, ch0, sigma0):
    """(sigma under linear bg, sigma under quadratic bg, verdict)"""
    a = net_area(counts, ch0, sigma0, 1)
    b = net_area(counts, ch0, sigma0, 2)
    if a is None or b is None:
        return a, b, None
    return a, b, (a >= 4.0 and b >= 4.0)


def sigma_for(sp, res, e):
    ch = sp.channel(e)
    fw = float(np.sqrt(max(res[0] + res[1] * e + res[2] * e * e, 1e-6)))
    return ch, max(fw / max(sp.dEdch(ch), 1e-9) / FWHM_SIGMA, 1.0)


def phantoms(key, det, k=0.85, imin=0.2):
    manifest = {(m['det'], m['set_name']): m for m in
                json.load(open(os.path.join(HERE, 'sets_manifest_decoy.json')))}
    runs_path = os.path.join(HERE, 'out_decoy', '%s_runs.csv' % key)
    peaks_path = os.path.join(HERE, 'out_decoy', '%s_peaks.csv' % key)
    if not os.path.exists(runs_path):
        return []
    with open(runs_path, newline='') as fh:
        runs = {int(r['run']): r for r in csv.DictReader(fh)}
    peaks = defaultdict(list)
    with open(peaks_path, newline='') as fh:
        for r in csv.DictReader(fh):
            peaks[int(r['run'])].append(r)
    out = []
    for run, meta in runs.items():
        parsed = parse_set(meta['set'])
        if parsed is None:
            continue
        chain, kind, kk, ii = parsed
        if abs(kk - k) > 1e-9 or abs(ii - imin) > 1e-9:
            continue
        info = manifest.get((det, meta['set']), {})
        decoy_e = sorted(l['e'] for l in info.get('lines', []) if l.get('decoy'))
        for p in peaks.get(run, []):
            if p['origin'] != 'Library' or p['anchor'] == '1':
                continue
            e = float(p['nuclide_energy'] or p['energy'])
            if decoy_e and min(abs(e - d) for d in decoy_e) < 0.01:
                out.append((chain, e, float(p['peak_snr'])))
    return out


def controls(key):
    """Strong lines of the chains the sample really contains."""
    truth = TRUTH[key]
    out = []
    for chain, mode in truth['chains'].items():
        if mode not in ('pos', 'head'):
            continue
        for r in chain_lines(CHAINS[chain]):
            if r['i_chain'] >= 3.0 and 60.0 <= r['energy'] <= 2800.0:
                out.append((chain, r['energy'], r['i_chain']))
    return out


def main():
    print('%-18s %-9s %6s %8s %8s %8s' % ('spectrum', 'class', 'n', 'both>4s', 'sign-flip', 'median|s|'))
    print('-' * 68)
    for key in ('ASN16_Th232', 'ASN16_Charoite', 'ASN16_UGlass', 'AS80_Th232WT20',
                'RC103_Th232WT20'):
        det = TRUTH[key]['det']
        res = next(r['res_kev'] for r in CALIB.values() if r['det'] == det)
        path = os.path.join(HERE, 'spectra', '%s.xml' % key)
        if not os.path.exists(path):
            continue
        sp = Spectrum(path)
        for label, items in (('control', [(c, e) for c, e, _ in controls(key)]),
                             ('phantom', [(c, e) for c, e, _ in phantoms(key, det)])):
            seen, good, flip, mags = set(), 0, 0, []
            for chain, e in items:
                if round(e, 1) in seen:
                    continue
                seen.add(round(e, 1))
                ch, sigma0 = sigma_for(sp, res, e)
                a, b, verdict = robust(sp.counts, ch, sigma0)
                if verdict is None:
                    continue
                good += 1 if verdict else 0
                flip += 1 if (a > 0) != (b > 0) else 0
                mags.append(min(abs(a), abs(b)))
            n = len(mags)
            if n == 0:
                continue
            print('%-18s %-9s %6d %7.0f%% %8.0f%% %8.1f' % (
                key, label, n, 100.0 * good / n, 100.0 * flip / n, np.median(mags)))
    print()
    print('control = table lines with I_chain >= 3% of a chain the sample really contains')
    print('phantom = decoy lines the library fit accepted (k=0.85, imin=0.20)')
    print('both>4s = net area positive at >=4 sigma under BOTH background models')


if __name__ == '__main__':
    main()

