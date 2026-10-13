# -*- coding: utf-8 -*-
r"""П188 (29.09.2026): клейма склада — формат, phys=, mdb=, hist=, узлы, Q_k. Только чтение.

  python D:\BqMoni_Claude\p188\stamps.py <каталог склада> [--expect-mdb=<16 знаков>]
Разделитель дробной части — точка.
"""
import collections
import os
import re
import struct
import sys

sys.path.insert(0, r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p140-night')
import rmx_qk  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def main():
    d = sys.argv[1]
    expect = None
    for a in sys.argv[2:]:
        if a.startswith('--expect-mdb='):
            expect = a.split('=', 1)[1]
    files = sorted(f for f in os.listdir(d) if f.lower().endswith('.rmx'))
    phys = collections.Counter()
    mdb = collections.Counter()
    fmt = collections.Counter()
    hist = collections.Counter()
    bad = 0
    for f in files:
        p = os.path.join(d, f)
        m = rmx_qk.read(p)
        b = open(p, 'rb').read(4096)
        stamp, pos = rmx_qk.read_string(b, 8)
        h = struct.unpack_from('<i', b, pos + 8)[0]
        ph = stamp.split(';')[0]
        # клеймо в файле — phys=<N>;<sha256 полного текста>: mdb= внутри хеша, сверяется пробой (MatrixStampProbe)
        md = stamp.split(';')[1][:8] if ';' in stamp else '(нет)'
        hs = re.search(r'hist=(\d+);', stamp)
        phys[ph] += 1
        mdb[md] += 1
        fmt[m['format']] += 1
        hist[h] += 1
        qk_ok = bool(m['qk']) and len(m['qk']) == m['nodes']
        flag = ''
        if not qk_ok:
            flag += ' Q_k-НЕПОЛОН'
        if hs and int(hs.group(1)) != h:
            flag += ' hist-клеймо!=файл'
        if flag:
            bad += 1
        print(u'%-34s фмт %d %s хеш=%s hist=%d узлов %d%s' % (f, m['format'], ph, md, h, m['nodes'], flag))
    print(u'итого %d .rmx; форматы %s; физика %s; хешей различных %d; истории %s; с находками %d'
          % (len(files), dict(fmt), dict(phys), len(mdb), dict(hist), bad))
    return 0 if bad == 0 else 1


if __name__ == '__main__':
    sys.exit(main())

