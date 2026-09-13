# -*- coding: utf-8 -*-
"""Побайтовое сравнение двух `.rmx` БЕЗ полей времени (П45, приёмка `A43`).

Маскируются ровно три поля (`ResponseMatrix.Save`): `CreatedUtc.Ticks` (int64) и
`BuildSeconds` (double) в шапке — сразу за `BQRM`, форматом, клеймом, `BinKev`
и `Histories`; и `NodeSeconds` (double[]) в блоке `NOIS` — после четырёх чисел
и двух массивов (`NodeHistories`, `NodeErrors`). Всё остальное обязано совпасть
байт в байт: клеймо, параметры, тело, отпечаток `BODY`, хвосты ключей.

  python rmx_bytes.py <a.rmx> <b.rmx>            -> код 0 = тождественны, 1 = нет
  python rmx_bytes.py --selfcheck <a.rmx>        -> положительный контроль: копия
     с одним испорченным байтом ТЕЛА обязана отказать, с испорченным байтом
     ВРЕМЕНИ — пройти; код 0 = контроль прошёл

Разделитель дробной части — точка.
"""
import io
import struct
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def read_string_len(b, pos):
    n, shift = 0, 0
    while True:
        c = b[pos]
        pos += 1
        n |= (c & 0x7F) << shift
        if c < 0x80:
            break
        shift += 7
    return n, pos


def time_ranges(b):
    """Список (начало, конец) байтовых отрезков полей времени."""
    if b[:4] != b'BQRM':
        raise ValueError('не BQRM')
    n, pos = read_string_len(b, 8)
    stamp = b[pos:pos + n].decode('utf-8')
    pos += n
    pos += 8 + 4                       # BinKev, Histories
    header = (pos, pos + 16)           # CreatedUtc.Ticks + BuildSeconds
    hits = [i for i in range(len(b)) if b.startswith(b'NOIS', i)]
    if len(hits) != 1:
        raise ValueError('меток NOIS %d, ожидалась одна' % len(hits))
    q = hits[0] + 4 + 8 + 8 + 8 + 8
    cnt = struct.unpack_from('<i', b, q)[0]
    q += 4 + 8 * cnt                   # NodeHistories
    cnt = struct.unpack_from('<i', b, q)[0]
    q += 4 + 8 * cnt                   # NodeErrors
    cnt = struct.unpack_from('<i', b, q)[0]
    seconds = (q + 4, q + 4 + 8 * cnt) # NodeSeconds — сами числа, счётчик сравнивается
    return stamp, [header, seconds]


def masked(b, ranges):
    out = bytearray(b)
    for s, e in ranges:
        for i in range(s, e):
            out[i] = 0
    return bytes(out)


def body_fingerprint(b):
    o = b.rfind(b'OPTF')
    if o < 0 or b[o + 4 + 7:o + 4 + 7 + 4] != b'BODY':
        return None
    return b[o + 4 + 7 + 4:o + 4 + 7 + 4 + 32].hex()


def compare(a, b, verbose=True):
    sa, ra = time_ranges(a)
    sb, rb = time_ranges(b)
    same_len = len(a) == len(b)
    same_ranges = ra == rb
    ma, mb = masked(a, ra), masked(b, rb)
    same = same_len and same_ranges and ma == mb
    # Файл старшего формата длиннее на хвосты ключей (IMPS/ECMP/BPTH…): сравнить
    # общую часть и назвать лишний хвост — это НЕ тождественность, а справка.
    tail_note = None
    if not same_len and same_ranges:
        n = min(len(ma), len(mb))
        if ma[:n] == mb[:n]:
            longer = a if len(a) > len(b) else b
            extra = longer[n:]
            tags = [extra[i:i + 4].decode('ascii', 'replace') for i in range(len(extra)) if extra[i:i + 4].isalpha() and extra[i:i + 4].isupper()]
            tail_note = 'общая часть %d байт тождественна; %s длиннее на %d байт: хвосты %s' % (
                n, 'первый' if len(a) > len(b) else 'второй', len(extra), ' '.join(tags))
    if verbose:
        print('  длина      : %d / %d %s' % (len(a), len(b), 'одинакова' if same_len else 'РАЗНАЯ'))
        if tail_note:
            print('  хвост      : ' + tail_note)
        print('  клеймо     : %s' % ('одинаковое' if sa == sb else 'РАЗНОЕ: %s / %s' % (sa, sb)))
        print('  отпечаток  : %s / %s' % (body_fingerprint(a), body_fingerprint(b)))
        print('  поля времени: %s' % ', '.join('[%d..%d)' % r for r in ra))
        if same_len and same_ranges and not same:
            first = next(i for i in range(len(ma)) if ma[i] != mb[i])
            diff = sum(1 for i in range(len(ma)) if ma[i] != mb[i])
            print('  РАСХОЖДЕНИЕ: первый байт %d, всего байт %d' % (first, diff))
        print('  ИТОГ: %s' % ('ТОЖДЕСТВЕННЫ без полей времени' if same else 'НЕ ТОЖДЕСТВЕННЫ'))
    return same


def selfcheck(path):
    a = open(path, 'rb').read()
    _, ranges = time_ranges(a)
    # 1. порча одного байта ТЕЛА (первый байт за полем времени шапки + 1000)
    body_pos = ranges[0][1] + 1000
    bad = bytearray(a)
    bad[body_pos] ^= 0x01
    caught = not compare(a, bytes(bad), verbose=False)
    print('контроль 1: порча байта тела в %d — %s' % (body_pos, 'ПОЙМАНА' if caught else 'ПРОПУЩЕНА'))
    # 2. порча байта ВРЕМЕНИ (BuildSeconds, старший байт)
    tpos = ranges[0][0] + 15
    bad2 = bytearray(a)
    bad2[tpos] ^= 0x40
    passed = compare(a, bytes(bad2), verbose=False)
    print('контроль 2: порча байта времени в %d — %s' % (tpos, 'проигнорирована, как и должно' if passed else 'ОТКАЗ — маска не сработала'))
    # 3. порча байта NodeSeconds
    s, e = ranges[1]
    ok3 = True
    if e > s:
        bad3 = bytearray(a)
        bad3[s + 3] ^= 0x10
        ok3 = compare(a, bytes(bad3), verbose=False)
        print('контроль 3: порча байта NodeSeconds в %d — %s' % (s + 3, 'проигнорирована, как и должно' if ok3 else 'ОТКАЗ — маска не сработала'))
    # 4. порча байта ХВОСТА (последний байт файла — ключ ETRN/ECMP/BPTH)
    bad4 = bytearray(a)
    bad4[-1] ^= 0x01
    caught4 = not compare(a, bytes(bad4), verbose=False)
    print('контроль 4: порча последнего байта (хвост ключей) — %s' % ('ПОЙМАНА' if caught4 else 'ПРОПУЩЕНА'))
    good = caught and passed and ok3 and caught4
    print('САМОПРОВЕРКА %s' % ('ПРОШЛА' if good else 'НЕ ПРОШЛА'))
    return 0 if good else 1


def main(argv):
    if len(argv) >= 3 and argv[1] == '--selfcheck':
        return selfcheck(argv[2])
    if len(argv) != 3:
        print(__doc__)
        return 2
    a = open(argv[1], 'rb').read()
    b = open(argv[2], 'rb').read()
    print('%s\n%s' % (argv[1], argv[2]))
    return 0 if compare(a, b) else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
