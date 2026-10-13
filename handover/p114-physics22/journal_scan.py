# -*- coding: utf-8 -*-
import re
import sys
sys.stdout.reconfigure(encoding='utf-8')
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\handover-2026-09-19-p114-physics22-rev32.md'
b = open(p, 'rb').read()
crlf = b.count(b'\r\n')
print('CRLF', crlf, 'bare CR', b.count(b'\r') - crlf, 'LF', b.count(b'\n') - crlf)
for m in re.finditer(b'\r(?!\n)', b):
    print('bare CR at', m.start(), b[m.start() - 40:m.start() + 40])
for ctrl in (b'\x08', b'\t', b'\x0c', b'\x0b', b'\x07'):
    if ctrl in b:
        print('ctrl', ctrl, b.count(ctrl))
i = b.find('store\\`'.encode('utf-8'))
print('store\\` at', i, b[i - 60:i + 40] if i >= 0 else '')
