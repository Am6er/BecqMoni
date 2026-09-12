# -*- coding: utf-8 -*-
"""Читатель клейма и хвостов файла матрицы `.rmx` — ТОЛЬКО чтение (П22, 12.09.2026).

    python handover/p22-th-disk/read_rmx_stamp.py <файл.rmx> [...]

Печатает размер, клеймо `phys=N;<sha256>` (первое вхождение в файле) и хвосты
PKBN/XRKL/PKWT/NORM (байт), KDIP/JNTH (int32), LETA (double) — по метке с конца
файла, как их пишет `ResponseMatrix.Save` (`ResponseMatrix.cs`, хвосты 4…12).
"""
import os
import re
import struct
import sys


def info(path):
    b = open(path, 'rb').read()
    m = re.search(rb'phys=\d+;[0-9a-f]{64}', b)
    stamp = m.group(0).decode() if m else None
    tails = {}
    for tag in (b'PKBN', b'XRKL', b'KDIP', b'LETA', b'NORM', b'PKWT', b'JNTH'):
        i = b.rfind(tag)
        name = tag.decode()
        if i < 0:
            tails[name] = None
        elif tag in (b'PKBN', b'XRKL', b'PKWT', b'NORM'):
            tails[name] = b[i + 4]
        elif tag in (b'KDIP', b'JNTH'):
            tails[name] = struct.unpack_from('<i', b, i + 4)[0]
        else:
            tails[name] = struct.unpack_from('<d', b, i + 4)[0]
    return len(b), stamp, tails


if __name__ == '__main__':
    for p in sys.argv[1:]:
        n, stamp, tails = info(p)
        print('%-28s %8d  %s  %s' % (os.path.basename(p), n, stamp, tails))
