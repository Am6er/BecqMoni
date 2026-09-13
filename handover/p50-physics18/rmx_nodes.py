# -*- coding: utf-8 -*-
"""Пик и сумма строки матрицы по ВЫБРАННЫМ узлам — ТОЛЬКО ЧТЕНИЕ (П37, 13.09.2026).

`MatrixDiffProbe` печатает медиану и худший узел по всем 140; здесь — конкретные
узлы (ближайшие к заданным кэВ) двух файлов рядом: эффективность пика (последний
бин суммы каналов), сумма строки (полная), доля пика, и сдвиг B относительно A в
процентах. Раскладка тела — по `ResponseMatrix.Save`/`Load` (формат 8): шапка
(`BQRM`, формат, клеймо, бин, истории, время, секунды), блок параметров
(`WriteOptions`: 2 double, int32, double, int32, 8 bool, int32), затем узлы,
энергии, число каналов и строки `[канал][узел]` как int32 длина + float32.
Разделитель дробной части — точка.

  python rmx_nodes.py <A.rmx> <B.rmx> [кэВ,кэВ,...]   (умолчание 32,59.5,662,1461,2614)
"""
import struct
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def read_string(b, pos):
    n, shift = 0, 0
    while True:
        c = b[pos]
        pos += 1
        n |= (c & 0x7F) << shift
        if c < 0x80:
            break
        shift += 7
    return b[pos:pos + n].decode('utf-8'), pos + n


def load(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'BQRM', path
    fmt = struct.unpack_from('<i', b, 4)[0]
    assert fmt == 8, (path, fmt)
    stamp, p = read_string(b, 8)
    p += 8 + 4 + 8 + 8                     # BinKev, Histories, CreatedUtc, BuildSeconds
    p += 8 + 8 + 4 + 8 + 4 + 8 + 4         # WriteOptions: 2 double, int, double, int, 8 bool, int
    nodes = struct.unpack_from('<i', b, p)[0]
    p += 4
    energies = struct.unpack_from('<%dd' % nodes, b, p)
    p += 8 * nodes
    channels = struct.unpack_from('<i', b, p)[0]
    p += 4
    totals = [None] * nodes
    for c in range(channels):
        for i in range(nodes):
            n = struct.unpack_from('<i', b, p)[0]
            p += 4
            row = struct.unpack_from('<%df' % n, b, p)
            p += 4 * n
            if totals[i] is None:
                totals[i] = [0.0] * n
            elif len(totals[i]) < n:
                totals[i] = totals[i] + [0.0] * (n - len(totals[i]))
            for k, v in enumerate(row):
                totals[i][k] += v
    return stamp, energies, totals


def main(argv):
    a, bpath = argv[1], argv[2]
    want = [float(x) for x in (argv[3].split(',') if len(argv) > 3 else ['32', '59.5', '662', '1461', '2614'])]
    sa, ea, ta = load(a)
    sb, eb, tb = load(bpath)
    print('A: %s  %s' % (a, sa))
    print('B: %s  %s' % (bpath, sb))
    print('%9s %5s %12s %12s %8s %12s %12s %8s %8s %8s' % (
        'кэВ', 'узел', 'пик A', 'пик B', 'Δпик,%', 'сумма A', 'сумма B', 'Δсум,%', 'доляA', 'доляB'))
    for w in want:
        i = min(range(len(ea)), key=lambda k: abs(ea[k] - w))
        ra, rb = ta[i], tb[i]
        if not ra or not rb:
            print('%9.3f %5d  (пустой узел)' % (ea[i], i))
            continue
        pa, pb = ra[-1], rb[-1]
        s1, s2 = sum(ra), sum(rb)
        print('%9.3f %5d %12.6e %12.6e %+8.3f %12.6e %12.6e %+8.3f %8.4f %8.4f' % (
            ea[i], i, pa, pb, 100.0 * (pb - pa) / pa if pa else 0.0, s1, s2,
            100.0 * (s2 - s1) / s1 if s1 else 0.0, pa / s1 if s1 else 0.0, pb / s2 if s2 else 0.0))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
