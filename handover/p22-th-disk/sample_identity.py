# -*- coding: utf-8 -*-
"""Тот ли образец: корпусный `AS80_Th232Medal` (2025) против съёмки Amber 08.09.2026.

    python handover/p22-th-disk/sample_identity.py

Читает оба файла как есть (калибровка — записанная в файле), печатает cps по
всему спектру и чистые площади пиков 238/583/911/2614 кэВ в окне ±1.2 ПШПВ с
линейным фоном по боковым полосам. Никаких записей. П22, 12.09.2026.
"""
import os
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
A = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'spectra', 'AS80_Th232Medal.xml')
B = os.path.join(ROOT, 'tools', 'effmaker', 'probes', 'build_p13th', 'p13', 'Th-232_amber.xml')


def load(p):
    rd = ET.parse(p).getroot().find('.//ResultData')
    es = rd.find('EnergySpectrum')
    co = [float(c.text) for c in es.find('EnergyCalibration/Coefficients')]
    y = [float(d.text) for d in es.find('Spectrum')]
    return dict(co=co, y=y, lt=float(es.find('LiveTime').text),
                note=(rd.find('SampleInfo/Note').text or ''))


def energy(co, ch):
    return sum(c * ch ** i for i, c in enumerate(co))


def slope(co, c):
    return co[1] + 2 * co[2] * c + 3 * co[3] * c * c + 4 * co[4] * c ** 3


def net(d, e0):
    y = d['y']
    lo = min(range(len(y)), key=lambda c: abs(energy(d['co'], c) - (e0 - 60)))
    hi = min(range(len(y)), key=lambda c: abs(energy(d['co'], c) - (e0 + 60)))
    c0 = max(range(lo, hi + 1), key=lambda c: y[c])
    fw = 0.0765 * (662.0 / e0) ** 0.5 * e0 / slope(d['co'], c0)
    w = int(1.2 * fw)
    s = int(1.0 * fw)
    g = sum(y[c0 - w:c0 + w + 1])
    bg = (sum(y[c0 - w - s:c0 - w]) + sum(y[c0 + w + 1:c0 + w + 1 + s])) / (2 * s) * (2 * w + 1)
    return c0, g / d['lt'], (g - bg) / d['lt']


if __name__ == '__main__':
    a, b = load(A), load(B)
    for name, d in (('корпус 2025', a), ('Amber 08.09.2026', b)):
        print('%-18s live %7.0f с  всего %.2f cps  примечание «%s»'
              % (name, d['lt'], sum(d['y']) / d['lt'], d['note']))
    for e in (238.63, 583.19, 911.20, 2614.51):
        ca, ga, na = net(a, e)
        cb, gb, nb = net(b, e)
        print('%7.1f кэВ  корпус кан %4d чистая %.3f cps | Amber кан %4d чистая %.3f cps | отношение %.3f'
              % (e, ca, na, cb, nb, nb / na))
