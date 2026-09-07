# -*- coding: utf-8 -*-
u"""Гайн переднего плана ОТНОСИТЕЛЬНО встроенного фона — по одним и тем же линиям.

⛔ Замысел. Комнатный фон составляет большую часть СЫРОГО переднего плана
(у `AS80_Charoite` 174.6 имп/с × 1584 с = 276 тыс. из 433 тыс. отсчётов), то
есть одни и те же линии K-40, Tl-208, Bi-214 видны в ОБОИХ. Прибор один, линии
одни, а гистограммы сняты в разное время: значит отношение каналов, на которых
эти линии стоят, — прямая мера ухода усиления между двумя съёмками, и она не
зависит ни от таблицы, ни от подгонки шкалы.

    python handover/p29-recal/centers2.py <файл.xml> [fwhm662_pct]
"""
import sys, os
import numpy as np
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', '..', 'tools', 'CORPUS', 'scripts'))
import gaussfit                                                # noqa: E402

LINES = [(238.632, 'Pb-212'), (295.224, 'Pb-214'), (351.932, 'Pb-214'),
         (583.187, 'Tl-208'), (609.312, 'Bi-214'), (911.204, 'Ac-228'),
         (1120.287, 'Bi-214'), (1460.822, 'K-40'), (1764.494, 'Bi-214'),
         (2614.511, 'Tl-208')]


def load(path):
    r = ET.parse(path).getroot()
    out = {}
    for tag in ('EnergySpectrum', 'BackgroundEnergySpectrum'):
        es = r.find('.//' + tag)
        if es is None:
            continue
        c = np.array([int(x.text) for x in es.find('Spectrum').findall('DataPoint')], float)
        co = np.array([float(x.text) for x in es.find('EnergyCalibration/Coefficients')])
        out[tag] = dict(counts=c, ecal=co, live=float(es.find('LiveTime').text))
    return out


def energy(co, ch):
    return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(co))


def channel(co, e, n):
    g = np.arange(n, dtype=float)
    return float(np.interp(e, energy(co, g), g))


def fit(counts, co, e, fw662, window=2.0):
    n = len(counts)
    ch0 = channel(co, e, n)
    if ch0 < 10 or ch0 > n - 11:
        return None, 'вне шкалы'
    sig_kev = fw662 / 100.0 * 662.0 * np.sqrt(max(e, 1.0) / 662.0) / gaussfit.FWHM_SIGMA
    dEdch = co[1] + 2 * (co[2] if len(co) > 2 else 0.0) * ch0
    sig_ch = max(sig_kev / dEdch, 1.5)
    r, why = gaussfit.fit_peak_ex(counts, ch0, sig_ch, window=window)
    return r, why


def main():
    path = sys.argv[1]
    fw = float(sys.argv[2]) if len(sys.argv) > 2 else 7.65
    d = load(path)
    fg, bg = d['EnergySpectrum'], d['BackgroundEnergySpectrum']
    n = len(fg['counts'])
    kbg = fg['live'] / bg['live']
    print(u'файл: %s' % path)
    print(u'фон в переднем плане: %.1f имп/с × %.0f с = %.0f отсчётов из %.0f (%.0f %%)'
          % (bg['counts'].sum() / bg['live'], fg['live'],
             bg['counts'].sum() * kbg, fg['counts'].sum(),
             100 * bg['counts'].sum() * kbg / fg['counts'].sum()))
    print(u'\n%-9s %-8s %10s %10s %9s %10s %10s'
          % (u'E_табл', u'нуклид', u'кан_ПП', u'кан_фон', u'кан_ПП/фон',
             u'кан_фон/E', u'кан_ПП/E'))
    rr = []
    for e, nuc in LINES:
        a, wa = fit(fg['counts'], fg['ecal'], e, fw)
        b, wb = fit(bg['counts'], bg['ecal'], e, fw)
        if a is None or b is None:
            print(u'%-9.2f %-8s   ПП:%-12s фон:%-12s' % (e, nuc, wa or '-', wb or '-'))
            continue
        ea, eb = float(energy(fg['ecal'], a['mu'])), float(energy(bg['ecal'], b['mu']))
        print(u'%-9.2f %-8s %10.2f %10.2f %9.5f %10.5f %10.5f'
              % (e, nuc, a['mu'], b['mu'], a['mu'] / b['mu'], eb / e, ea / e))
        rr.append((e, a['mu'], b['mu'], a.get('amp', 0), b.get('amp', 0), ea, eb))
    if rr:
        w = np.array([min(r[3], 1e6) for r in rr], float)
        ca = np.array([r[1] for r in rr]); cb = np.array([r[2] for r in rr])
        print(u'\nусиление ПП относительно фона (взвеш. по амплитуде ПП, через ноль): %.5f'
              % ((w * ca * cb).sum() / (w * cb * cb).sum()))
        print(u'то же без весов (медиана отношений каналов): %.5f' % np.median(ca / cb))
        et = np.array([r[0] for r in rr])
        print(u'фон против таблицы (медиана E_найд/E_табл):   %.5f'
              % np.median(np.array([r[6] for r in rr]) / et))
        print(u'ПП  против таблицы (медиана E_найд/E_табл):   %.5f'
              % np.median(np.array([r[5] for r in rr]) / et))


main()
