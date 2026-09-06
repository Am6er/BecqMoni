# -*- coding: utf-8 -*-
u"""S142: что стоит за ПУСТОЙ клеткой `materials.csv` у части `known`.

Считается по рабочим копиям корпуса: у каждого спектра вынимается встроенный
узел `<Geometry>` и печатается, какие элементы даёт КРИСТАЛЛ и ПРОБА при
пороге массовой доли 1 % — том самом, с которым их читает потребитель
(`CorpusFsaProbe.SpecOf`: `DescribeCrystal(geometry.Crystal, 0.01)` и
`HeavyElementsOf(geometry.Source, 0.01, …)`).

Вопрос строки был: «пустая клетка = сцена молча считается пустой». Ответ здесь
числом: сколько спектров `known` получают из геометрии кристалл и сколько —
непустую пробу, и есть ли в геометрии хоть один блок защиты.
"""
import csv
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
CORPUS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus')

SYM = {1: 'H', 6: 'C', 7: 'N', 8: 'O', 9: 'F', 11: 'Na', 12: 'Mg', 13: 'Al',
       17: 'Cl', 19: 'K', 32: 'Ge', 53: 'I', 55: 'Cs', 71: 'Lu'}


def rd(name):
    with io.open(os.path.join(CORPUS, name), encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def block(geom, tag, minfrac=0.01):
    node = geom.find(tag)
    if node is None:
        return None, []
    name = node.findtext('Name') or ''
    els = [(int(e.get('Z')), float(e.get('Fraction')))
           for e in node.findall('Fractions/Element')]
    return name, [z for z, f in els if f >= minfrac]


parts = {r['spectrum']: r for r in rd('parts.csv')}
mats = {r['spectrum']: r for r in rd('materials.csv')}

hdr = '%-22s %-9s %-24s %-22s %s'
print(hdr % ('спектр', 'часть', 'кристалл геометрии', 'проба геометрии', 'клетки csv'))
n_known = n_crystal = n_sample_heavy = n_sample_any = 0
shield_blocks = 0
tags_seen = set()
for key in sorted(parts):
    p = parts[key]
    if p['part'] != 'known':
        continue
    n_known += 1
    path = os.path.join(CORPUS, 'spectra', key + '.xml')
    root = ET.parse(path).getroot()
    geom = root.find('.//Efficiency/Geometry')
    if geom is None:
        print(hdr % (key, p['part'], 'НЕТ УЗЛА GEOMETRY', '', ''))
        continue
    tags_seen.update(c.tag for c in geom if c.find('Fractions') is not None)
    cname, cz = block(geom, 'Crystal')
    sname, sz = block(geom, 'Source')
    if cz:
        n_crystal += 1
    if sz:
        n_sample_any += 1
    heavy = [z for z in sz if z >= 40]
    if heavy:
        n_sample_heavy += 1
    m = mats[key]
    cells = '/'.join(x or '-' for x in (m['crystal'], m['sample'], m['shield']))
    print(hdr % (key, p['part'],
                 '%s %s' % (cname, [SYM.get(z, z) for z in cz]),
                 '%s %s' % (sname, [SYM.get(z, z) for z in sz]),
                 cells))

print()
print(u'known всего: %d' % n_known)
print(u'  кристалл из геометрии непуст:      %d' % n_crystal)
print(u'  проба из геометрии непуста:        %d' % n_sample_any)
print(u'  проба содержит элемент Z>=40:      %d' % n_sample_heavy)
print(u'  блоки вещества в узле <Geometry>:  %s' % ', '.join(sorted(tags_seen)))
print(u'  блока ЗАЩИТЫ среди них:            %s'
      % ('НЕТ' if not (tags_seen & {'Shield', 'Shielding', 'Castle'}) else 'есть'))
