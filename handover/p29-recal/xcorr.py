# -*- coding: utf-8 -*-
u"""Уход усиления переднего плана относительно ВСТРОЕННОГО ФОНА — без таблицы линий.

⛔ Судья независим и от таблицы энергий, и от разложения FSA. Комнатный фон даёт
64 % отсчётов сырого переднего плана `AS80_Charoite`, то есть в обеих
гистограммах стоят ОДНИ И ТЕ ЖЕ линии одного прибора, снятые в разное время.
Ищем растяжение шкалы `s`, при котором фон, пересаженный на каналы переднего
плана как `ch_фон = ch_ПП / s`, лучше всего его объясняет:

    min по s, a   Σ (ПП_i − a·Фон_i(s))² / max(ПП_i, 1)

Собственные линии чароита в эту сумму входят как ПОМЕХА (их фон не объясняет) и
работают только против чувствительности, а не за неё: они широкие и малые.
Несовпадение шкал даёт у каждого сильного пика S-образную невязку, и она стоит
в этой сумме дороже всего.

    python handover/p29-recal/xcorr.py <файл.xml> [E_min кэВ] [E_max кэВ]
"""
import sys
import numpy as np
import xml.etree.ElementTree as ET


def load(path):
    r = ET.parse(path).getroot()
    out = {}
    for tag in ('EnergySpectrum', 'BackgroundEnergySpectrum'):
        es = r.find('.//' + tag)
        c = np.array([int(x.text) for x in es.find('Spectrum').findall('DataPoint')], float)
        co = np.array([float(x.text) for x in es.find('EnergyCalibration/Coefficients')])
        out[tag] = dict(counts=c, ecal=co, live=float(es.find('LiveTime').text))
    return out


def energy(co, ch):
    return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(co))


def main():
    path = sys.argv[1]
    emin = float(sys.argv[2]) if len(sys.argv) > 2 else 150.0
    emax = float(sys.argv[3]) if len(sys.argv) > 3 else 2900.0
    d = load(path)
    fg, bg = d['EnergySpectrum'], d['BackgroundEnergySpectrum']
    f, b = fg['counts'], bg['counts']
    n = len(f)
    ch = np.arange(n, dtype=float)
    E = energy(fg['ecal'], ch)
    m = (E >= emin) & (E <= emax)
    print(u'файл: %s' % path)
    print(u'полоса %.0f…%.0f кэВ = каналы %d…%d' % (emin, emax, ch[m][0], ch[m][-1]))
    best = None
    out = []
    for s in np.arange(0.980, 1.0605, 0.0005):
        bs = np.interp(ch / s, ch, b, left=0.0, right=0.0) / s
        num = (f[m] * bs[m] / np.maximum(f[m], 1.0)).sum()
        den = (bs[m] * bs[m] / np.maximum(f[m], 1.0)).sum()
        a = num / max(den, 1e-30)
        chi = ((f[m] - a * bs[m]) ** 2 / np.maximum(f[m], 1.0)).sum() / m.sum()
        out.append((s, chi, a))
        if best is None or chi < best[1]:
            best = (s, chi, a)
    for s, chi, a in out:
        if abs(s - best[0]) < 0.0105 or abs(round(s * 1000) % 10) < 1e-9:
            print(u'  s=%.4f  χ²/канал %10.4f  доля фона %.4f%s'
                  % (s, chi, a, u'   <<< минимум' if s == best[0] else u''))
    # параболa по трём точкам вокруг минимума
    i = [o[0] for o in out].index(best[0])
    if 0 < i < len(out) - 1:
        x = np.array([out[i - 1][0], out[i][0], out[i + 1][0]])
        y = np.array([out[i - 1][1], out[i][1], out[i + 1][1]])
        c = np.polyfit(x, y, 2)
        print(u'\nМИНИМУМ (парабола по трём точкам): s = %.5f' % (-c[1] / (2 * c[0])))
    print(u'усиление переднего плана относительно фона: %.5f' % best[0])


main()
