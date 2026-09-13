# -*- coding: utf-8 -*-
"""Снять мёртвые строки `EfficiencyMaker*` из общих ресурсов: Resources.resx,
Resources.ru.resx (CRLF+BOM сохраняются) и Resources.Designer.cs (свойства).
Список имён — D:\\BqMoni_Claude\\p54\\dead_resources_rm.txt."""
import io
import re
import sys

REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
names = [l.strip() for l in io.open(r'D:\BqMoni_Claude\p54\dead_resources_rm.txt', encoding='utf-8') if l.strip()]


def load(p):
    raw = open(p, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    nl = '\r\n' if '\r\n' in text else '\n'
    return text, bom, nl


def save(p, text, bom):
    out = text.encode('utf-8')
    if bom:
        out = b'\xef\xbb\xbf' + out
    open(p, 'wb').write(out)


def strip_resx(p):
    text, bom, nl = load(p)
    assert nl == '\r\n', p
    gone = []
    for name in names:
        pat = re.compile(r'  <data name="' + re.escape(name) + r'"[^>]*>.*?</data>\r\n', re.S)
        text, n = pat.subn('', text)
        if n == 1:
            gone.append(name)
        elif n > 1:
            raise SystemExit('%s: %s встречается %d раз' % (p, name, n))
    save(p, text, bom)
    return gone


def strip_designer(p):
    text, bom, nl = load(p)
    gone = []
    for name in names:
        # необязательный xml-комментарий перед свойством + само свойство + пустая строка
        pat = re.compile(
            r'(?:        /// <summary>' + nl + r'(?:        ///.*?' + nl + r')*?        /// </summary>' + nl + r')?'
            r'        public static string ' + re.escape(name) + r' \{' + nl
            + r'            get \{' + nl
            + r'                return ResourceManager\.GetString\("' + re.escape(name) + r'", resourceCulture\);' + nl
            + r'            \}' + nl
            + r'        \}' + nl
            + r'(?:[ 	]*' + nl + r')?')
        text, n = pat.subn('', text)
        if n == 1:
            gone.append(name)
        elif n != 0:
            raise SystemExit('%s: %s встречается %d раз' % (p, name, n))
    save(p, text, bom)
    return gone


for rel, fn in ((r'\BecquerelMonitor\Properties\Resources.resx', strip_resx),
                (r'\BecquerelMonitor\Properties\Resources.ru.resx', strip_resx),
                (r'\BecquerelMonitor\Properties\Resources.Designer.cs', strip_designer)):
    gone = fn(REPO + rel)
    missing = sorted(set(names) - set(gone))
    print('%s: снято %d, не найдено %d %s' % (rel, len(gone), len(missing), missing))
