# -*- coding: utf-8 -*-
u"""Положительный контроль судьи `xcorr`/`screen_gain`: когда шкалы ЗАВЕДОМО одни.

⛔ Зачем. При `s = 1` пересадка `np.interp(ch/s, ch, b)` возвращает фон
ТОЖДЕСТВЕННО, а при любом `s ≠ 1` линейная интерполяция его СГЛАЖИВАЕТ, и
сглаживание само по себе уменьшает χ². То есть у судьи есть встроенный уклон
ПРОТИВ ровно единицы, и не проверив его, легко принять уклон за находку.

Контроль: сличаем фон САМ С СОБОЙ (тот же массив в обе стороны). Настоящий
ответ известен — 1.00000. Всё, что судья покажет сверх этого, и есть его уклон.

    python handover/p29-recal/xcorr_selftest.py <файл.xml> [ещё файлы...]
"""
import sys

import numpy as np
import xml.etree.ElementTree as ET


def load(path):
    r = ET.parse(path).getroot()
    out = {}
    for tag in ('EnergySpectrum', 'BackgroundEnergySpectrum'):
        es = r.find('.//' + tag)
        if es is None:
            return None
        c = np.array([int(x.text) for x in es.find('Spectrum').findall('DataPoint')], float)
        co = es.find('EnergyCalibration/Coefficients')
        out[tag] = dict(counts=c,
                        ecal=np.array([float(x.text) for x in co]) if co is not None else None)
    return out


def energy(co, ch):
    return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(co))


def best_s(f, b, ecal, emin=150.0, emax=2900.0):
    n = len(f)
    ch = np.arange(n, dtype=float)
    E = energy(ecal, ch) if ecal is not None else ch
    m = (E >= emin) & (E <= emax)
    ss, cc = [], []
    for s in np.arange(0.96, 1.0605, 0.0005):
        bs = np.interp(ch / s, ch, b, left=0.0, right=0.0) / s
        w = 1.0 / np.maximum(f[m], 1.0)
        a = (f[m] * bs[m] * w).sum() / max((bs[m] * bs[m] * w).sum(), 1e-30)
        ss.append(s)
        cc.append((((f[m] - a * bs[m]) ** 2) * w).sum() / m.sum())
    i = int(np.argmin(cc))
    return ss[i], cc[i], float(np.interp(1.0, ss, cc))


def main():
    print(u'%-30s %10s %12s %12s %10s'
          % (u'файл (фон против себя)', u's', u'χ² в s', u'χ² в 1.0', u'выигрыш %'))
    for p in sys.argv[1:]:
        d = load(p)
        if d is None:
            print(u'%-30s нет встроенного фона' % p)
            continue
        b = d['BackgroundEnergySpectrum']['counts']
        s, c, c1 = best_s(b, b, d['EnergySpectrum']['ecal'])
        print(u'%-30s %10.5f %12.6f %12.6f %10.2f'
              % (p.split('\\')[-1].split('/')[-1], s, c, c1, 100 * (1 - c / max(c1, 1e-30))))


main()
