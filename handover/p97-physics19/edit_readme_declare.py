# -*- coding: utf-8 -*-
r"""П97 — заменить раздел «## ✅ ДЕЙСТВУЮЩАЯ БАЗА …» README корпуса (до «### Как переобъявлять базу») текстом
readme_declare_rev28.md. Байты: BOM и CRLF README сохраняются; текст объявления приводится к CRLF.
  python handover/p97-physics19/edit_readme_declare.py [--check]
"""
import io
import os
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
README = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
NEW = os.path.join(HERE, 'readme_declare_rev28.md')
HEAD_MARK = u'\n## ✅ ДЕЙСТВУЮЩАЯ БАЗА: '
TAIL_MARK = u'### Как переобъявлять базу'


def main(argv):
    b = open(README, 'rb').read()
    bom = b[:3] == b'\xef\xbb\xbf'
    t = b.decode('utf-8-sig')
    assert t.count(HEAD_MARK) == 1, u'заголовков действующей базы: %d' % t.count(HEAD_MARK)
    assert t.count(TAIL_MARK) == 1
    i = t.index(HEAD_MARK) + 1
    j = t.index(TAIL_MARK)
    new = io.open(NEW, encoding='utf-8-sig').read().replace(u'\r\n', u'\n').replace(u'\n', u'\r\n')
    if not new.endswith(u'\r\n'):
        new += u'\r\n'
    out = t[:i] + new + u'\r\n' + t[j:]
    old_section = t[i:j]
    print(u'старый раздел: %d символов, новый: %d; BOM %s' % (len(old_section), len(new), bom))
    if '--check' in argv:
        return 0
    data = out.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    open(README, 'wb').write(data)
    chk = open(README, 'rb').read()
    crlf = chk.count(b'\r\n')
    lf = chk.count(b'\n') - crlf
    print(u'записано: crlf %d, lf-only %d' % (crlf, lf))
    return 0 if lf == 0 else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
