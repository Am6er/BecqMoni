# -*- coding: utf-8 -*-
"""Читатель хвостов файла матрицы `.rmx` — ТОЛЬКО ЧТЕНИЕ (П37, 13.09.2026; П50 — хвосты физики 18).

Печатает клеймо из шапки и ключи физики из хвостов `OPTF` (пара, rayl2, …),
`PKBN`/`XRKL`/`KDIP`/`LETA`/`NORM`/`LBIN`/`PKCH`/`LYSP`/`ETRN` и отпечаток тела
`BODY` — то, чего `MatrixDiffProbe.Describe` не печатает (у него узлы, бин,
истории, зерно). Нужен, чтобы сказать «клеймо отличается ТОЛЬКО phys=»
числом, а не верой: тело тождественно + все ключи тождественны = в тексте
клейма (`ResponseMatrix.ComputeStamp`) остаётся одно отличие, `phys=`.

Раскладка — по `ResponseMatrix.Save`: после блока `NOIS` идёт `OPTF` + 7 bool,
`BODY` + 32 байта, `PKWT` + bool, `RLFL` + double + bool, `JNTK` (переменной
длины) и дальше ФИКСИРОВАННЫЙ хвост из 66 байт: `JNTH` int32, `PKBN` bool,
`XRKL` bool, `KDIP` int32, `LETA` double, `NORM` byte, `LBIN` bool, `PKCH` bool,
`LYSP` int32, `ETRN` bool; с 13.09.2026 дальше `IMPS` bool (П41), `ECMP` bool и
`BPTH` int32 (П44) — читатель расширен П50 (физика 18). Разделитель дробной части — точка.

  python rmx_tails.py <файл.rmx> [...]
"""
import io
import struct
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def read_string(b, pos):
    # BinaryWriter.Write(string): 7-битная длина + UTF-8
    n, shift = 0, 0
    while True:
        c = b[pos]
        pos += 1
        n |= (c & 0x7F) << shift
        if c < 0x80:
            break
        shift += 7
    return b[pos:pos + n].decode('utf-8'), pos + n


def tails(path):
    b = open(path, 'rb').read()
    out = {}
    if b[:4] != b'BQRM':
        return {'error': 'не BQRM'}
    fmt = struct.unpack_from('<i', b, 4)[0]
    stamp, _ = read_string(b, 8)
    out['format'] = fmt
    out['stamp'] = stamp
    o = b.rfind(b'OPTF')
    if o < 0 or b[o + 4 + 7:o + 4 + 7 + 4] != b'BODY':
        out['error'] = 'OPTF/BODY не найдены'
        return out
    names = ['pairth', 'e+tr', 'e+off', 'rayl2', 'cone', 'lx', 'klcasc']
    for i, n in enumerate(names):
        out[n] = b[o + 4 + i]
    out['body'] = b[o + 4 + 7 + 4:o + 4 + 7 + 4 + 32].hex()
    p = o + 4 + 7 + 4 + 32
    if b[p:p + 4] == b'PKWT':
        out['peakw'] = b[p + 4]
        p += 5
    if b[p:p + 4] == b'RLFL':
        out['roul'], out['fluo'] = struct.unpack_from('<d?', b, p + 4)
    # Хвосты после JNTK — в фиксированном порядке, но у файла старше ключа
    # хвоста нет (склад физики 16 от 12.09 00:xx кончается на NORM). Поэтому
    # снимаются С КОНЦА, от младшего к старшему: есть метка на месте — снять,
    # нет — этого хвоста у файла не было, идём к старшему.
    end = len(b)
    for tag, fmt_, size in (('BPTH', '<i', 4), ('ECMP', '<?', 1), ('IMPS', '<?', 1),
                            ('ETRN', '<?', 1), ('LYSP', '<i', 4), ('PKCH', '<?', 1),
                            ('LBIN', '<?', 1), ('NORM', '<B', 1), ('LETA', '<d', 8),
                            ('KDIP', '<i', 4), ('XRKL', '<?', 1), ('PKBN', '<?', 1),
                            ('JNTH', '<i', 4)):
        q = end - size - 4
        if q >= 0 and b[q:q + 4] == tag.encode('ascii'):
            out[tag] = struct.unpack_from(fmt_, b, q + 4)[0]
            end = q
    out['tail_ends_at'] = end
    return out


def main(argv):
    for path in argv[1:]:
        t = tails(path)
        print(path)
        for k in ('format', 'stamp', 'body', 'pairth', 'e+tr', 'e+off', 'rayl2', 'cone', 'lx', 'klcasc',
                  'peakw', 'roul', 'fluo', 'JNTH', 'PKBN', 'XRKL', 'KDIP', 'LETA', 'NORM', 'LBIN',
                  'PKCH', 'LYSP', 'ETRN', 'IMPS', 'ECMP', 'BPTH', 'error'):
            if k in t:
                v = t[k]
                if isinstance(v, float):
                    v = repr(v)
                print('  %-7s %s' % (k, v))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
