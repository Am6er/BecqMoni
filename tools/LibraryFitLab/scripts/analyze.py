# -*- coding: utf-8 -*-
"""Score the (k, imin) sweep.

Two numbers per grid point:

  recall  fraction of the chain's physically strong lines (I_chain >= I_REF,
          inside the detector's range) that some final peak sits on, measured
          only on spectra where the chain is really present. The denominator is
          the same for every grid point - it comes from the unfiltered chain -
          so a set cannot win by simply declaring fewer lines.

  fp      library peaks accepted from the decoy set of the same chain with the
          same (k, imin). The decoy shares the real anchor but every other line
          is displaced onto an energy where nothing is emitted, so each of these
          is a false positive by construction.
"""
import os
import csv
import json
import numpy as np
from collections import defaultdict
from chains import chain_lines, CHAINS, ANCHORS

HERE = os.path.dirname(os.path.abspath(__file__))
I_REF = 1.0        # a line counts towards recall from this chain intensity (%)

with open(os.path.join(HERE, 'calibration.json')) as fh:
    CALIB = {row['key']: row for row in json.load(fh)}

DET_RANGE = {'ASN16': (40.0, 3000.0), 'AS80x80': (40.0, 2900.0), 'RC103': (40.0, 2800.0)}

# Head of the U-238 series: what survives in uranium glass, where the series is
# chemically broken at radium (the user states there is no Ra-226 in it).
U238_HEAD = {'238U', '234TH', '234PAm1', '234PA', '234U', '230TH'}

# chain presence per spectrum: pos = present, neg = absent, bg = only room
# background, unk = cannot be asserted
TRUTH = {
    'ASN16_Th232':     dict(det='ASN16',   chains={'Th-232': 'pos', 'Ra-226': 'bg', 'U-238': 'bg', 'U-235': 'bg'}),
    'ASN16_Charoite':  dict(det='ASN16',   chains={'Th-232': 'unk', 'Ra-226': 'pos', 'U-238': 'unk', 'U-235': 'unk'}),
    'ASN16_UGlass':    dict(det='ASN16',   chains={'Th-232': 'neg', 'Ra-226': 'neg', 'U-238': 'head', 'U-235': 'pos'}),
    'ASN16_Granite':   dict(det='ASN16',   chains={'Th-232': 'pos', 'Ra-226': 'pos', 'U-238': 'pos', 'U-235': 'pos'}),
    'AS80_Th232WT20':  dict(det='AS80x80', chains={'Th-232': 'pos', 'Ra-226': 'bg', 'U-238': 'bg', 'U-235': 'bg'}),
    'AS80_Th232_v2':   dict(det='AS80x80', chains={'Th-232': 'pos', 'Ra-226': 'bg', 'U-238': 'bg', 'U-235': 'bg'}),
    'AS80_UGlass':     dict(det='AS80x80', chains={'Th-232': 'neg', 'Ra-226': 'neg', 'U-238': 'head', 'U-235': 'pos'}),
    'AS80_Charoite':   dict(det='AS80x80', chains={'Th-232': 'unk', 'Ra-226': 'pos', 'U-238': 'unk', 'U-235': 'unk'}),
    'RC103_Th232WT20': dict(det='RC103',   chains={'Th-232': 'pos', 'Ra-226': 'bg', 'U-238': 'bg', 'U-235': 'bg'}),
}


def fwhm_kev(res, e):
    return float(np.sqrt(max(res[0] + res[1] * e + res[2] * e * e, 1e-6)))


def reference_lines(chain, det, mode):
    lo, hi = DET_RANGE[det]
    rows = [r for r in chain_lines(CHAINS[chain])
            if lo <= r['energy'] <= hi and r['i_chain'] >= I_REF]
    if mode == 'head':
        rows = [r for r in rows if r['nucid'] in U238_HEAD]
    # two table lines the detector cannot separate count as one target
    res = None
    for row in CALIB.values():
        if row['det'] == det:
            res = row['res_kev']
            break
    merged = []
    for r in sorted(rows, key=lambda r: r['energy']):
        if merged and abs(r['energy'] - merged[-1]['energy']) < 0.6 * fwhm_kev(res, r['energy']):
            if r['i_chain'] > merged[-1]['i_chain']:
                merged[-1] = r
            continue
        merged.append(r)
    return merged, res


def sample_structure(key):
    """Energies where the given sample really can produce a photopeak.

    A decoy line that lands here is not a usable false-positive witness. Chains
    that are present contribute all their lines down to 0.05%; chains that are
    absent still contribute their strong lines, because the room background has
    some radon and thorium in it whatever the sample is.
    """
    truth = TRUTH[key]
    det = truth['det']
    lo, hi = DET_RANGE[det]
    out = [1460.82, 511.0, 57.98, 59.32, 67.24, 69.1]
    for chain, mode in truth['chains'].items():
        floor = 0.05 if mode in ('pos', 'head', 'unk') else 2.0
        rows = chain_lines(CHAINS[chain])
        if mode == 'head':
            # the rest of the series is chemically absent, but keep its strong
            # lines out of the witness list anyway
            out.extend(r['energy'] for r in rows
                       if r['i_chain'] >= (0.05 if r['nucid'] in U238_HEAD else 2.0))
            continue
        out.extend(r['energy'] for r in rows if r['i_chain'] >= floor)
    for strong in (2614.51, 2204.10, 1764.49, 1620.5):
        out.extend([strong - 511.0, strong - 1022.0])
    out.extend([200.0, 220.0, 240.0, 260.0])              # backscatter band
    return sorted(e for e in out if lo <= e <= hi)


