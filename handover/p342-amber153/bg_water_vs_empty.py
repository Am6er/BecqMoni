# -*- coding: utf-8 -*-
u"""П342 (AMBER153, остаток (а)): фон воды против фона пустой защиты ОДНОГО прибора, поверка 2016.

ЗАЧЕМ. Корпусные спектры G1S16 в сосудах (маринелли, Дента-100, Петри-60) вычитают фон
«пустая защита» (`фон пустая защита_15`), а G1S24 — фон «закрытая крышка, вода» в маринелли
(`Фон закр кр вода_16`). У поверки 2016 есть ОБА фона, снятые одним прибором в соседние
сутки (17–18.05.2016 вода, 18–19.05.2016 пусто, по 54 000 с, одна калибровка
`ENERGY: 1,-25.81408,2.990863`). Их отношение по энергии — ИЗМЕРЕННОЕ пропускание
внешнего фона литром воды в маринелли r_вода(E), без модели.

ЧТО ПЕЧАТАЕТ. Отношение счёта (на секунду живого времени) вода/пусто по полосам энергии и
по площадям линий фона (свинец K, 239, 352, 583, 609, 911, 1461, 1764, 2614), площадь —
окно ±1.25 ПШПВ минус прямая по боковым окнам. Пишет `bg_water_vs_empty.csv`.

Положительный контроль: тот же счёт «пусто против пусто» (файл _15 против самого себя) обязан
дать 1.000 во всех полосах; «пусто _15 против пусто _08» (накопительная серия: _08 — это
8/15 набора) — ≈ 1.00 на секунду (статистика), а не 0.53, — иначе нормировка по TLIVE неверна.

    python handover/p342-amber153/bg_water_vs_empty.py
"""
import csv
import io
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(REPO, 'tools', 'CORPUS', 'scripts'))
from spe_import import read_spe  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

POV = r'D:\YandexDisk\Спектры\Спектры источники эталоны\Spe - поверки'
WATER16 = os.path.join(POV, 'Поверка 2016', 'Фон вода', 'фон вода_15.spe')
EMPTY16 = os.path.join(POV, 'Поверка 2016', 'фон пустая защита', 'фон пустая защита_15.spe')
EMPTY16_08 = os.path.join(POV, 'Поверка 2016', 'фон пустая защита', 'фон пустая защита_08.spe')
WATER24 = os.path.join(POV, 'Поверка 2024', 'Фон закр кр', 'Фон закр кр вода_16.spe')

BANDS = [(20, 30), (30, 40), (40, 50), (50, 60), (60, 70), (70, 80), (80, 90), (90, 100), (100, 120),
         (120, 150), (150, 200), (200, 300), (300, 400), (400, 500), (500, 700), (700, 1000),
         (1000, 1500), (1500, 2000), (2000, 2800)]
LINES = [('Pb Kα', 74.97), ('Pb Kβ', 84.9), ('Pb-212', 238.63), ('Pb-214', 351.93), ('511', 511.0),
         ('Tl-208', 583.19), ('Bi-214', 609.31), ('Ac-228', 911.2), ('Bi-214', 1120.29),
         ('K-40', 1460.82), ('Bi-214', 1764.49), ('Tl-208', 2614.51)]


def load(path):
    head, counts = read_spe(path, require_check=False)
    live = float(head['TLIVE'].replace(',', '.'))
    cal = [float(x) for x in head['ENERGY'].replace(',', ' ').split()]
    order = int(cal[0])
    coef = cal[1:2 + order]
    return head, counts, live, coef


def energy(coef, ch):
    return sum(c * ch ** i for i, c in enumerate(coef))


def channel_of(coef, e):
    lo, hi = 0.0, 1023.0
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if energy(coef, mid) < e:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def band_sum(counts, coef, e0, e1):
    # Каналы, чей центр в [e0, e1).
    s = 0
    for ch, c in enumerate(counts):
        e = energy(coef, float(ch))
        if e0 <= e < e1:
            s += c
    return s


def fwhm_kev(e):
    # Модель группы G1S16 (detectors.csv): ПШПВ ≈ 6.63 % на 662 по ~√E; для окна этого хватает.
    return 0.0663 * 661.66 * math.sqrt(e / 661.66) if e > 0 else 5.0


