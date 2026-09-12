# -*- coding: utf-8 -*-
"""П34 12.09.2026, `A309` — ПРОФИЛЬ ПОРОГА АЦП по всему корпусу (131 спектр + их фоны).

    python handover/p34-fit-floor/profile.py [--dump=<csv>] [--show=<имя,...>]

Для каждого спектра (проба и фон порознь): первый ненулевой канал и его энергия («adc», как
`FsaBand.AdcFloorOf`), и профиль первых 48 каналов от него — чтобы НАЗВАТЬ рампу по данным,
а не догадкой. Ничего не пишет в корпус (только чтение).
"""
import csv
import io
import os
import sys
import xml.etree.ElementTree as ET

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SPECTRA = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'spectra')
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def read_es(es):
    counts = np.array([int(d.text) for d in es.findall('Spectrum/DataPoint')], dtype=float)
    ecal = [float(x.text) for x in es.findall('EnergyCalibration/Coefficients/Coefficient')]
    lt = es.find('LiveTime')
    live = float(lt.text) if lt is not None and lt.text else float(es.find('MeasurementTime').text)
    return counts, ecal, live


def energy(ecal, ch):
    ch = np.asarray(ch, dtype=float)
    return sum(c * ch ** i for i, c in enumerate(ecal))


def load(path):
    root = ET.parse(path).getroot()
    rd = root.find('ResultDataList/ResultData')
    es = rd.find('EnergySpectrum')
    bg = rd.find('BackgroundEnergySpectrum')
    s = read_es(es)
    b = read_es(bg) if bg is not None and bg.find('Spectrum') is not None and len(bg.findall('Spectrum/DataPoint')) > 0 else None
    return s, b


def first_nonzero(counts, ecal):
    for ch in range(len(counts)):
        if counts[ch] > 0:
            e = float(energy(ecal, ch))
            if e > 0:
                return ch, e
    return -1, 0.0


def main():
    dump = None
    show = set()
    for a in sys.argv[1:]:
        if a.startswith('--dump='):
            dump = a[7:]
        elif a.startswith('--show='):
            show = set(a[7:].split(','))
    rows = []
    for name in sorted(os.listdir(SPECTRA)):
        if not name.endswith('.xml'):
            continue
        key = name[:-4]
        s, b = load(os.path.join(SPECTRA, name))
        for kind, sp in (('проба', s), ('фон', b)):
            if sp is None:
                rows.append((key, kind, -1, 0.0, 0.0, '', 0))
                continue
            counts, ecal, live = sp
            ch0, e0 = first_nonzero(counts, ecal)
            de = float(energy(ecal, ch0 + 1) - energy(ecal, ch0)) if ch0 >= 0 else 0.0
            prof = counts[ch0:ch0 + 48] if ch0 >= 0 else np.array([])
            rows.append((key, kind, ch0, e0, de, ' '.join('%d' % c for c in prof), len(counts)))
            if key in show or not show:
                print('%-26s %-5s ch0=%5d E0=%7.2f кэВ dE=%.3f n=%d  | %s' % (
                    key, kind, ch0, e0, de, len(counts), ' '.join('%d' % c for c in prof[:32])))
    if dump:
        with io.open(dump, 'w', encoding='utf-8', newline='') as f:
            w = csv.writer(f)
            w.writerow(['spectrum', 'kind', 'ch0', 'E0_keV', 'dE_keV', 'n', 'profile48'])
            for r in rows:
                w.writerow([r[0], r[1], r[2], '%.3f' % r[3], '%.4f' % r[4], r[6], r[5]])


if __name__ == '__main__':
    main()
