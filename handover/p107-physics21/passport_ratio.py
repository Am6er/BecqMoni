# -*- coding: utf-8 -*-
r"""П99 — изм/ожид по паспорту для точечных эталонов G1S (Note: «Nuc A=… Бк dA=… DD-MM-YYYY»)
и трёх паспортных Cs-137 (ASN16/RC103) — rev30 против rev31 (decay_s компонента = Бк на дату съёмки).
  python passport_ratio.py
"""
import csv
import glob
import io
import math
import re
import sys
import xml.etree.ElementTree as ET
from datetime import date

for _s in (sys.stdout,):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\pie'
WT = r'D:\BqMoni_Claude\p107\wt\tools\CORPUS\corpus\spectra'
HALF = {'Cs-137': 30.08, 'Co-60': 5.2714, 'Am-241': 432.6, 'Ba-133': 10.551, 'Cd-109': 1.2651, 'Ce-139': 0.3766,
        'Co-57': 0.7444, 'Eu-152': 13.517, 'Mn-54': 0.8555, 'Na-22': 2.6018, 'Y-88': 0.2919, 'Zn-65': 0.6682,
        'Bi-207': 31.55, 'Th-228': 1.9116}
CS = [('ASN16_Cs137', 'Cs-137', 5715.5), ('ASN16_Cs137_10cm', 'Cs-137', 5712.3),
      ('RC103_Cs137_0cm', 'Cs-137', 5564.3), ('RC103_Cs137_50mm', 'Cs-137', 5235.6)]


def comp(d, key, nuc):
    for p in glob.glob(d + '/*_spline_components.csv'):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            if r.get('spectrum') == key and r.get('component') == nuc:
                try:
                    return float(r['decay_s'])
                except (ValueError, KeyError):
                    return None
    return None


def passport(key):
    rd = ET.parse(WT + '\\' + key + '.xml').getroot().find('ResultDataList/ResultData')
    note = rd.findtext('SampleInfo/Note') or ''
    m = re.match(r'\s*(\S+)\s+A=([\d.]+)\s*\S+\s+dA=([\d.]+)%\s+(\d\d)-(\d\d)-(\d{4})', note)
    if not m:
        return None
    nuc, a0, da, dd, mm, yy = m.group(1), float(m.group(2)), float(m.group(3)), int(m.group(4)), int(m.group(5)), int(m.group(6))
    t0 = date(yy, mm, dd)
    shot = rd.findtext('StartTime') or rd.findtext('SampleInfo/Time')
    t1 = date(int(shot[:4]), int(shot[5:7]), int(shot[8:10]))
    hl = HALF.get(nuc)
    if hl is None:
        return None
    a = a0 * 2 ** (-(t1 - t0).days / (hl * 365.25))
    return nuc, a, da, t1


def main():
    keys = sorted(set(k[:-4] for k in __import__('os').listdir(WT) if k.startswith('G1S') and ('_P5' in k or '_P25' in k)))
    print('%-18s %-7s %10s %10s %10s %8s %8s' % ('спектр', 'нуклид', 'паспорт', 'rev30', 'rev31', 'r30', 'r31'))
    rows = []
    for k in keys:
        p = passport(k)
        if not p:
            continue
        nuc, a, da, t1 = p
        m28 = comp(ROOT + '\\out_rev30_full', k, nuc)
        m29 = comp(ROOT + '\\out_rev31_full', k, nuc)
        if m28 is None or m29 is None:
            continue
        rows.append((k, nuc, a, m28, m29, m28 / a, m29 / a))
        print('%-18s %-7s %10.1f %10.1f %10.1f %8.3f %8.3f' % (k, nuc, a, m28, m29, m28 / a, m29 / a))
    for suf in ('_P5', '_P25'):
        r28 = sorted(r[5] for r in rows if r[0].endswith(suf)); r29 = sorted(r[6] for r in rows if r[0].endswith(suf))
        if r28:
            print('медиана %-4s rev30 %.3f -> rev31 %.3f (n=%d)' % (suf, r28[len(r28) // 2], r29[len(r29) // 2], len(r28)))
    print()
    print('%-18s %-7s %10s %10s %10s %8s %8s' % ('спектр', 'нуклид', 'паспорт', 'rev30', 'rev31', 'r30', 'r31'))
    for k, nuc, a in CS:
        m28 = comp(ROOT + '\\out_rev30_full', k, nuc); m29 = comp(ROOT + '\\out_rev31_full', k, nuc)
        print('%-18s %-7s %10.1f %10s %10s %8s %8s' % (k, nuc, a, '%.1f' % m28 if m28 else '-', '%.1f' % m29 if m29 else '-',
                                                     '%.3f' % (m28 / a) if m28 else '-', '%.3f' % (m29 / a) if m29 else '-'))


if __name__ == '__main__':
    main()
