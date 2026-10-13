# -*- coding: utf-8 -*-
r"""Читатель блока `ANGK` (Q_k угловой корреляции) файла матрицы формата 9 — ТОЛЬКО ЧТЕНИЕ
(П87, 16.09.2026, `AMBER46`). Независим от кода приложения: раскладка — по
`ResponseMatrix.Save`: `BQRM`, int32 формат, строка клейма (7-битная длина + UTF-8),
double BinKev, int32 Histories, int64 ticks, double BuildSeconds, блок настроек
(2 double, int32, double, int32, 8 bool, int32 зерно = 44 байта), тело (int32 узлов,
узлы double, int32 каналов, на канал и узел int32 длина + float[]), затем `ANGK`,
int32 узлов блока и на узел 10 double + int64:
  Q2 Q4 dQ2 dQ4 Q2T Q4T dQ2T dQ4T eps_peak eps_total histories.
Формат 8 (без блока) читается тоже — печатается «блока нет».

  python rmx_qk.py <файл.rmx> [--csv=<выход.csv>] [--at=E1,E2,...]   (интерполяция по ln E)
Разделитель дробной части — точка.
"""
import io
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


def read(path):
    b = open(path, 'rb').read()
    assert b[:4] == b'BQRM', 'не наш файл'
    fmt = struct.unpack_from('<i', b, 4)[0]
    stamp, pos = read_string(b, 8)
    pos += 8 + 4 + 8 + 8          # BinKev, Histories, ticks, BuildSeconds
    pos += 44                     # блок настроек
    body_start = pos
    nodes = struct.unpack_from('<i', b, pos)[0]
    pos += 4
    energies = list(struct.unpack_from('<%dd' % nodes, b, pos))
    pos += 8 * nodes
    channels = struct.unpack_from('<i', b, pos)[0]
    pos += 4
    for c in range(channels):
        for i in range(nodes):
            length = struct.unpack_from('<i', b, pos)[0]
            pos += 4 + 4 * length
    body_end = pos
    import hashlib
    out = {'format': fmt, 'stamp': stamp, 'energies': energies, 'nodes': nodes, 'body_end': body_end, 'qk': None,
           'body_sha': hashlib.sha256(b[body_start:body_end]).hexdigest()}
    if fmt >= 9:
        assert b[pos:pos + 4] == b'ANGK', 'формат %d: за телом нет ANGK' % fmt
        pos += 4
        count = struct.unpack_from('<i', b, pos)[0]
        pos += 4
        rows = []
        for i in range(count):
            vals = struct.unpack_from('<10dq', b, pos)
            pos += 88
            rows.append(vals)
        out['qk'] = rows
    return out


def interp(energies, values, e):
    n = len(energies)
    if e <= energies[0]:
        return values[0]
    if e >= energies[-1]:
        return values[-1]
    i = 1
    while i < n - 1 and energies[i] < e:
        i += 1
    x0, x1 = math.log(energies[i - 1]), math.log(energies[i])
    t = (math.log(e) - x0) / (x1 - x0) if x1 > x0 else 0.0
    return values[i - 1] + (values[i] - values[i - 1]) * t


def main(argv):
    path = argv[0]
    csv = None
    at = []
    for a in argv[1:]:
        if a.startswith('--csv='):
            csv = a[6:]
        elif a.startswith('--at='):
            at = [float(x) for x in a[5:].split(',') if x]
    m = read(path)
    print(path)
    print('  format %d  stamp %s  nodes %d' % (m['format'], m['stamp'], m['nodes']))
    if m['qk'] is None:
        print('  блока ANGK нет (формат %d)' % m['format'])
        return 0
    rows = m['qk']
    print('  блок ANGK: узлов %d' % len(rows))
    print('  %8s %9s %8s %9s %8s %9s %9s %11s %11s %9s' % ('E', 'Q2', 'dQ2', 'Q4', 'dQ4', 'Q2T', 'Q4T', 'eps_peak', 'eps_total', 'hist'))
    for e, r in zip(m['energies'], rows):
        print('  %8.3f %9.5f %8.5f %9.5f %8.5f %9.5f %9.5f %11.5g %11.5g %9d' % (e, r[0], r[2], r[1], r[3], r[4], r[5], r[8], r[9], r[10]))
    if csv:
        with io.open(csv, 'w', encoding='utf-8', newline='') as f:
            f.write('energy_kev,q2,q4,dq2,dq4,q2t,q4t,dq2t,dq4t,eps_peak,eps_total,histories\n')
            for e, r in zip(m['energies'], rows):
                f.write('%r,%r,%r,%r,%r,%r,%r,%r,%r,%r,%r,%d\n' % ((e,) + tuple(r)))
        print('  csv: %s' % csv)
    if at:
        es = m['energies']
        q2 = [r[0] for r in rows]
        q4 = [r[1] for r in rows]
        q2t = [r[4] for r in rows]
        q4t = [r[5] for r in rows]
        for e in at:
            print('  при %.1f кэВ (ln E): Q2 %.5f  Q4 %.5f  Q2T %.5f  Q4T %.5f' % (
                e, interp(es, q2, e), interp(es, q4, e), interp(es, q2t, e), interp(es, q4t, e)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
