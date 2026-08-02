# -*- coding: utf-8 -*-
"""The recommended sets themselves: k = 0.7 FWHM, I_min = 1% per chain-parent decay."""
import os
import csv
import numpy as np
from chains import chain_lines, CHAINS, ANCHORS
from mkconfig import filter_lines, DETECTORS, DET_RANGE, fwhm_kev

HERE = os.path.dirname(os.path.abspath(__file__))
DEST = os.path.join(HERE, 'export')
K, IMIN = 0.7, 1.0


def main():
    os.makedirs(DEST, exist_ok=True)
    rows = []
    for det in ('ASN16', 'AS80x80', 'RC103'):
        res = DETECTORS[det]
        lo, hi = DET_RANGE[det]
        for chain, root in CHAINS.items():
            lines = chain_lines(root)
            kept, anchor_ids = filter_lines(lines, res, ANCHORS[chain], K, IMIN, lo, hi)
            print('== %-8s %-8s %d lines' % (det, chain, len(kept)))
            for r in kept:
                is_anchor = id(r) in anchor_ids
                # intensity as NucBase would import it (per decay of that nuclide)
                rows.append([det, chain, '%.2f' % r['energy'], '%.4f' % r['i_chain'],
                             '%.4f' % r['i_nuc'], '%.4f' % r['branch'], r['name'],
                             '%.1f' % fwhm_kev(res, r['energy']),
                             'anchor' if is_anchor else ''])
                if det == 'ASN16':
                    print('   %8.2f keV  I_chain=%7.3f%%  I_nuc=%7.3f%%  %-22s %s' % (
                        r['energy'], r['i_chain'], r['i_nuc'], r['name'],
                        'ANCHOR' if is_anchor else ''))
    path = os.path.join(DEST, 'recommended_sets.csv')
    with open(path, 'w', newline='', encoding='utf-8') as fh:
        w = csv.writer(fh)
        w.writerow(['detector', 'chain', 'energy_keV', 'intensity_pct_per_chain_decay',
                    'intensity_pct_per_nuclide_decay', 'chain_branching', 'nuclide',
                    'fwhm_keV', 'role'])
        w.writerows(rows)
    print('\n-> %s (%d rows)' % (path, len(rows)))


if __name__ == '__main__':
    main()
