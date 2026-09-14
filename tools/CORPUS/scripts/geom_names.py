# -*- coding: utf-8 -*-
u"""Имена веществ геометрии: читать из `.in` и сверять с узлом `<Efficiency>`.

ЗАЧЕМ (`A269`, остаток `A161`). Склад матриц считается из `corpus/geometries/*.in`,
а разбор сверяет клеймо файла матрицы с геометрией, ЛЕЖАЩЕЙ В СПЕКТРЕ
(`matrix.IsValidFor(rd.Efficiency.Geometry)`). Имя вещества входит в отпечаток,
поэтому две записи одной сцены обязаны нести одно и то же имя. С 23.08.2026 они
несли РАЗНОЕ: в узле `<Efficiency>` имя пробы порчено знаками замены
(`����-06` вместо `ОИСН-06`), потому что тогда `.in` читался
не своей кодировкой. Коммит `ed398e09` (05.09.2026) починил чтение `.in`, а
замороженный в спектре узел остался порченым — и 37 корпусных спектров из 129
перестали получать матрицу.

Здесь лежит общее для двух потребителей:

* `restore_eff_nodes.py` — шаг 2/4 пересборки; он тянет узел ЦЕЛИКОМ ИЗ GIT и
  потому обязан восстановить имена ПОСЛЕ вставки, иначе правка умирает на первой
  же пересборке;
* `tools/check_geometry_names.py` — сторож, который отказывает, если имя в узле
  разошлось с `.in`.

⛔ Правится ТОЛЬКО имя вещества. Размеры, плотности и доли из `.in` сюда не
переносятся: расхождение в них — другая болезнь и другое решение.
"""
import io
import os
import re

#: Материалы геометрии: тег в XML узла -> ключ имени в `.in`.
#: Соответствие взято из `GeometryModel.Load` (`BecquerelMonitor/EfficiencyMaker`),
#: а не придумано: там же выбирается приставка источника.
FIXED_SLOTS = (
    (u'Crystal',    u'M_DS_Crystal.MName'),
    (u'Reflector',  u'M_DS_Reflector.MName'),
    (u'Cladding',   u'M_DS_Crystal_Cladding.MName'),
)

#: Стакан и проба живут под разными приставками у цилиндра и у Маринелли.
#: `GeometryModel.Load`: `prefix = SourceType == Marinelli ? "SM_" : "SC_"`.
SOURCE_SLOTS = (
    (u'BeakerWall', u'M_%sBeaker.MName'),
    (u'Source',     u'M_%sSource.MName'),
)

_LINE = re.compile(r'^\s*([A-Za-z0-9_.\[\]]+)\s*=\s*(.*)$')

NODE = re.compile(r'<Efficiency>\s*<Guid>.*</Efficiency>', re.S)
GEOM_NAME = re.compile(r'<Geometry><Name>([^<]*)</Name>')
SOURCE_TYPE = re.compile(r'<SourceType>([^<]*)</SourceType>')


def read_in(path):
    u"""Ключ -> значение из файла `.in`, ЕГО кодировкой.

    Распознавание повторяет `GeometryModel.Detect`: признак порядка байтов, потом
    строгий разбор как UTF-8, и лишь при отказе — 1251. Обратить порядок нельзя:
    1251 принимает любые байты, и вопрос «а не 1251 ли это» ответа не имеет.
    """
    with open(path, 'rb') as fh:
        data = fh.read()
    if data[:3] == b'\xef\xbb\xbf':
        text = data[3:].decode('utf-8')
    else:
        try:
            text = data.decode('utf-8')
        except UnicodeDecodeError:
            text = data.decode('cp1251')
    kv = {}
    for raw in text.replace('\r\n', '\n').replace('\r', '\n').split('\n'):
        cut = raw.find('//')
        if cut >= 0:
            raw = raw[:cut]
        m = _LINE.match(raw)
        if m:
            kv[m.group(1).lower()] = m.group(2)
    return kv


def slots_of(node):
    u"""Пары (тег XML, ключ `.in`) для этого узла; приставка — по типу источника."""
    m = SOURCE_TYPE.search(node)
    prefix = u'SM_' if (m and m.group(1) == u'Marinelli') else u'SC_'
    return list(FIXED_SLOTS) + [(tag, key % prefix) for tag, key in SOURCE_SLOTS]


def compare(node, geom_dir):
    u"""Что в узле разошлось с `.in`.

    Возвращает `(имя геометрии, расхождения, отказ)`, где расхождение —
    `(тег, имя в узле, имя в .in)`, а отказ — строка причины, по которой сверить
    не удалось (`None`, если удалось).
    """
    gm = GEOM_NAME.search(node)
    if gm is None:
        return None, [], None            # узел без геометрии — сверять нечего
    gname = gm.group(1)
    path = os.path.join(geom_dir, gname + u'.in')
    if not os.path.isfile(path):
        return gname, [], u'файла geometries/%s.in нет' % gname
    kv = read_in(path)

    diffs = []
    for tag, key in slots_of(node):
        m = re.search(r'<%s><Name>([^<]*)</Name>' % tag, node)
        if m is None:
            continue                     # вещества этого слоя в узле нет
        have = m.group(1)
        want = kv.get(key.lower(), u'').strip()
        if want == u'':
            # Пустым именем чинить нельзя: это стёрло бы то, что есть.
            if have != u'':
                return gname, diffs, u'в %s.in нет ключа %s, а в узле имя «%s»' % (
                    gname, key, have)
            continue
        if have != want:
            diffs.append((tag, have, want))
    return gname, diffs, None


def repair(node, geom_dir):
    u"""Узел с именами веществ из `.in`. Возвращает `(узел, расхождения, отказ)`."""
    gname, diffs, refusal = compare(node, geom_dir)
    if refusal is not None or not diffs:
        return node, diffs, refusal
    for tag, have, want in diffs:
        pat = re.compile(r'(<%s><Name>)([^<]*)(</Name>)' % tag)
        node, count = pat.subn(lambda m: m.group(1) + want + m.group(3), node, count=1)
        if count != 1:
            return node, diffs, u'не удалось заменить имя вещества %s у %s' % (tag, gname)
    return node, diffs, None


def node_of(text):
    u"""Узел `<Efficiency>` целиком, или `None`."""
    m = NODE.search(text)
    return m.group(0) if m is not None else None


def read_spectrum(path):
    return io.open(path, encoding='utf-8-sig', newline='').read()


def has_bom(path):
    u"""Был ли у файла признак порядка байтов.

    Спрашивается затем, чтобы запись его НЕ ЗАВОДИЛА. Правка одного имени не
    имеет права менять оболочку файла: в git все 129 спектров лежат без BOM, и
    файл, у которого он появился, отличается от соседа не только по существу.
    Поймано 06.09.2026 счётом байтов: правка сняла 4 байта имени и добавила 3
    байта BOM, итог −1 вместо −4 — по коду возврата этого не видно вовсе.
    """
    with open(path, 'rb') as fh:
        return fh.read(3) == b'\xef\xbb\xbf'


def write_spectrum(path, text, bom):
    u"""Записать спектр: UTF-8, переводы строк не трогаются (`newline=''`),
    признак порядка байтов — такой же, каким был."""
    with io.open(path, 'w', encoding='utf-8', newline='') as fh:
        fh.write((u'﻿' if bom else u'') + text)
