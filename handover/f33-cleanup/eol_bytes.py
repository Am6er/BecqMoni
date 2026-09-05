# -*- coding: utf-8 -*-
"""S129: perevody strok schitajutsja TOLKO bajtami (pravilo pamjati).

    python eol_bytes.py <file> [<file> ...]
"""
import io
import sys

for p in sys.argv[1:]:
    b = io.open(p, 'rb').read()
    crlf = b.count(b'\r\n')
    lf = b.count(b'\n') - crlf
    cr = b.count(b'\r') - crlf
    print('%s : CRLF %d, lone LF %d, lone CR %d' % (p, crlf, lf, cr))
