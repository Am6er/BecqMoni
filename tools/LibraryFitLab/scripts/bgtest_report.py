# -*- coding: utf-8 -*-
"""Same spectra, same decoys, background subtraction on vs off."""
import os
import csv
import json
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
from analyze import parse_set

manifest = {(m['det'], m['set_name']): m for m in
            json.load(open(os.path.join(HERE, 'sets_manifest_decoy.json')))}


def load(key, mode, what):
    path = os.path.join(HERE, 'out_bgtest', '%s_%s_%s.csv' % (key, mode, what))
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


print('Rates are conditioned on the anchor having matched: a run where the fit')
print('never triggered contributes nothing either way.')
print()
print('%-16s %-10s %7s %7s %8s %11s %9s %8s' % (
    'spectrum', 'background', 'runs', 'fired', 'finder', 'decoy lines',
    'phantoms', 'rate %'))
print('-' * 84)
for key in ('AS80_Th232_v2', 'AS80_UGlass'):
    for mode in ('visible', 'substract'):
        runs = load(key, mode, 'runs')
        peaks = load(key, mode, 'peaks')
        fp = decoy = finder = n = fired = 0
        finder_base = None
        for run, meta in runs.items():
            if meta['set'] == '-':
                finder_base = int(meta['n_finder'])
                continue
            n += 1
            if int(meta['n_anchor']) == 0:
                continue
            fired += 1
            info = manifest.get(('AS80x80', meta['set']), {})
            decoy += info.get('n_decoy', 0)
            finder += int(meta['n_finder'])
            fp += sum(1 for p in peaks.get(run, [])
                      if p['origin'] == 'Library' and p['anchor'] != '1')
        print('%-16s %-10s %7d %7d %8.1f %11d %9d %8.1f' % (
            key, 'off' if mode == 'visible' else 'ON', n, fired,
            finder / max(fired, 1), decoy, fp, 100.0 * fp / max(decoy, 1)))
    print('%-16s %-10s finder peaks with no set at all: %s' % ('', '', finder_base))
