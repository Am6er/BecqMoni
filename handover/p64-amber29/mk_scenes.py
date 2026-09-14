# -*- coding: utf-8 -*-
"""П64 (AMBER29): две сцены угля в маринелли 1 л на G1S — из корпусного шаблона
`G1S_mar1l_oisn06_057_p24.in` (детектор NaI 6.3 × 6.3, отражатель 0.13/0.1, оболочка
0.18/0.2, крепление 0.2, `DS_Fwhm662 = 6.44 %` — как у всех `G1S_mar1l_*`; сосуд ПЭ
стенки 0.2 см, зазор до торца 0.1 см).

Изменено ровно:
  * вещество пробы (оба блока, SC_ и SM_): активированный уголь — углерод Z = 6, ρ 0.461
    (масса 461 г / объём 1000 мл из шапки `.spe`; в `matdb` угля нет — графит 1.7,
    аморфный C 2.0; состав задан явно, база не пишется);
  * сцена (Б) `G1S_coal_corpus` — сосуд КОРПУСНЫЙ: Ø135.2 × 104, колодец Ø76 × 70,
    засыпка 100 мм (объём ровно 1000 см³ при стенке 2 мм — так и подобран 13.519602);
  * сцена (А) `G1S_coal_omasn` — сосуд ПО ЧЕРТЕЖУ ОМАСН (Радиевый институт, стр. 40):
    корпус Ø154 (крышка Ø156.5), высота 112, колодец Ø97 глубиной 65; «24» у верха —
    свободная высота между потолком колодца и крышкой (112 − 65 − 24 = 23 мм на крышку с
    пробкой). Семантика модели (`EfficiencySimulator`, ветка Marinelli): `SM_BeakerDiameter`
    — НАРУЖНЫЙ Ø, `SM_BeakerHoleDiameter` — ВНУТРЕННИЙ Ø колодца, стенки снаружи от него;
    объём пробы = π(r_out² − r_in²)·h + π·r_in²·(h − h_колодца), r_in = 48.5 + 2 = 50.5,
    r_out = 77 − 2 = 75 (мм). Высота засыпки из объёма 1 л: h = (10⁶ + π·50.5²·65)/(π·75²)
    = 86.06 мм — то есть 21 мм над потолком колодца из «24» свободных, 3 мм воздуха под
    крышкой; ρ = 0.461 та же.

    python handover/p64-amber29/mk_scenes.py <каталог склада полосы>

Пишет <ключ>.in (cp1251, CRLF — как шаблон) и index.csv (geometry,spectrum,preset,vessel):
спектры `coalA_*` — сцена А, `coalB_*` — сцена Б (копии одних XML, `mk_wd.ps1`).
"""
import io
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
TEMPLATE = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'geometries', 'G1S_mar1l_oisn06_057_p24.in')

KEYS = ['coal_t20m', 'coal_t2h', 'coal_t3h'] + ['coal_e%02d' % i for i in range(1, 31)]
BG2 = ['coal_t20m_bg2', 'coal_e01_bg2', 'coal_e30_bg2']


def set_key(text, key, value):
    pat = re.compile(r'^(' + re.escape(key) + r'\s*=\s*)(.*?)(\r?\n)', re.M)
    m = pat.search(text)
    if not m:
        raise SystemExit('нет ключа ' + key)
    return text[:m.start(2)] + value + text[m.end(2):]


def replace_source(text, prefix, rho):
    start = text.index('%s_nSourceElements' % prefix)
    end = text.index('// Empty space', start)
    block = (
        "{p}_nSourceElements = 1\r\n"
        "{p}_RoSource = {rho}\r\n"
        "{p}_ZSource[0] = 6\r\n"
        "{p}_FractionsSource[0] = 1\r\n"
        "{p}_FractionTypeSource = MASS\r\n"
        "M_{p}_Source.MName = Activated charcoal\r\n"
        "M_{p}_Source.Nmaterials = 1\r\n"
        "M_{p}_Source.Name[0] = Activated charcoal                       \r\n"
        "M_{p}_Source.MatRelWeight[0] = 1\r\n"
        "\r\n"
        "\r\n"
    ).format(p=prefix, rho=rho)
    return text[:start] + block + text[end:]


def omasn_height():
    r_in = 0.5 * 97.0 + 2.0
    r_out = 0.5 * 154.0 - 2.0
    return (1e6 + math.pi * r_in * r_in * 65.0) / (math.pi * r_out * r_out)   # мм


def volume_cm3(d_out, d_hole, h_hole, h_src, wall=2.0):
    r_in = 0.5 * d_hole + wall
    r_out = 0.5 * d_out - wall
    return (math.pi * (r_out * r_out - r_in * r_in) * h_src + math.pi * r_in * r_in * (h_src - h_hole)) / 1000.0


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    t = io.open(TEMPLATE, 'rb').read().decode('cp1251')
    assert 'SourceType = MARINELLI' in t and 'SM_RoSource = 0.57' in t
    t = replace_source(t, 'SC', '0.461')
    t = replace_source(t, 'SM', '0.461')
    scenes = {}
    scenes['G1S_coal_corpus'] = t
    h = omasn_height()
    a = t
    a = set_key(a, 'SM_BeakerDiameter', '15.4 cm')
    a = set_key(a, 'SM_BeakerHeight', '11.2 cm')
    a = set_key(a, 'SM_BeakerHoleDiameter', '9.7 cm')
    a = set_key(a, 'SM_BeakerHoleHeight', '6.5 cm')
    a = set_key(a, 'SM_SourceHeight', '%.4f cm' % (h / 10.0))
    scenes['G1S_coal_omasn'] = a
    print('ОМАСН: высота засыпки %.2f мм, объём %.1f см³; корпусный: объём %.1f см³'
          % (h, volume_cm3(154.0, 97.0, 65.0, h), volume_cm3(135.19602, 76.0, 70.0, 100.0)))
    rows = ['geometry,spectrum,preset,vessel']
    for key, tag, human in (('G1S_coal_omasn', 'A', 'маринелли 1 л ОМАСН Ø154×112, колодец Ø97×65, засыпка 86.1 мм'),
                            ('G1S_coal_corpus', 'B', 'маринелли 1 л корпусный Ø135.2×104, колодец Ø76×70, засыпка 100 мм')):
        with io.open(os.path.join(out, key + '.in'), 'wb') as fh:
            fh.write(scenes[key].encode('cp1251'))
        for k in KEYS + BG2:
            rows.append('%s,%s,Gamma-1S UDS-GC 63x63,"уголь 461 г ρ 0.461, %s"' % (key, k.replace('coal_', 'coal%s_' % tag), human))
        print(key)
    with io.open(os.path.join(out, 'index.csv'), 'w', encoding='utf-8', newline='') as fh:
        fh.write('\n'.join(rows) + '\n')


if __name__ == '__main__':
    main()
