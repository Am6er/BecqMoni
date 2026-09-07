# -*- coding: utf-8 -*-
u"""Насколько сдвинулся степенной узел ПШПВ у группы после перекалибровки.

Сравнивает узел `PowerFwhmCalibration` и ПШПВ на 662 кэВ у каждого спектра
группы: в `HEAD` (git) против каталога, названного первым доводом.

    python handover/p29-recal/fwhm_shift.py <каталог со спектрами> <ключ,ключ,...>
"""
import io
import os
import subprocess
import sys

import numpy as np
import xml.etree.ElementTree as ET


def nodes(txt):
    r = ET.fromstring(txt)
    rd = r.find('ResultDataList/ResultData')
    es = rd.find('EnergySpectrum')
    ec = [float(x.text) for x in es.find('EnergyCalibration/Coefficients')]
    fw = rd.find('PowerFwhmCalibration')
    co = [float(x.text) for x in fw.find('Coefficients')] if fw is not None else None
    n = int(es.find('NumberOfChannels').text)
    return ec, co, n


def main():
    d = sys.argv[1]
    keys = sys.argv[2].split(',')
    print(u'%-18s %-24s %-24s %s'
          % (u'ключ', u'узел ПШПВ было', u'стало', u'ПШПВ(662), кан.: было -> стало'))
    for k in keys:
        a = subprocess.run(['git', 'show', 'HEAD:tools/CORPUS/corpus/spectra/%s.xml' % k],
                           capture_output=True).stdout.decode('utf-8')
        b = io.open(os.path.join(d, k + '.xml'), encoding='utf-8').read()
        ea, fa, n = nodes(a)
        eb, fb, _ = nodes(b)

        def ch(ec, E):
            g = np.arange(n, dtype=float)
            e = sum(c * g ** i for i, c in enumerate(ec))
            return float(np.interp(E, e, g))

        def fwhm(f, c):
            return f[0] * max(c, 1e-9) ** f[1] if f else float('nan')

        c1, c2 = ch(ea, 662.0), ch(eb, 662.0)
        w1, w2 = fwhm(fa, c1), fwhm(fb, c2)
        print(u'%-18s %-24s %-24s %8.2f -> %8.2f  (%+.2f %%)'
              % (k, '%.6f %.6f' % (fa[0], fa[1]), '%.6f %.6f' % (fb[0], fb[1]),
                 w1, w2, 100 * (w2 / w1 - 1)))


main()
