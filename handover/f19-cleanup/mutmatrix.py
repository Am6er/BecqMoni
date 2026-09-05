# -*- coding: utf-8 -*-
"""Что ловит `split_corpus.py --check` и что из этого ловит `check_corpus.check_parts`."""
import csv, io, os, shutil, subprocess, sys

PARTS = os.path.join('corpus', 'parts.csv')
ORIG = PARTS + '.orig'
FIELDS = ['spectrum', 'det', 'part', 'geometry', 'why']

if not os.path.exists(ORIG):
    shutil.copyfile(PARTS, ORIG)


def read():
    with io.open(ORIG, encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))


def write(rows):
    with io.open(PARTS, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=FIELDS)
        w.writeheader()
        w.writerows([{k: r[k] for k in FIELDS} for r in rows])


def idx(rows, key):
    return [i for i, r in enumerate(rows) if r['spectrum'] == key][0]


def m_none(rows):
    return rows


def m_why(rows):
    rows[idx(rows, 'RC103_K40')]['why'] = 'заметка человека, скрипт такой не строит'
    return rows


def m_det(rows):
    rows[3]['det'] = 'ASN3'
    return rows


def m_part_known_to_unknown(rows):
    i = [j for j, r in enumerate(rows) if r['part'] == 'known'][0]
    rows[i]['part'] = 'unknown'
    rows[i]['geometry'] = ''
    rows[i]['why'] = 'форма и положение пробы не записаны нигде'
    return rows


def m_geom_swap(rows):
    ks = [j for j, r in enumerate(rows) if r['part'] == 'known']
    a, b = ks[0], ks[1]
    rows[a]['geometry'], rows[b]['geometry'] = rows[b]['geometry'], rows[a]['geometry']
    return rows


def m_geom_missing(rows):
    i = [j for j, r in enumerate(rows) if r['part'] == 'known'][0]
    rows[i]['geometry'] = 'НЕТ_ТАКОЙ_ГЕОМЕТРИИ'
    return rows


def m_drop(rows):
    del rows[10]
    return rows


def m_extra(rows):
    e = dict(rows[10])
    e['spectrum'] = 'ВЫДУМАННЫЙ_СПЕКТР'
    rows.append(e)
    return rows


def m_order(rows):
    rows[0], rows[1] = rows[1], rows[0]
    return rows


def m_excluded(rows):
    i = [j for j, r in enumerate(rows) if r['part'] == 'unknown'][0]
    rows[i]['part'] = 'excluded'
    rows[i]['why'] = 'германий — вне работы по приказу Amber 08.08.2026'
    return rows


CASES = [
    (u'0. без подмены (как лежит в дереве)', m_none),
    (u'1. why подменён у другой строки', m_why),
    (u'2. det подменён', m_det),
    (u'3. known -> unknown, геометрия снята', m_part_known_to_unknown),
    (u'4. две known обменялись геометриями', m_geom_swap),
    (u'5. геометрия названа несуществующая', m_geom_missing),
    (u'6. строка удалена', m_drop),
    (u'7. лишняя строка (спектра нет в манифесте)', m_extra),
    (u'8. порядок двух строк переставлен', m_order),
    (u'9. unknown -> excluded (не германий)', m_excluded),
]

SPLIT = os.path.join('scripts', 'split_corpus.py')
PARTS_RUNNER = (
    'import sys; sys.path.insert(0,"scripts"); import check_corpus; '
    'sys.exit(0 if check_corpus.check_parts() else 1)')

print(u'%-46s %-14s %s' % (u'подмена', u'--check', u'check_corpus.check_parts'))
print(u'-' * 92)
for name, fn in CASES:
    write(fn(read()))
    a = subprocess.run([sys.executable, SPLIT, '--check'],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    b = subprocess.run([sys.executable, '-c', PARTS_RUNNER],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    print(u'%-46s %-14s %s' % (
        name,
        u'ОТКАЗ %d' % a.returncode if a.returncode else u'молчит 0',
        u'ОТКАЗ 1' if b.returncode else u'молчит 0'))
shutil.copyfile(ORIG, PARTS)
print(u'\nparts.csv возвращён на место')