def load_runs(key):
    path = os.path.join(HERE, OUT_SUBDIR, '%s_runs.csv' % key)
    if not os.path.exists(path):
        return {}
    out = {}
    with open(path, newline='') as fh:
        for row in csv.DictReader(fh):
            out[int(row['run'])] = row
    return out


def load_peaks(key):
    path = os.path.join(HERE, OUT_SUBDIR, '%s_peaks.csv' % key)
    peaks = defaultdict(list)
    if not os.path.exists(path):
        return peaks
    with open(path, newline='') as fh:
        for row in csv.DictReader(fh):
            peaks[int(row['run'])].append(row)
    return peaks


def parse_set(name):
    """'Th-232~decoy|k0.85|i0.20' -> ('Th-232', 'decoy', 0.85, 0.20)"""
    if name == '-':
        return None
    head, kpart, ipart = name.split('|')
    kind = 'decoy' if '~decoy' in head else 'real'
    chain = head.replace('~decoy', '')
    return chain, kind, float(kpart[1:]), float(ipart[1:])


def main(subdir='out_sets', manifest_name='sets_manifest.json', out='scored.json'):
    global OUT_SUBDIR
    OUT_SUBDIR = subdir
    manifest = json.load(open(os.path.join(HERE, manifest_name)))
    by_name = {(m['det'], m['set_name']): m for m in manifest}

    rows = []
    for key, truth in TRUTH.items():
        det = truth['det']
        runs = load_runs(key)
        if not runs:
            print('!! no data for %s' % key)
            continue
        peaks = load_peaks(key)
        structure = np.array(sample_structure(key))
        res = next(r['res_kev'] for r in CALIB.values() if r['det'] == det)

        baseline = {}
        for run, meta in runs.items():
            if meta['set'] == '-':
                baseline[float(meta['snr'])] = (run, meta)

        for run, meta in runs.items():
            parsed = parse_set(meta['set'])
            if parsed is None:
                continue
            chain, kind, k, imin = parsed
            mode = truth['chains'][chain]
            snr = float(meta['snr'])
            plist = peaks.get(run, [])
            lib = [p for p in plist if p['origin'] == 'Library']
            n_anchor = int(meta['n_anchor'])

            base_run, base_meta = baseline.get(snr, (None, None))
            finder_lost = (int(base_meta['n_finder']) - int(meta['n_finder'])) if base_meta else 0

            rec = None
            if kind == 'real' and mode in ('pos', 'head'):
                refs, res = reference_lines(chain, det, mode)
                energies = np.array([float(p['energy']) for p in plist])
                hit = 0
                for r in refs:
                    tol = max(0.5 * fwhm_kev(res, r['energy']), 3.0)
                    if energies.size and np.min(np.abs(energies - r['energy'])) <= tol:
                        hit += 1
                rec = (hit, len(refs))

            base_rec = None
            if rec is not None and base_run is not None:
                refs, res = reference_lines(chain, det, mode)
                be = np.array([float(p['energy']) for p in peaks.get(base_run, [])])
                hit = 0
                for r in refs:
                    tol = max(0.5 * fwhm_kev(res, r['energy']), 3.0)
                    if be.size and np.min(np.abs(be - r['energy'])) <= tol:
                        hit += 1
                base_rec = (hit, len(refs))

            info = by_name.get((det, meta['set']), {})
            fp_all = fp_clean = n_witness = None
            if kind == 'decoy':
                decoys = [l for l in info.get('lines', []) if l.get('decoy')]
                witness = [l for l in decoys
                           if structure.size == 0 or
                           np.min(np.abs(structure - l['e'])) >= 1.2 * fwhm_kev(res, l['e'])]
                n_witness = len(witness)
                wset = np.array([l['e'] for l in witness]) if witness else np.array([])
                fp_all = 0
                fp_clean = 0
                for p in lib:
                    if p['anchor'] == '1':
                        continue
                    fp_all += 1
                    e = float(p['nuclide_energy'] or p['energy'])
                    if wset.size and np.min(np.abs(wset - e)) < 0.6:
                        fp_clean += 1

            rows.append(dict(
                spectrum=key, det=det, chain=chain, kind=kind, k=k, imin=imin,
                mode=mode, snr=snr, set_lines=int(meta['set_lines']),
                n_total=int(meta['n_total']), n_finder=int(meta['n_finder']),
                n_lib=int(meta['n_library']), n_anchor=n_anchor,
                finder_lost=finder_lost, ms=int(meta['ms']),
                hit=rec[0] if rec else None, refs=rec[1] if rec else None,
                base_hit=base_rec[0] if base_rec else None,
                # in a decoy set every non-anchor library peak is false by
                # construction; fp_clean keeps only those whose position this
                # particular sample cannot populate at all
                fp=fp_all, fp_clean=fp_clean, n_witness=n_witness,
                n_decoy_lines=info.get('n_decoy', 0),
            ))

    with open(os.path.join(HERE, out), 'w') as fh:
        json.dump(rows, fh)
    print('scored %d runs over %d spectra -> %s' % (
        len(rows), len(set(r['spectrum'] for r in rows)), out))
    return rows


if __name__ == '__main__':
    import sys
    if len(sys.argv) > 1 and sys.argv[1] == 'decoy':
        main('out_decoy', 'sets_manifest_decoy.json', 'scored_decoy.json')
    else:
        main()
