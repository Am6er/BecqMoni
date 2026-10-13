# -*- coding: utf-8 -*-
r"""П142 23.09.2026 — дописать §26 (физика 23) в `tools/effmaker/handover-response-matrix.md`.
BOM и CRLF сохраняются; дробная часть — с точкой. Идемпотентно: если §26 уже есть — ничего не делает.
  python handover/p142-rev33/rm_append_26.py
"""
import io
import os
import sys

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
DOC = os.path.join(ROOT, 'tools', 'effmaker', 'handover-response-matrix.md')
SRC = os.path.join(ROOT, 'handover', 'p142-rev33', 'rm_section_26.md')
MARK = u'## \u00a726. \u0424\u0438\u0437\u0438\u043a\u0430 23'


def main():
    raw = open(DOC, 'rb').read()
    text = raw.decode('utf-8-sig')
    if MARK in text:
        print(u'\u00a726 \u0443\u0436\u0435 \u0435\u0441\u0442\u044c \u2014 \u043d\u0438\u0447\u0435\u0433\u043e \u043d\u0435 \u0434\u0435\u043b\u0430\u044e')
        return 0
    add = io.open(SRC, encoding='utf-8').read().replace('\r\n', '\n').strip('\n')
    add = add.replace('\n', '\r\n')
    if not text.endswith(u'\r\n'):
        text += u'\r\n'
    text += u'\r\n' + add + u'\r\n'
    out = (u'\ufeff' + text).encode('utf-8')
    with open(DOC, 'wb') as fh:
        fh.write(out)
    print(u'\u0434\u043e\u043f\u0438\u0441\u0430\u043d\u043e: %d \u2192 %d \u0431\u0430\u0439\u0442, CRLF %d, \u043e\u0434\u0438\u043d\u043e\u0447\u043d\u044b\u0445 LF %d'
          % (len(raw), len(out), out.count(b'\r\n'), out.count(b'\n') - out.count(b'\r\n')))
    return 0


if __name__ == '__main__':
    sys.exit(main())
