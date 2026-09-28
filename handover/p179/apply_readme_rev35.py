# -*- coding: utf-8 -*-
r"""П179: заменить раздел «## ✅ ДЕЙСТВУЮЩАЯ БАЗА» в tools/CORPUS/README.md (до «### Как переобъявлять базу»)
текстом handover/p179/readme_declare_rev35.md (образец — П176 apply_readme_rev34.py). BOM и CRLF сохраняются.
   python handover/p179/apply_readme_rev35.py [корень]
"""
import io, os, sys
for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass
ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
README = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
SECTION = os.path.join(ROOT, 'handover', 'p179', 'readme_declare_rev35.md')
raw = open(README, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
text = raw.decode('utf-8-sig')
if text.count(u'\n## ✅ ДЕЙСТВУЮЩАЯ БАЗА: ') != 1:
    print(u'⛔ заголовков раздела не один'); sys.exit(1)
start = text.index(u'\n## ✅ ДЕЙСТВУЮЩАЯ БАЗА: ') + 1
end = text.index(u'### Как переобъявлять базу', start)
eol = '\r\n' if '\r\n' in text else '\n'
new = io.open(SECTION, encoding='utf-8').read().replace('\r\n', '\n').rstrip('\n') + '\n\n'
new = new.replace('\n', eol)
text2 = text[:start] + new + text[end:]
open(README, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + text2.encode('utf-8'))
lone = text2.count('\n') - text2.count('\r\n')
print(u'BOM %s; перевод строки %r; CRLF %d -> %d; одиночных LF %d' % (bom, eol, text.count('\r\n'), text2.count('\r\n'), lone))
sys.exit(0 if (eol == '\n' or lone == 0) else 1)
