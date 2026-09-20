# -*- coding: utf-8 -*-
r"""П87: заменить раздел «## ✅ ДЕЙСТВУЮЩАЯ БАЗА: 15.09.2026 …» (rev25) в tools/CORPUS/README.md разделом rev26
из readme_declare_rev26.md — до строки «### Как переобъявлять базу». Байтами: соглашение переводов строк
раздела берётся у заменяемого текста (CRLF/LF), BOM сохраняется.

  python edit_readme_declare.py <README.md>
"""
import io
import sys

p = sys.argv[1]
b = open(p, 'rb').read()
t = b.decode('utf-8')
start = t.index(u'## ✅ ДЕЙСТВУЮЩАЯ БАЗА: 15.09.2026')
end = t.index(u'### Как переобъявлять базу', start)
old = t[start:end]
nl = u'\r\n' if old.count(u'\r\n') >= old.count(u'\n') // 2 else u'\n'
new = io.open('D:/BqMoni_Claude/p87/readme_declare_rev26.md', encoding='utf-8').read().replace(u'\r\n', u'\n')
new = new.replace(u'\n', nl)
if not new.endswith(nl + nl):
    new = new.rstrip(u'\r\n') + nl + nl
t2 = t[:start] + new + t[end:]
open(p, 'wb').write(t2.encode('utf-8'))
print(u'%s: раздел rev25 (%d строк) заменён rev26 (%d строк), переводы %s, BOM %s' % (
    p, old.count(nl), new.count(nl), 'CRLF' if nl == u'\r\n' else 'LF', 'сохранён' if t2.startswith(u'\ufeff') == t.startswith(u'\ufeff') else '?'))
