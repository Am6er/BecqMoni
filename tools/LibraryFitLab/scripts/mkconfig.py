# -*- coding: utf-8 -*-
"""Generate one NuclideDefinition.xml per detector holding every filter variant
of every chain as a separate NuclideSet.

The two filters under test are applied here, at set-construction time, so no
production code has to be touched to sweep them:

  k    minimum spacing between two lines of the set, in units of the detector's
       FWHM at that energy. Lines are taken strongest-first and a line is kept
       only if it is at least k*FWHM away from every already kept line. k = 0
       keeps the full chain.
  imin minimum intensity, in per cent per decay of the CHAIN PARENT (so the
       branching of e.g. Bi-212 -> Tl-208 is already folded in).

The anchor line is always kept regardless of both filters: without it the fit
never triggers.
"""
import os
import json
import uuid
import numpy as np
from chains import chain_lines, CHAINS, ANCHORS

HERE = os.path.dirname(os.path.abspath(__file__))

# Detector resolution models fitted by calibrate.py: FWHM[keV] = sqrt(a+b*E+c*E^2)
with open(os.path.join(HERE, 'calibration.json')) as fh:
    CALIB = {row['key']: row for row in json.load(fh)}

DETECTORS = {}
for row in CALIB.values():
    DETECTORS.setdefault(row['det'], row['res_kev'])

# Energy window actually covered by each detector (keV). Below/above it a line
# cannot be seen at all and would only pad the counts.
DET_RANGE = {'ASN16': (40.0, 3000.0), 'AS80x80': (40.0, 2900.0), 'RC103': (40.0, 2800.0)}

# Модели разрешения корпуса. Раньше здесь были только три детектора девятки —
# всё, что лежало в data/calibration.json, — и построить сет для германия или
# CZT было не из чего. corpus/detectors.csv пишет build_corpus.py: строка на
# группу, модель FWHM(E) и рабочий диапазон. Числа девятки в нём те же самые,
# так что старые сеты не меняются.
_DETECTORS_CSV = os.path.join(os.path.dirname(HERE), 'corpus', 'detectors.csv')
if os.path.exists(_DETECTORS_CSV):
    import csv as _csv
    with open(_DETECTORS_CSV, encoding='utf-8-sig', newline='') as _fh:
        for _row in _csv.DictReader(_fh):
            DETECTORS[_row['det']] = [float(_row['res_c0']), float(_row['res_c1']),
                                      float(_row['res_c2'])]
            DET_RANGE[_row['det']] = (float(_row['e_lo']), float(_row['e_hi']))

K_GRID = [0.0, 0.3, 0.5, 0.7, 0.85, 1.0, 1.3, 1.6, 2.0]
I_GRID = [0.0, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0]


def fwhm_kev(res, e):
    v = res[0] + res[1] * e + res[2] * e * e
    return float(np.sqrt(max(v, 1e-6)))


def filter_lines(lines, res, anchors, k, imin, e_lo, e_hi):
    """Greedy strongest-first selection under the two filters."""
    pool = [r for r in lines if e_lo <= r['energy'] <= e_hi]
    anchor_rows = [r for r in pool if any(abs(r['energy'] - a) < 1.5 for a in anchors)]
    kept = list(anchor_rows)
    for r in sorted(pool, key=lambda r: -r['i_chain']):
        if r in kept:
            continue
        if r['i_chain'] < imin:
            continue
        if k > 0.0:
            w = k * fwhm_kev(res, r['energy'])
            if any(abs(r['energy'] - q['energy']) < w for q in kept):
                continue
        kept.append(r)
    kept.sort(key=lambda r: r['energy'])
    return kept, set(id(r) for r in anchor_rows)


