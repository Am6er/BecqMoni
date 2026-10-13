# -*- coding: utf-8 -*-
r"""П176: заменить раздел «## ✅ ДЕЙСТВУЮЩАЯ БАЗА» в tools/CORPUS/README.md (до «### Как переобъявлять базу»)
текстом handover/p176-rev34/readme_declare_rev34.md. BOM и CRLF файла сохраняются; печатает счёт CR/LF до и после.
"""
import io
import os
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
README = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
SECTION = os.path.join(ROOT, 'handover', 'p176-rev34', 'readme_declare_rev34.md')

raw = open(README, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
text = raw.decode('utf-8-sig')
crlf_before = text.count('\r\n')
lf_before = text.count('\n')
start = text.index(u'\n## ✅ ДЕЙСТВУЮЩАЯ БАЗА: ') + 1
end = text.index(u'### Как переобъявлять базу', start)
if text.count(u'\n## ✅ ДЕЙСТВУЮЩАЯ БАЗА: ') != 1:
    print(u'⛔ заголовков раздела не один')
    sys.exit(1)
new = io.open(SECTION, encoding='utf-8').read().replace('\r\n', '\n').rstrip('\n') + '\n\n'
new = new.replace('\n', '\r\n')
text2 = text[:start] + new + text[end:]
out = (b'\xef\xbb\xbf' if bom else b'') + text2.encode('utf-8')
open(README, 'wb').write(out)
lone_lf = text2.count('\n') - text2.count('\r\n')
print(u'BOM: %s; до: CRLF %d / LF %d; после: CRLF %d / LF %d; одиночных LF %d'
      % (bom, crlf_before, lf_before, text2.count('\r\n'), text2.count('\n'), lone_lf))
sys.exit(0 if lone_lf == 0 else 1)
