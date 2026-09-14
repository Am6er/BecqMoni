# -*- coding: utf-8 -*-
"""П67 (AMBER22): три геометрии ториевого диска Amber на AS80x80 одним составом — сцены `.in` из корпусного шаблона
`AS80_th_disk.in` (детектор NaI 80×80: отражатель 0.2, зазор 2.17, оболочка 0.3, крепление 0.2, DS_Fwhm662 7.65 %).

Диск — стекло Ø40 × 5 мм (заметка Amber в спектре «диск D = 40 mm, thick = 5 mm»; обод 6 мм = стекло у края 5 +
губки 2 × 0.5; купол на лице не мерен — держится параметром H_GLASS, при куполе брать ⟨h²⟩/⟨h⟩), объём 6.28 см³.
Оправа — стальное кольцо Ø40→42 (металл 1 мм, замер Amber 14.09.2026), высота 6 мм. Трубка — картон, стенка 2 мм,
Ø33/37, длина 81 мм, целлюлоза ρ 0.7. Эквиваленты подобраны трассировкой прямого потока (`tracer.py`,
`tracer_tune.txt`): модель/точно по энергиям 238…2614 и сдвиг отношения 238:2614.

Что умеет формат `.in` и чем заменено то, чего он не умеет (находка П67):
  * одна стенка сосуда одним веществом (торцевая + боковая), кольца/трубки под источником нет, диска ребром нет;
  * КОНТАКТ: цилиндр Ø40×5 вплотную к крышке, боковая стенка сталь 1 мм (= кольцо), торцевой нет
    (модель/точно 1.002 плоско, 238:2614 +0.07 %);
  * ЛИЦОМ 81 мм: цилиндр Ø40×5, дно стекла на 82 мм (губка 1 мм под ним — на 0.5 мм толще замера, в запас),
    трубка ПОД диском — форматом не выражается; заменена торцевой стенкой целлюлозы 8.0 мм (средний путь лучей
    в стенке трубки, взвешенный потоком, 8.7 мм; модель/точно 1.001…1.002, 238:2614 +0.01 %); кольцо лицом
    даёт ≤ 0.2 % — снято (стенка одна);
  * РЕБРОМ 93 мм: КОРОБКА 40 × 5 × 40 мм (ось диска поперёк), центр на 93 мм; кольцо на ближней кромке +
    трубка — ОДНОЙ стенкой-смесью «сталь 1.0 мм + целлюлоза 6.5 мм» по массовой толщине (ρ 1.66, Fe 63 % массы)
    торцом 7.5 мм и боковой стенкой 0.45 мм той же смеси на гранях (лучи, выходящие через плоские грани диска,
    торцевой стенки не пересекают — их ловит боковая); модель/точно 0.999/1.001/1.002/1.002 (238/338/583/911),
    238:2614 −0.18 % при ρ 3.3 (по плотностям — `tracer_tune.txt`);
  * состав стекла — `glass.py` (ThO₂ по активности, Ba/La по плотности), плотность — ПАРАМЕТР (2.8/3.3/3.8/4.2);
    контроль — состав П13 («Ториевое стекло» 4.345, ThO₂ 16 %) на тех же трёх моделях.

    python mk_scenes.py <каталог склада полосы>

Пишет <ключ>.in (cp1251, CRLF — как шаблон) и index.csv (geometry,spectrum,preset,vessel).
"""
import io
import os
import re
import sys

import glass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
TEMPLATE = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'geometries', 'AS80_th_disk.in')

H_GLASS = 0.408          # см, эквивалентная толщина кабошона «кромка 3 + купол 2» (⟨h²⟩/⟨h⟩; решение распорядителя 14.09.2026 — держать П56; объём не измерен)
D_GLASS = 4.0            # см
RING_T = 0.1             # см, металл кольца (замер Amber 14.09.2026: 1 мм)
Z_FACE81 = 8.2           # см, дно стекла лицом на трубке (81 + 1 мм губки)
PAPER_END_FACE = 0.8     # см, эквивалентная торцевая стенка целлюлозы лицом на трубке
Z_EDGE_C = 9.3           # см, центр диска ребром
EDGE_H = 4.0             # см, высота коробки ребром (диаметр диска)
EDGE_PAPER = 0.75        # см, целлюлозный эквивалент трубки ребром (в смеси с кольцом)
EDGE_SIDE = 0.08         # см, боковая стенка смеси на гранях
RHO_STEEL, RHO_PAPER = 7.87, 0.7
CELL = {6: 0.4444, 1: 0.0622, 8: 0.4934}

