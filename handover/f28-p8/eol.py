# -*- coding: utf-8 -*-
"""F28: счёт переводов строк ТОЛЬКО по байтам (грабля «переводы строк считать байтами»).
Использование: python eol.py <файл> [<файл> ...]
"""
import sys, io

for p in sys.argv[1:]:
    b = io.open(p, 'rb').read()
    crlf = b.count(b'\r\n')
    cr = b.count(b'\r') - crlf
    lf = b.count(b'\n') - crlf
    bom = b[:3] == b'\xef\xbb\xbf'
    print('%s  байт=%d CRLF=%d одиночных_CR=%d одиночных_LF=%d BOM=%s'
          % (p, len(b), crlf, cr, lf, 'да' if bom else 'нет'))