def line_area(counts, coef, e):
    w = 1.25 * fwhm_kev(e)
    c0, c1 = channel_of(coef, e - w), channel_of(coef, e + w)
    a, b = int(round(c0)), int(round(c1))
    n = b - a + 1
    if a - n < 0 or b + n >= len(counts):
        return float('nan'), float('nan')
    left = sum(counts[a - n:a]) / float(n)
    right = sum(counts[b + 1:b + 1 + n]) / float(n)
    gross = sum(counts[a:b + 1])
    base = 0.5 * (left + right) * n
    net = gross - base
    sig = math.sqrt(gross + base / 2.0)
    return net, sig


def main():
    hw, cw, lw, kw = load(WATER16)
    he, ce, le, ke = load(EMPTY16)
    h8, c8, l8, k8 = load(EMPTY16_08)
    h24, c24, l24, k24 = load(WATER24)
    print('вода 2016 :', hw['MEASBEGIN'], 'TLIVE', lw, 'CPS', hw['CPS'], 'калибровка', kw)
    print('пусто 2016:', he['MEASBEGIN'], 'TLIVE', le, 'CPS', he['CPS'], 'калибровка', ke)
    print('пусто _08 :', h8['MEASBEGIN'], 'TLIVE', l8)
    print('вода 2024 :', h24['MEASBEGIN'], 'TLIVE', l24, 'CPS', h24['CPS'], 'калибровка', k24)
    if kw != ke:
        print('⛔ калибровки воды и пустой защиты РАЗНЫЕ — сравнение по каналу неверно')
        return 2
    rows = []
    print()
    print('полоса, кэВ   вода/с      пусто/с    вода/пусто  ±σ     контроль пусто/пусто  _15/_08')
    for e0, e1 in BANDS:
        sw = band_sum(cw, kw, e0, e1)
        se = band_sum(ce, ke, e0, e1)
        s8 = band_sum(c8, k8, e0, e1)
        r = (sw / lw) / (se / le) if se else float('nan')
        sig = r * math.sqrt(1.0 / max(sw, 1) + 1.0 / max(se, 1))
        self_ctl = (se / le) / (se / le) if se else float('nan')
        r8 = (se / le) / (s8 / l8) if s8 else float('nan')
        print('%5d–%-5d  %9.4f  %9.4f   %7.3f  %6.3f   %7.3f               %6.3f' % (
            e0, e1, sw / lw, se / le, r, sig, self_ctl, r8))
        rows.append(['band', '%d-%d' % (e0, e1), '%.6f' % (sw / lw), '%.6f' % (se / le),
                     '%.5f' % r, '%.5f' % sig, '%.5f' % r8])
    print()
    print('линия            кэВ    вода, имп/с     пусто, имп/с    вода/пусто  ±σ')
    for name, e in LINES:
        nw, sw_ = line_area(cw, kw, e)
        ne, se_ = line_area(ce, ke, e)
        r = (nw / lw) / (ne / le) if ne > 0 else float('nan')
        sig = abs(r) * math.sqrt((sw_ / nw) ** 2 + (se_ / ne) ** 2) if nw > 0 and ne > 0 else float('nan')
        print('%-10s %8.2f   %.5f±%.5f  %.5f±%.5f   %6.3f  %6.3f' % (
            name, e, nw / lw, sw_ / lw, ne / le, se_ / le, r, sig))
        rows.append(['line', '%s %.2f' % (name, e), '%.6f' % (nw / lw), '%.6f' % (ne / le),
                     '%.5f' % r, '%.5f' % sig, ''])
    tw, te = sum(cw) / lw, sum(ce) / le
    print()
    print('всего: вода %.4f имп/с, пусто %.4f имп/с, вода/пусто %.4f' % (tw, te, tw / te))
    with io.open(os.path.join(HERE, 'bg_water_vs_empty.csv'), 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(['kind', 'what', 'water_cps', 'empty_cps', 'water_over_empty', 'sigma', 'empty15_over_08'])
        w.writerows(rows)
    return 0


if __name__ == '__main__':
    sys.exit(main())
