# -*- coding: utf-8 -*-
"""Замена кусков в CRLF-файле БАЙТАМИ: читаем bytes, ищем куски с CRLF, пишем bytes. BOM сохраняется."""
import io, sys
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\FsaReportViewProbe.cs'

def apply(pairs):
    b = io.open(p, 'rb').read()
    assert b[:3] == b'\xef\xbb\xbf'
    for old, new in pairs:
        o = old.replace('\n', '\r\n').encode('utf-8')
        n = new.replace('\n', '\r\n').encode('utf-8')
        c = b.count(o)
        assert c == 1, ('не ровно одно вхождение: %d' % c, old[:80])
        b = b.replace(o, n)
    io.open(p, 'wb').write(b)
    lf_only = b.count(b'\n') - b.count(b'\r\n')
    assert lf_only == 0, lf_only
    print('ok, CRLF %d' % b.count(b'\r\n'))

if __name__ == '__main__':
    exec(io.open(sys.argv[1], encoding='utf-8').read())