def esc(text):
    return (text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;'))


# Everything a real photopeak can sit on: the gamma lines of the four chains
# above a detectability floor, the ambient K-40 and annihilation peaks, the W K
# X-rays of the WT-20 electrode, and the single/double escape peaks of the two
# strongest high-energy lines. Backscatter lives around 200-250 keV.
def real_structure(chain_cache, floor=0.1):
    energies = [1460.82, 511.0, 57.98, 59.32, 67.24, 69.1]
    for lines in chain_cache.values():
        energies.extend(r['energy'] for r in lines if r['i_chain'] >= floor)
    for strong in (2614.51, 1764.49, 2204.10, 1620.5):
        energies.extend([strong - 511.0, strong - 1022.0])
    energies.extend([200.0, 220.0, 240.0, 260.0])          # backscatter band
    return sorted(energies)


def make_decoy(lines, res, anchors, avoid, structure, e_lo, e_hi):
    """A chain whose anchor is real but whose other lines are displaced onto
    energies where nothing is emitted.

    This is the null model the study rests on. Judging false positives by "this
    sample should not contain radium" is soft: the room always has some radon,
    so a weak but genuine line gets scored as an error. In a decoy set the
    anchor still fires the fit, but every other component sits somewhere the
    sample cannot emit, so a line reported there is false by construction.

    The chains are dense enough that a displaced line cannot always be kept away
    from real structure, so placement uses a loose guard and each decoy line is
    additionally flagged `clean` when its final position is far from anything
    real. Only clean lines are counted as false positives; keeping the unclean
    ones in the set preserves the line density the k filter has to act on.
    """
    out = []
    for idx, r in enumerate(lines):
        if any(abs(r['energy'] - a) < 1.5 for a in anchors):
            out.append(dict(r, name=r['name'].replace('(', '[decoy] ('),
                            decoy=False, clean=False))
            continue
        w = fwhm_kev(res, r['energy'])
        # deterministic pseudo-random displacement of 2..4 FWHM, alternating side
        step = (2.0 + 1.9 * (((idx * 7919) % 1000) / 1000.0)) * w
        best = None
        for sign in ((1, -1) if idx % 2 == 0 else (-1, 1)):
            for grow in (1.0, 1.35, 1.7, 2.2, 2.8):
                e = r['energy'] + sign * step * grow
                if e < e_lo or e > e_hi:
                    continue
                gw = fwhm_kev(res, e)
                if any(abs(e - q['energy']) < 0.6 * gw for q in out):
                    continue
                margin = min((abs(e - f) / gw for f in avoid), default=9.9)
                if best is None or margin > best[0]:
                    best = (margin, e, gw)
                if margin >= 1.0:
                    break
            if best is not None and best[0] >= 1.0:
                break
        if best is None:
            continue
        margin, e, gw = best
        clean = margin >= 1.0 and all(abs(e - f) >= 1.2 * gw for f in structure)
        out.append(dict(r, energy=e, name=r['name'].replace('(', '[decoy] ('),
                        decoy=True, clean=clean))
    out.sort(key=lambda r: r['energy'])
    return out


def build(det, path, kinds=('real', 'decoy')):
    res = DETECTORS[det]
    e_lo, e_hi = DET_RANGE[det]
    chain_cache = {name: chain_lines(root) for name, root in CHAINS.items()}

    avoid = real_structure(chain_cache, floor=0.3)
    structure = real_structure(chain_cache, floor=0.05)
    variants = []
    if 'real' in kinds:
        variants.append(('real', chain_cache))
    if 'decoy' in kinds:
        variants.append(('decoy', {name: make_decoy(lines, res, ANCHORS[name], avoid,
                                                    structure, e_lo, e_hi)
                                   for name, lines in chain_cache.items()}))

    nuclides = []           # (name, energy, halflife, intensity, set id, is_anchor)
    sets = []               # (id, name)
    manifest = []
    for kind, cache in variants:
        for chain, lines in cache.items():
            anchors = ANCHORS[chain]
            for k in K_GRID:
                for imin in I_GRID:
                    set_id = str(uuid.uuid4())
                    set_name = '%s%s|k%.2f|i%.2f' % (
                        chain, '' if kind == 'real' else '~decoy', k, imin)
                    sets.append((set_id, set_name))
                    kept, anchor_ids = filter_lines(lines, res, anchors, k, imin, e_lo, e_hi)
                    for r in kept:
                        nuclides.append((r['name'], r['energy'], r['half_life_y'],
                                         r['i_chain'], set_id, id(r) in anchor_ids,
                                         r['nucid']))
                    manifest.append(dict(det=det, chain=chain, kind=kind, k=k, imin=imin,
                                         set_name=set_name, set_id=set_id,
                                         n_lines=len(kept),
                                         n_decoy=sum(1 for r in kept if r.get('decoy')),
                                         n_clean=sum(1 for r in kept if r.get('clean')),
                                         lines=[dict(e=r['energy'], i=r['i_chain'],
                                                     nucid=r['nucid'], name=r['name'],
                                                     decoy=bool(r.get('decoy')),
                                                     clean=bool(r.get('clean')))
                                                for r in kept]))

    # merge identical (name, energy) rows so the file is not 100k entries: one
    # NuclideDefinition can belong to many sets at once
    merged = {}
    for name, energy, hl, inten, set_id, is_anchor, nucid in nuclides:
        key = (name, round(energy, 4))
        row = merged.setdefault(key, dict(name=name, energy=energy, hl=hl,
                                          inten=inten, sets=[], anchor=False,
                                          nucid=nucid))
        row['sets'].append(set_id)
        row['anchor'] = row['anchor'] or is_anchor

    out = ['<?xml version="1.0"?>',
           '<NuclideDefinitionFile xmlns:xsd="http://www.w3.org/2001/XMLSchema" '
           'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">',
           '  <NuclideDefinitions>']
    for row in sorted(merged.values(), key=lambda r: r['energy']):
        out.append('    <Nuclide>')
        out.append('      <Name>%s</Name>' % esc(row['name']))
        out.append('      <Energy>%s</Energy>' % repr(row['energy']))
        out.append('      <HalfLife>%s</HalfLife>' % repr(max(row['hl'], 1e-12)))
        out.append('      <NuclideColor>Green</NuclideColor>')
        out.append('      <Note />')
        out.append('      <Visible>true</Visible>')
        out.append('      <Intencity>%s</Intencity>' % repr(row['inten']))
        out.append('      <Sets>')
        for s in row['sets']:
            out.append('        <guid>%s</guid>' % s)
        out.append('      </Sets>')
        out.append('      <IsAnchor>%s</IsAnchor>' % ('true' if row['anchor'] else 'false'))
        out.append('    </Nuclide>')
    out.append('  </NuclideDefinitions>')
    out.append('  <NuclideSets>')
    for set_id, set_name in sets:
        out.append('    <NuclideSet>')
        out.append('      <Id>%s</Id>' % set_id)
        out.append('      <Name>%s</Name>' % esc(set_name))
        out.append('      <HideUnknownPeaks>false</HideUnknownPeaks>')
        out.append('    </NuclideSet>')
    out.append('  </NuclideSets>')
    out.append('</NuclideDefinitionFile>')

    with open(path, 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(out))
    return manifest, len(merged), len(sets)


if __name__ == '__main__':
    import sys
    kinds = tuple(a.split('=')[1].split(',') for a in sys.argv[1:] if a.startswith('--kind='))
    kinds = kinds[0] if kinds else ('real', 'decoy')
    suffix = ([a.split('=')[1] for a in sys.argv[1:] if a.startswith('--suffix=')] + [''])[0]

    # Полная сетка k x I_min — это 9 x 7 сетов на цепочку; для сравнения гейтов
    # нужна одна рекомендованная точка, зато на восемнадцати группах.
    for arg in sys.argv[1:]:
        if arg.startswith('--k='):
            K_GRID[:] = [float(x) for x in arg.split('=', 1)[1].split(',')]
        if arg.startswith('--imin='):
            I_GRID[:] = [float(x) for x in arg.split('=', 1)[1].split(',')]
    dets = ([a.split('=', 1)[1].split(',') for a in sys.argv[1:]
             if a.startswith('--dets=')] + [sorted(DETECTORS)])[0]

    all_manifest = []
    for det in dets:
        wd = os.path.join(HERE, 'wd_%s%s' % (det, suffix))
        os.makedirs(os.path.join(wd, 'config'), exist_ok=True)
        path = os.path.join(wd, 'config', 'NuclideDefinition.xml')
        manifest, n_nuc, n_sets = build(det, path, kinds)
        all_manifest.extend(manifest)
        print('%-8s %4d sets, %4d nuclide rows -> %s' % (det, n_sets, n_nuc, path))
        for chain in CHAINS:
            rows = [m for m in manifest if m['chain'] == chain and m['kind'] == kinds[0]]
            grid = {}
            for m in rows:
                grid[(m['k'], m['imin'])] = m['n_lines']
            print('   %-7s lines(k,imin):' % chain)
            print('        imin: ' + ' '.join('%6.2f' % i for i in I_GRID))
            for k in K_GRID:
                print('     k=%4.2f: ' % k + ' '.join('%6d' % grid[(k, i)] for i in I_GRID))
    with open(os.path.join(HERE, 'sets_manifest%s.json' % suffix), 'w') as fh:
        json.dump(all_manifest, fh)
