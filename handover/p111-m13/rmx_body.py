# -*- coding: utf-8 -*-
r"""Читатель ТЕЛА файла матрицы отклика (формат 8/9) — ТОЛЬКО ЧТЕНИЕ, независим от кода
приложения (П97, 18.09.2026; раскладка — по `ResponseMatrix.Save`, как `rmx_qk.py` П87):
`BQRM`, int32 формат, строка клейма (7-битная длина + UTF-8), double BinKev, int32 Histories,
int64 ticks, double BuildSeconds, блок настроек 44 байта, тело: int32 узлов, узлы double,
int32 каналов, на КАЖДЫЙ канал и узел int32 длина + float[длина] (бины по BinKev от нуля).

Даёт по узлу отклик суммой каналов и по полосам энергии (доля от суммы строки):
пик (последний бин строки — полное поглощение по построению бина пика), четверти ниже пика
[0..25 %E), [25..50), [50..75), [75..100 %E без бина пика), 0–100 кэВ. Сравнение двух файлов —
`compare(a, b, energies)`: Δ % по полосам у узлов, ближайших к названным энергиям.

  python rmx_body.py <A.rmx> <B.rmx> [--at=662,1461,2614]
"""
import hashlib
import math
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


def read(path, rows=True):
    b = open(path, 'rb').read()
    assert b[:4] == b'BQRM', 'не наш файл'
    fmt = struct.unpack_from('<i', b, 4)[0]
    stamp, pos = read_string(b, 8)
    bin_kev = struct.unpack_from('<d', b, pos)[0]
    pos += 8
    histories = struct.unpack_from('<i', b, pos)[0]
    pos += 4 + 8 + 8
    pos += 44
    body_start = pos
    nodes = struct.unpack_from('<i', b, pos)[0]
    pos += 4
    energies = list(struct.unpack_from('<%dd' % nodes, b, pos))
    pos += 8 * nodes
    channels = struct.unpack_from('<i', b, pos)[0]
    pos += 4
    data = [[None] * nodes for _ in range(channels)]
    for c in range(channels):
        for i in range(nodes):
            length = struct.unpack_from('<i', b, pos)[0]
            pos += 4
            if rows:
                data[c][i] = struct.unpack_from('<%df' % length, b, pos)
            pos += 4 * length
    body_end = pos
    return {'format': fmt, 'stamp': stamp, 'bin': bin_kev, 'histories': histories, 'nodes': nodes,
            'energies': energies, 'channels': channels, 'rows': data if rows else None,
            'body_sha': hashlib.sha256(b[body_start:body_end]).hexdigest()}


def total_row(m, i):
    n = max(len(m['rows'][c][i]) for c in range(m['channels']))
    out = [0.0] * n
    for c in range(m['channels']):
        r = m['rows'][c][i]
        for k in range(len(r)):
            out[k] += r[k]
    return out


BANDS = ('peak', 'q0', 'q1', 'q2', 'q3', 'k0_100', 'sum')


def bands(row, e, bin_kev):
    s = sum(row)
    n = len(row)
    peak = row[n - 1] if n else 0.0
    # бин k покрывает [k*bin, (k+1)*bin); четверти — по энергии относительно E, без бина пика
    q = [0.0, 0.0, 0.0, 0.0]
    k100 = 0.0
    for k in range(n - 1):
        lo = k * bin_kev
        frac = lo / e if e > 0 else 0.0
        j = min(3, int(frac * 4))
        q[j] += row[k]
        if lo < 100.0:
            k100 += row[k]
    return {'peak': peak, 'q0': q[0], 'q1': q[1], 'q2': q[2], 'q3': q[3], 'k0_100': k100, 'sum': s}


def nearest(energies, e):
    return min(range(len(energies)), key=lambda i: abs(energies[i] - e))


def compare(a, b, at):
    out = []
    for e in at:
        i = nearest(a['energies'], e)
        ra = bands(total_row(a, i), a['energies'][i], a['bin'])
        rb = bands(total_row(b, i), b['energies'][i], b['bin'])
        d = {}
        for k in BANDS:
            d[k] = (rb[k] / ra[k] - 1.0) * 100.0 if ra[k] > 0 else float('nan')
        out.append((a['energies'][i], ra, rb, d))
    return out


def main(argv):
    at = [662.0, 1461.0, 2614.0]
    files = []
    for x in argv:
        if x.startswith('--at='):
            at = [float(v) for v in x[5:].split(',') if v]
        else:
            files.append(x)
    a = read(files[0])
    b = read(files[1])
    print('A: %s  формат %d  %s  узлов %d  каналов %d  bin %g' % (files[0], a['format'], a['stamp'][:24], a['nodes'], a['channels'], a['bin']))
    print('B: %s  формат %d  %s  узлов %d  каналов %d  bin %g' % (files[1], b['format'], b['stamp'][:24], b['nodes'], b['channels'], b['bin']))
    print('тело: %s' % ('ПОБИТОВО' if a['body_sha'] == b['body_sha'] else 'РАЗОШЛОСЬ'))
    print('%9s %10s %8s %8s %8s %8s %8s %8s' % ('E', 'полоса', 'пик', 'q0', 'q1', 'q2', 'q3', '0-100'))
    for e, ra, rb, d in compare(a, b, at):
        print('%9.1f %10s %8.4f %8.4f %8.4f %8.4f %8.4f %8.4f' % (e, 'A доля', ra['peak'] / ra['sum'], ra['q0'] / ra['sum'], ra['q1'] / ra['sum'], ra['q2'] / ra['sum'], ra['q3'] / ra['sum'], ra['k0_100'] / ra['sum']))
        print('%9s %10s %+8.2f %+8.2f %+8.2f %+8.2f %+8.2f %+8.2f   сумма %+.2f %%' % ('', 'B/A-1 %', d['peak'], d['q0'], d['q1'], d['q2'], d['q3'], d['k0_100'], d['sum']))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
