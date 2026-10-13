# -*- coding: utf-8 -*-
r"""П141 (23.09.2026) — ЧИТАТЕЛЬ клейм ЖИВОГО склада корпуса, только чтение.

Независим от кода приложения: раскладку читает `rmx_qk.py` полосы П140
(`handover/p140-night/rmx_qk.py`, ⛔ не правится). Печатает у каждой матрицы
формат файла, клеймо, число узлов, число каналов и sha256 тела.

  python stamp_check.py [<каталог склада>] [--only=RC103_point0,AS80_point0]

Разделитель дробной части — точка.
"""
import os
import struct
import sys

sys.path.insert(0, r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p140-night')
import rmx_qk  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def channels(path):
    """Число каналов — сразу за сеткой узлов (раскладка `ResponseMatrix.Save`)."""
    b = open(path, 'rb').read()
    pos = 8
    _s, pos = rmx_qk.read_string(b, pos)
    pos += 8 + 4 + 8 + 8 + 44
    nodes = struct.unpack_from('<i', b, pos)[0]
    pos += 4 + 8 * nodes
    return struct.unpack_from('<i', b, pos)[0]


def main(argv):
    store = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
    only = None
    for a in argv:
        if a.startswith('--only='):
            only = a[len('--only='):].split(',')
        elif not a.startswith('--'):
            store = a
    names = sorted(f[:-4] for f in os.listdir(store) if f.endswith('.rmx'))
    if only:
        names = [n for n in names if n in only]
    formats, stamps, chans = {}, {}, {}
    for name in names:
        p = os.path.join(store, name + '.rmx')
        m = rmx_qk.read(p)
        ch = channels(p)
        phys = m['stamp'].split(';')[0]
        formats[m['format']] = formats.get(m['format'], 0) + 1
        stamps[phys] = stamps.get(phys, 0) + 1
        chans[ch] = chans.get(ch, 0) + 1
        print('%-28s формат %d  %s  клеймо %s  узлов %3d  каналов %d  Q_k узлов %3d  тело sha %s'
              % (name, m['format'], phys, m['stamp'].split(';')[1][:16] if ';' in m['stamp'] else '?',
                 m['nodes'], ch, len(m['qk'] or []), m['body_sha'][:16]))
    print('')
    print('матриц прочитано: %d' % len(names))
    print('формат файла   : %s' % ', '.join('%d — %d' % kv for kv in sorted(formats.items())))
    print('версия физики  : %s' % ', '.join('%s — %d' % kv for kv in sorted(stamps.items())))
    print('каналов        : %s' % ', '.join('%d — %d' % kv for kv in sorted(chans.items())))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