RHOS = (2.8, 3.3, 3.8, 4.2)


def set_key(text, key, value):
    pat = re.compile(r'^(' + re.escape(key) + r'\s*=\s*)(.*?)(\r?\n)', re.M)
    m = pat.search(text)
    if not m:
        raise SystemExit('нет ключа ' + key)
    return text[:m.start(2)] + value + text[m.end(2):]


def material_block(prefix, kind, mname, rho, fractions, label):
    """kind: 'Source' | 'Wall'; label — M_<prefix>_Source / M_<prefix>_Beaker"""
    lines = ['%s_n%sElements = %d' % (prefix, kind, len(fractions)), '%s_Ro%s = %s' % (prefix, kind, fmt(rho))]
    for i, (z, w) in enumerate(sorted(fractions.items())):
        lines.append('%s_Z%s[%d] = %d' % (prefix, kind, i, z))
        lines.append('%s_Fractions%s[%d] = %s' % (prefix, kind, i, fmt(w)))
    lines.append('%s_FractionType%s = MASS' % (prefix, kind))
    lines.append('M_%s_%s.MName = %s' % (prefix, label, mname))
    lines.append('M_%s_%s.Nmaterials = 1' % (prefix, label))
    lines.append('M_%s_%s.Name[0] = %-40s' % (prefix, label, mname))
    lines.append('M_%s_%s.MatRelWeight[0] = 1' % (prefix, label))
    return '\r\n'.join(lines) + '\r\n\r\n\r\n'


def fmt(x):
    return ('%.6g' % x)


def replace_block(text, start_key, end_marker, block):
    start = text.index(start_key)
    end = text.index(end_marker, start)
    return text[:start] + block + text[end:]


def with_materials(text, source_name, source_rho, source_fr, wall_name, wall_rho, wall_fr):
    text = replace_block(text, 'SC_nWallElements', '//Source', material_block('SC', 'Wall', wall_name, wall_rho, wall_fr, 'Beaker'))
    text = replace_block(text, 'SC_nSourceElements', '// Empty space', material_block('SC', 'Source', source_name, source_rho, source_fr, 'Source'))
    return text


def composite(t_fe, t_p):
    """стенка-смесь: сталь t_fe см + целлюлоза t_p см по массовой толщине → (ρ, доли, толщина)"""
    m_fe = RHO_STEEL * t_fe
    m_p = RHO_PAPER * t_p
    t = t_fe + t_p
    rho = (m_fe + m_p) / t
    fr = {26: m_fe / (m_fe + m_p)}
    for z, w in CELL.items():
        fr[z] = fr.get(z, 0.0) + w * m_p / (m_fe + m_p)
    return rho, fr, t


def scene_contact(t, src_name, src_rho, src_fr, ring_t=RING_T):
    t = set_key(t, 'SourceType', 'CYLINDER')
    t = set_key(t, 'SC_BeakerToDetectorFrontDistance', '0 cm')
    t = set_key(t, 'SC_BeakerDiameter', '%s cm' % fmt(D_GLASS + 2 * ring_t))
    t = set_key(t, 'SC_BeakerHeight', '%s cm' % fmt(H_GLASS))
    t = set_key(t, 'SC_BeakerSideWallThickness', '%s cm' % fmt(ring_t))
    t = set_key(t, 'SC_BeakerEndWallThickness', '0 cm')
    t = set_key(t, 'SC_SourceHeight', '%s cm' % fmt(H_GLASS))
    return with_materials(t, src_name, src_rho, src_fr, 'Steel ring', RHO_STEEL, {26: 1.0})


def scene_face81(t, src_name, src_rho, src_fr):
    t = set_key(t, 'SourceType', 'CYLINDER')
    t = set_key(t, 'SC_BeakerToDetectorFrontDistance', '%s cm' % fmt(Z_FACE81 - PAPER_END_FACE))
    t = set_key(t, 'SC_BeakerDiameter', '%s cm' % fmt(D_GLASS))
    t = set_key(t, 'SC_BeakerHeight', '%s cm' % fmt(H_GLASS))
    t = set_key(t, 'SC_BeakerSideWallThickness', '0 cm')
    t = set_key(t, 'SC_BeakerEndWallThickness', '%s cm' % fmt(PAPER_END_FACE))
    t = set_key(t, 'SC_SourceHeight', '%s cm' % fmt(H_GLASS))
    return with_materials(t, src_name, src_rho, src_fr, 'Cardboard tube (equiv.)', RHO_PAPER, CELL)


