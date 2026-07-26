# -*- coding: utf-8 -*-
"""Top deconvolution settings per detector, by recall gain and by cost."""
import io
import re
import sys
import contextlib
from collections import defaultdict
import deconv_report

buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    deconv_report.main(verbose=True)

rows = defaultdict(list)
for line in buf.getvalue().splitlines():
    m = re.match(r'^(ASN16|AS80x80|RC103)\s+(\d+)\s+([\d.]+)\s+(\d+)\s*\|\s*'
                 r'([\d.-]+)\s+([\d.-]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)', line)
    if m:
        det, snr, roi, extra, rec, gain, extras, unexpl, strict, ms = m.groups()
        rows[det].append(dict(snr=float(snr), roi=float(roi), extra=int(extra),
                              rec=float(rec), gain=float(gain), extras=float(extras),
                              strict=float(strict), ms=float(ms)))

for det in ('ASN16', 'AS80x80', 'RC103'):
    r = rows[det]
    if not r:
        continue
    print('=' * 88)
    print('%s  (%d grid points)' % (det, len(r)))
    print('  snr  roi extra | recall%  gain pp  extras  unexplained    ms')
    print('  --- best recall gain ---')
    for x in sorted(r, key=lambda x: (-x['gain'], x['ms']))[:8]:
        print('  %3.0f %4.1f %5d | %7.1f %8.1f %7.1f %12.2f %6.0f' % (
            x['snr'], x['roi'], x['extra'], x['rec'], x['gain'], x['extras'], x['strict'], x['ms']))
    print('  --- best gain per second ---')
    for x in sorted(r, key=lambda x: -(x['gain'] / max(x['ms'] / 1000.0, 0.05)))[:5]:
        print('  %3.0f %4.1f %5d | %7.1f %8.1f %7.1f %12.2f %6.0f' % (
            x['snr'], x['roi'], x['extra'], x['rec'], x['gain'], x['extras'], x['strict'], x['ms']))
    print()
