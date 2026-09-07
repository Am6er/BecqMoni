# -*- coding: utf-8 -*-
u"""Где НА САМОМ ДЕЛЕ стоят линии: центры гауссиан против табличных энергий.

Читает спектр корпуса и фитит гауссианы в окнах вокруг ОЖИДАЕМЫХ каналов
табличных линий, отдельно у переднего плана (за вычетом фона) и у встроенного
фона. Печатает найденный канал, энергию по хранящейся шкале и отношение
E_найд / E_табл — то самое усиление, если оно одно на всю шкалу.

    python handover/p29-recal/centers.py <файл.xml> [fwhm662_pct]
"""
import sys, os
import numpy as np
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', '..', 'tools', 'CORPUS', 'scripts'))
import gaussfit                                                # noqa: E402

# (энергия кэВ, подпись). Линии ряда Ra-226, ряда Th-232 и K-40 — то, что
# заведомо есть и в чароите, и в комнатном фоне.
LINES = [
    (238.632, 'Pb-212'), (241.997, 'Pb-214'), (295.224, 'Pb-214'),
    (338.320, 'Ac-228'), (351.932, 'Pb-214'), (583.187, 'Tl-208'),
    (609.312, 'Bi-214'), (727.330, 'Bi-212'), (768.356, 'Bi-214'),
    (911.204, 'Ac-228'), (968.971, 'Ac-228'), (1120.287, 'Bi-214'),
    (1238.110, 'Bi-214'), (1460.822, 'K-40'), (1764.494, 'Bi-214'),
    (2204.210, 'Bi-214'), (2614.511, 'Tl-208'),
]


def load(path):
    r = ET.parse(path).getroot()
    out = {}
    for tag in ('EnergySpectrum', 'BackgroundEnergySpectrum'):
        es = r.find('.//' + tag)
        if es is None:
            continue
        c = np.array([int(x.text) for x in es.find('Spectrum').findall('DataPoint')], float)
        co = [float(x.text) for x in es.find('EnergyCalibration/Coefficients')]
        out[tag] = dict(counts=c, ecal=np.asarray(co),
                        live=float(es.find('LiveTime').text))
    return out


def energy(co, ch):
    return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(co))


def channel(co, e, n):
    grid = np.arange(n, dtype=float)
    return float(np.interp(e, energy(co, grid), grid))


def scan(name, counts, co, fw662):
    n = len(counts)
    print(u'\n=== %s (отсчётов %d) ===' % (name, int(counts.sum())))
    print(u'%-9s %-8s %9s %10s %10s %8s %8s'
          % (u'E_табл', u'нуклид', u'канал', u'E_найд', u'ΔE', u'E_н/E_т', u'ампл/σ'))
    rows = []
    for e, nuc in LINES:
        ch0 = channel(co, e, n)
        if ch0 < 8 or ch0 > n - 9:
            continue
        sig_kev = fw662 / 100.0 * 662.0 * np.sqrt(max(e, 1.0) / 662.0) / gaussfit.FWHM_SIGMA
        dEdch = co[1] + 2 * (co[2] if len(co) > 2 else 0.0) * ch0
        sig_ch = max(sig_kev / dEdch, 1.5)
        r = gaussfit.fit_peak(counts, ch0, sig_ch, window=2.2)
        if r is None:
            print(u'%-9.2f %-8s %9s' % (e, nuc, u'— фит не сел'))
            continue
        mu = r['mu']
        en = float(energy(co, mu))
        snr = r.get('amp', 0.0) / max(r.get('amp_err', 1e-9), 1e-9)
        print(u'%-9.2f %-8s %9.2f %10.2f %+10.2f %8.5f %8.1f'
              % (e, nuc, mu, en, en - e, en / e, snr))
        rows.append((e, en, snr))
    if rows:
        w = np.array([min(r[2], 60.0) for r in rows])
        et = np.array([r[0] for r in rows]); en = np.array([r[1] for r in rows])
        g = float((w * en * et).sum() / (w * et * et).sum())
        print(u'  взвешенное усиление E_найд/E_табл (через ноль): %.5f  по %d линиям'
              % (g, len(rows)))
    return rows


def main():
    path = sys.argv[1]
    fw = float(sys.argv[2]) if len(sys.argv) > 2 else 7.65
    d = load(path)
    fg = d['EnergySpectrum']
    print(u'файл: %s' % path)
    print(u'шкала: %s' % ' '.join('%.10g' % c for c in fg['ecal']))
    if 'BackgroundEnergySpectrum' in d:
        bg = d['BackgroundEnergySpectrum']
        k = fg['live'] / bg['live']
        scan(u'ПЕРЕДНИЙ ПЛАН за вычетом фона', fg['counts'] - bg['counts'] * k, fg['ecal'], fw)
        scan(u'ВСТРОЕННЫЙ ФОН (своя шкала)', bg['counts'], bg['ecal'], fw)
    else:
        scan(u'ПЕРЕДНИЙ ПЛАН', fg['counts'], fg['ecal'], fw)


main()