def scene_edge93(t, src_name, src_rho, src_fr, ring_t=RING_T):
    rho_w, fr_w, t_w = composite(ring_t, EDGE_PAPER)
    t = set_key(t, 'SourceType', 'BOX')
    t = set_key(t, 'SB_BoxToDetectorFrontDistance', '%s cm' % fmt(Z_EDGE_C - EDGE_H / 2 - t_w))
    t = set_key(t, 'SB_SourceX', '%s cm' % fmt(D_GLASS))
    t = set_key(t, 'SB_SourceY', '%s cm' % fmt(H_GLASS))
    t = set_key(t, 'SB_BoxSideWallThickness', '%s cm' % fmt(EDGE_SIDE))
    t = set_key(t, 'SB_BoxEndWallThickness', '%s cm' % fmt(t_w))
    t = set_key(t, 'SB_SourceHeight', '%s cm' % fmt(EDGE_H))
    return with_materials(t, src_name, src_rho, src_fr, 'Steel ring + cardboard (equiv. mix)', rho_w, fr_w)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    t0 = io.open(TEMPLATE, 'rb').read().decode('cp1251')
    assert 'SourceType = CYLINDER' in t0 and 'SC_RoSource = 4.345' in t0
    scenes = []
    for rho in RHOS:
        tag = 'r%02d' % int(round(rho * 10))
        fr = glass.oxide_fractions(glass.composition(rho))
        name = 'Th glass P67 rho %.1f' % rho
        scenes.append(('AS80_p67_contact_%s' % tag, scene_contact(t0, name, rho, fr), 'contact', rho, RING_T))
        scenes.append(('AS80_p67_face81_%s' % tag, scene_face81(t0, name, rho, fr), 'face81', rho, RING_T))
        scenes.append(('AS80_p67_edge93_%s' % tag, scene_edge93(t0, name, rho, fr), 'edge93', rho, RING_T))
    # чувствительность к кольцу — ребром при 3.3
    fr = glass.oxide_fractions(glass.composition(3.3))
    for rt, tag in ((0.05, 'ring05'), (0.2, 'ring20')):
        scenes.append(('AS80_p67_edge93_r33_%s' % tag, scene_edge93(t0, 'Th glass P67 rho 3.3', 3.3, fr, ring_t=rt), 'edge93', 3.3, rt))
    # положительный контроль — состав П13 на тех же трёх моделях
    scenes.append(('AS80_p67_contact_p13', scene_contact(t0, 'Thorium glass P13', glass.RHO_P13, glass.P13), 'contact', glass.RHO_P13, RING_T))
    scenes.append(('AS80_p67_face81_p13', scene_face81(t0, 'Thorium glass P13', glass.RHO_P13, glass.P13), 'face81', glass.RHO_P13, RING_T))
    scenes.append(('AS80_p67_edge93_p13', scene_edge93(t0, 'Thorium glass P13', glass.RHO_P13, glass.P13), 'edge93', glass.RHO_P13, RING_T))
    spectrum_of = {'contact': ['contact52k', 'contact_cal0809'], 'face81': ['face81'], 'edge93': ['edge93']}
    rows = ['geometry,spectrum,preset,vessel']
    for key, text, geom, rho, rt in scenes:
        with io.open(os.path.join(out, key + '.in'), 'wb') as fh:
            fh.write(text.encode('cp1251'))
        for sp in spectrum_of[geom]:
            rows.append('%s,%s__%s,Atom Spectra Pro 80x80,"диск П67 %s ρ %.3f кольцо %.1f мм"' % (key, sp, key, geom, rho, rt * 10))
        print(key)
    with io.open(os.path.join(out, 'index.csv'), 'w', encoding='utf-8', newline='') as fh:
        fh.write('\n'.join(rows) + '\n')
    print('сцен: %d' % len(scenes))


if __name__ == '__main__':
    main()
