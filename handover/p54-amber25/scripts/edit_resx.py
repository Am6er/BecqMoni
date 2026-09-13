# -*- coding: utf-8 -*-
"""Снять из .resx записи <data name="..."> по списку имён. Байты: CRLF и BOM сохраняются."""
import io
import re
import sys

REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'


def remove(path, names):
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    assert '\r\n' in text and text.count('\n') == text.count('\r\n'), path + ': не CRLF'
    removed = []
    for name in names:
        pattern = re.compile(r'  <data name="' + re.escape(name) + r'"[^>]*>.*?</data>\r\n', re.S)
        text, n = pattern.subn('', text)
        if n:
            removed.append((name, n))
    out = text.encode('utf-8')
    if bom:
        out = b'\xef\xbb\xbf' + out
    open(path, 'wb').write(out)
    return removed


if __name__ == '__main__':
    target = sys.argv[1]
    names = [n for n in sys.argv[2:]]
    for name, n in remove(target, names):
        print('снято', name, n)
