# -*- coding: utf-8 -*-
u"""Сплошная проверка корпуса на ТУ ЖЕ болезнь, что у `AS80_Charoite` (`A278`).

Для каждого спектра со ВСТРОЕННЫМ фоном ищет растяжение шкалы `s`, при котором
фон, пересаженный на каналы переднего плана как `ch_фон = ch_ПП / s`, лучше
всего его объясняет. `s ≠ 1` значит, что передний план и фон сняты при РАЗНОМ
усилении, а файл держит на них ОДНУ шкалу.

⛔ СУДЬЯ ГОДЕН НЕ ВЕЗДЕ, и это выяснилось первым же прогоном. Там, где у
переднего плана свой сильный источник (точечные Cs-137, Co-60, Na-22, Eu-152),
фон даёт единицы процентов отсчётов, а χ² держат пики источника, которых фон не
объясняет ВОВСЕ; минимум тогда уезжает на край сетки и не значит ничего. Первый
прогон 07.09.2026 выдал 33 «больных» спектра, из них 20 упирались в край, —
чистая ловушка «признак без области применимости».

Поэтому судятся только спектры, у которых фон даёт заметную долю отсчётов
переднего плана (`--minbg=`, умолчание 0.25; у `AS80_Charoite` 0.64) и у
которых минимум лежит ВНУТРИ сетки.

⚠ Судья ничего не говорит о том, КТО из двух уехал, и слеп к спектрам без
встроенного фона. Это ЛОКАТОР, а не приговор.

    python handover/p29-recal/screen_gain.py <каталог corpus> [порог %] [--minbg=0.25]
"""
import os
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
        sp = es.find('Spectrum')
        if sp is None:
            return None
        c = np.array([int(x.text) for x in sp.findall('DataPoint')], float)
        co = es.find('EnergyCalibration/Coefficients')
        co = np.array([float(x.text) for x in co]) if co is not None else None
        lt = es.find('LiveTime')
        out[tag] = dict(counts=c, ecal=co,
                        live=float(lt.text) if lt is not None and lt.text else 0.0)
    return out


def energy(co, ch):
    return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(co))


def best_s(f, b, ecal, emin=150.0, emax=2900.0, lo=0.96, hi=1.06, step=0.0005):
    n = len(f)
    if len(b) != n:
        return None
    ch = np.arange(n, dtype=float)
    E = energy(ecal, ch) if ecal is not None else ch
    m = (E >= emin) & (E <= emax)
    if m.sum() < 50:
        return None
    ss, cc = [], []
    for s in np.arange(lo, hi + 1e-9, step):
        bs = np.interp(ch / s, ch, b, left=0.0, right=0.0) / s
        w = 1.0 / np.maximum(f[m], 1.0)
        a = (f[m] * bs[m] * w).sum() / max((bs[m] * bs[m] * w).sum(), 1e-30)
        ss.append(s)
        cc.append((((f[m] - a * bs[m]) ** 2) * w).sum() / m.sum())
    i = int(np.argmin(cc))
    edge = i in (0, len(cc) - 1)
    if edge:
        smin = ss[i]
    else:
        x = np.array(ss[i - 1:i + 2])
        y = np.array(cc[i - 1:i + 2])
        p = np.polyfit(x, y, 2)
        smin = float(-p[1] / (2 * p[0]))
    return smin, cc[i], float(np.interp(1.0, ss, cc)), edge


def main():
    corpus = sys.argv[1]
    rest = [a for a in sys.argv[2:] if not a.startswith('--')]
    opts = dict(a[2:].split('=', 1) for a in sys.argv[2:] if a.startswith('--'))
    thr = float(rest[0]) if rest else 0.5
    minbg = float(opts.get('minbg', 0.25))
    sp = os.path.join(corpus, 'spectra')
    judged, skipped = [], []
    for fn in sorted(os.listdir(sp)):
        if not fn.endswith('.xml'):
            continue
        key = fn[:-4]
        try:
            d = load(os.path.join(sp, fn))
        except Exception as ex:
            skipped.append((key, u'ОШИБКА %s' % ex))
            continue
        if d is None:
            skipped.append((key, u'нет встроенного фона'))
            continue
        fg, bg = d['EnergySpectrum'], d['BackgroundEnergySpectrum']
        if fg['live'] <= 0 or bg['live'] <= 0 or fg['counts'].sum() < 1000:
            skipped.append((key, u'нет времени или отсчётов'))
            continue
        frac = bg['counts'].sum() * (fg['live'] / bg['live']) / max(fg['counts'].sum(), 1)
        if frac < minbg:
            skipped.append((key, u'фон даёт лишь %.0f %% отсчётов ПП' % (100 * frac)))
            continue
        r = best_s(fg['counts'], bg['counts'], fg['ecal'])
        if r is None:
            skipped.append((key, u'шкалы не сходятся'))
            continue
        s, chi, chi1, edge = r
        if edge:
            skipped.append((key, u'минимум НА КРАЮ сетки (s=%.3f) — не судим' % s))
            continue
        judged.append((key, s, chi, chi1, frac))
    judged.sort(key=lambda r: -abs(r[1] - 1.0))
    print(u'СУДИМО: %d;  не судимо: %d  (порог доли фона %.2f)'
          % (len(judged), len(skipped), minbg))
    print(u'\n%-28s %9s %9s %11s %11s %10s'
          % (u'ключ', u's', u'уход %', u'χ² в s', u'χ² в 1.0', u'доля фона'))
    for key, s, chi, chi1, frac in judged:
        mark = u'  <<<' if abs(s - 1.0) * 100 >= thr else u''
        print(u'%-28s %9.5f %+9.2f %11.4f %11.4f %10.2f%s'
              % (key, s, 100 * (s - 1), chi, chi1, frac, mark))
    print(u'\n--- НЕ СУДИМЫ (и почему) ---')
    for key, why in skipped:
        print(u'  %-28s %s' % (key, why))


main()
