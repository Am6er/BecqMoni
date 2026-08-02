# -*- coding: utf-8 -*-
"""Export the sweep summaries as CSV for tools/LibraryFitLab/data/."""
import os
import csv
import json
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
DEST = os.path.join(HERE, 'export')
K_GRID = [0.0, 0.3, 0.5, 0.7, 0.85, 1.0, 1.3, 1.6, 2.0]
I_GRID = [0.0, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0]


def ensure():
    os.makedirs(DEST, exist_ok=True)


def write(name, header, rows):
    with open(os.path.join(DEST, name), 'w', newline='', encoding='utf-8') as fh:
        w = csv.writer(fh)
        w.writerow(header)
        w.writerows(rows)
    print('  %-34s %d rows' % (name, len(rows)))


def set_grid():
    real = [r for r in json.load(open(os.path.join(HERE, 'scored.json'))) if r['kind'] == 'real']
    decoy = [r for r in json.load(open(os.path.join(HERE, 'scored_decoy.json'))) if r['kind'] == 'decoy']
    rows = []
    for det in ('ASN16', 'AS80x80', 'RC103'):
        rr = [r for r in real if r['det'] == det]
        pos = [r for r in rr if r['hit'] is not None]
        dd = [r for r in decoy if r['det'] == det]
        for k in K_GRID:
            for i in I_GRID:
                s = [r for r in pos if r['k'] == k and r['imin'] == i]
                a = [r for r in rr if r['k'] == k and r['imin'] == i]
                d = [r for r in dd if r['k'] == k and r['imin'] == i]
                if not s or not d:
                    continue
                rows.append([
                    det, '%.2f' % k, '%.2f' % i,
                    '%.1f' % np.mean([r['set_lines'] for r in a]),
                    '%.1f' % (100.0 * sum(r['hit'] for r in s) / max(sum(r['refs'] for r in s), 1)),
                    '%.1f' % (100.0 * sum(r['base_hit'] for r in s) / max(sum(r['refs'] for r in s), 1)),
                    '%.2f' % np.mean([r['n_lib'] for r in a]),
                    '%.2f' % np.mean([r['finder_lost'] for r in a]),
                    '%.2f' % np.mean([r['fp'] or 0 for r in d]),
                    '%.1f' % (100.0 * sum(r['fp'] or 0 for r in d) /
                              max(sum(r['n_decoy_lines'] or 0 for r in d), 1)),
                    '%.0f' % np.mean([r['ms'] for r in a]),
                ])
    write('set_filter_grid.csv',
          ['detector', 'k_fwhm', 'i_min_pct', 'set_lines', 'recall_pct', 'finder_only_recall_pct',
           'library_peaks', 'finder_peaks_lost', 'phantom_lines', 'phantom_rate_pct', 'ms'], rows)


def per_spectrum():
    real = [r for r in json.load(open(os.path.join(HERE, 'scored.json'))) if r['kind'] == 'real']
    decoy = {(r['spectrum'], r['chain'], r['k'], r['imin']): r
             for r in json.load(open(os.path.join(HERE, 'scored_decoy.json')))
             if r['kind'] == 'decoy'}
    rows = []
    for r in real:
        d = decoy.get((r['spectrum'], r['chain'], r['k'], r['imin']))
        rows.append([r['spectrum'], r['det'], r['chain'], r['mode'],
                     '%.2f' % r['k'], '%.2f' % r['imin'], r['set_lines'],
                     r['n_total'], r['n_finder'], r['n_lib'], r['n_anchor'],
                     r['finder_lost'],
                     r['hit'] if r['hit'] is not None else '',
                     r['refs'] if r['refs'] is not None else '',
                     r['base_hit'] if r['base_hit'] is not None else '',
                     (d['fp'] if d else ''), (d['n_decoy_lines'] if d else ''),
                     r['ms']])
    write('set_filter_per_spectrum.csv',
          ['spectrum', 'detector', 'chain', 'chain_present', 'k_fwhm', 'i_min_pct', 'set_lines',
           'peaks_total', 'peaks_finder', 'peaks_library', 'anchors', 'finder_lost',
           'strong_lines_found', 'strong_lines_total', 'strong_lines_found_finder_only',
           'phantom_lines', 'decoy_lines', 'ms'], rows)


def calibration():
    rows = []
    for c in json.load(open(os.path.join(HERE, 'calibration.json'))):
        rows.append([c['key'], c['det'], c['channels'], '%.0f' % c['live'], c['mode'],
                     c['n_lines'], '%.2f' % c['rms'],
                     ' '.join('%.10g' % x for x in c['ecal']),
                     ' '.join('%.10g' % x for x in c['fwhm_ch']),
                     ' '.join('%.6g' % x for x in c['res_kev'])])
    write('spectra_calibration.csv',
          ['spectrum', 'detector', 'channels', 'live_s', 'ecal_mode', 'ecal_lines',
           'ecal_rms_keV', 'energy_poly', 'sqrt_fwhm_coef_ch', 'resolution_model_keV'], rows)


def deconv_grid():
    import io
    import re
    import contextlib
    import deconv_report
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        deconv_report.main(verbose=True)
    rows = []
    for line in buf.getvalue().splitlines():
        m = re.match(r'^(ASN16|AS80x80|RC103)\s+(\d+)\s+([\d.]+)\s+(\d+)\s*\|\s*'
                     r'([\d.-]+)\s+([\d.-]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)', line)
        if m:
            rows.append(list(m.groups()))
    write('deconvolution_grid.csv',
          ['detector', 'min_snr', 'roi_radius_fwhm', 'max_extra_per_roi', 'recall_pct',
           'gain_over_finder_pp', 'extra_peaks', 'unexplained_loose', 'unexplained_strict',
           'ms'], rows)


def combined_grid():
    import io
    import re
    import contextlib
    import combined_report
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        combined_report.main()
    rows = []
    det = deconv = None
    for line in buf.getvalue().splitlines():
        m = re.match(r'^(ASN16|AS80x80|RC103)\s+(off|on)\s+-\s+-\s*\|\s*([\d.]+)', line)
        if m:
            det, deconv = m.group(1), m.group(2)
            rows.append([det, deconv, '', '', m.group(3), '', '', '', '', ''])
            continue
        m = re.match(r'^\s+([\d.]+)\s+([\d.]+)\s*\|\s*([\d.]+)\s+([+\-][\d.]+)\s+'
                     r'([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)', line)
        if m and det:
            rows.append([det, deconv, m.group(1), m.group(2), m.group(3), m.group(4),
                         m.group(5), m.group(6), m.group(7), m.group(8)])
    write('combined_grid.csv',
          ['detector', 'deconvolution', 'k_fwhm', 'i_min_pct', 'recall_pct',
           'vs_baseline_pp', 'library_peaks', 'phantom_lines', 'finder_peaks_lost', 'ms'],
          rows)


if __name__ == '__main__':
    ensure()
    print('exporting to %s' % DEST)
    calibration()
    set_grid()
    per_spectrum()
    deconv_grid()
    combined_grid()
