# -*- coding: utf-8 -*-
"""Клейма и статистика матриц отклика — ЧТЕНИЕМ ФАЙЛОВ, а не ключей прогона.
F37, 05.09.2026 (`S132`). Ничего не пишет.

    python handover/f37-s132/rmx_stamps.py tools/CORPUS/corpus/geometries

Зачем отдельный разборщик. Клеймо матрицы устроено как `phys=<версия>;<sha256>`
— из его ТЕКСТА видно только версию физики, всё остальное свёрнуто в хеш. Числа
приходится брать из полей самого файла:

  * `Histories` (после клейма) — НОМИНАЛ историй на узел, от которого считаются
    пилот (1/10) и потолок (x8); именно он входит в отпечаток;
  * хвост `NOIS` — `HistoriesSpent` (ПОТРАЧЕНО) и `HistoriesWorstNode`.

Путать эти два числа нельзя: при догонке по шуму потраченное отличается от
номинала в разы, а при плоском счёте равно `узлы x номинал` — по чему плоский
прогон и узнаётся, потому что сам режим (`ContinuumErrorTarget`) не пишется ни
в файл, ни в клеймо.
"""
import collections
import datetime
import glob
import io
import os
import struct
import sys


def read7(b, i):
    """Длина строки у BinaryWriter — 7-битами с продолжением."""
    v = 0
    s = 0
    while True:
        c = b[i]
        i += 1
        v |= (c & 0x7F) << s
        if not (c & 0x80):
            break
        s += 7
    return v, i


def parse(path):
    b = open(path, 'rb').read()
    if b[:4] != b'BQRM':
        raise ValueError('не матрица отклика: ' + path)
    i = 4
    fmt, = struct.unpack_from('<i', b, i)
    i += 4
    n, i = read7(b, i)
    stamp = b[i:i + n].decode('utf-8')
    i += n
    binkev, = struct.unpack_from('<d', b, i)
    i += 8
    hist, = struct.unpack_from('<i', b, i)
    i += 4
    ticks, = struct.unpack_from('<q', b, i)
    i += 8
    secs, = struct.unpack_from('<d', b, i)

    # Хвост `NOIS` ищется по метке, а годным считается ПОСЛЕДНЕЕ осмысленное
    # вхождение: четыре тех же байта могут случайно лечь и в тело матрицы.
    spent = worst = cwe = None
    pos = -1
    while True:
        pos = b.find(b'NOIS', pos + 1)
        if pos < 0:
            break
        try:
            cre, cw = struct.unpack_from('<dd', b, pos + 4)
            sp, wo = struct.unpack_from('<qq', b, pos + 20)
        except struct.error:
            continue
        if 0.0 <= cre < 1000.0 and 0.0 <= cw < 1000.0 and 0 < sp < 10 ** 13 and 0 < wo <= sp:
            spent, worst, cwe = sp, wo, cw

    created = datetime.datetime(1, 1, 1) + datetime.timedelta(microseconds=ticks // 10)
    return dict(fmt=fmt, stamp=stamp, bin=binkev, hist=hist, spent=spent,
                worst=worst, cwe=cwe, secs=secs, created=created,
                size=os.path.getsize(path))


def phys(stamp):
    for part in stamp.split(';'):
        if part.startswith('phys='):
            return part[5:]
    return '?'


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    d = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        'tools', 'CORPUS', 'corpus', 'geometries')
    rows = []
    for p in sorted(glob.glob(os.path.join(d, '*.rmx'))):
        r = parse(p)
        r['key'] = os.path.basename(p)[:-4]
        rows.append(r)
    if not rows:
        print('матриц в %s нет' % d)
        return 2

    print('файлов: %d в %s' % (len(rows), d))
    groups = collections.Counter()
    for r in rows:
        nodes = (r['spent'] or 0) // (r['worst'] or 1)
        groups[('phys=' + phys(r['stamp']), 'формат %d' % r['fmt'],
                'номинал %d' % r['hist'], 'бин %.1f' % r['bin'],
                'узлов %d' % nodes,
                'сетка ' + ('своя' if 'grid=' in r['stamp'] else 'штатная'),
                'счёт ' + ('плоский' if r['spent'] == nodes * r['hist'] else 'с догонкой'))] += 1

    print('\n== сводка: физика — из клейма, остальное — поля файла ==')
    for k, v in sorted(groups.items(), key=lambda x: -x[1]):
        print('   %s   -> файлов %d' % (', '.join(k), v))

    print('\n== по файлам ==')
    print('%-42s %-4s %-11s %-13s %-12s %-7s %-7s %s' % (
        'ключ', 'фмт', 'номинал', 'потрачено', 'худший узел', 'шум%', 'сек', 'создан, UTC'))
    for r in rows:
        print('%-42s %-4d %-11d %-13s %-12s %-7s %-7.0f %s' % (
            r['key'], r['fmt'], r['hist'],
            r['spent'] if r['spent'] else '-',
            r['worst'] if r['worst'] else '-',
            ('%.2f' % r['cwe']) if r['cwe'] is not None else '-',
            r['secs'], r['created'].strftime('%Y-%m-%d %H:%M')))

    tot = sum(r['secs'] for r in rows)
    print('\nсчёт: сумма %.0f с (%.2f ч), медиана %.0f с' % (
        tot, tot / 3600.0, sorted(r['secs'] for r in rows)[len(rows) // 2]))
    versions = collections.Counter(phys(r['stamp']) for r in rows)
    print('версии физики: ' + ', '.join('%s у %d' % (k, v) for k, v in sorted(versions.items())))
    return 0


if __name__ == '__main__':
    sys.exit(main())
