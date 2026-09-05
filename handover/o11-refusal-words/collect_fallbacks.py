# -*- coding: utf-8 -*-
u"""Собрать пары «ключ → запасная надпись» из вызовов `DoseRateCoefficients.Text`.

Разбор — ЧУЖИМИ руками: берутся `balanced_args` / `split_args` починенного
`tools/check_resx_designer.py` (`T228`), чтобы список ключей и список надписей
получались ОДНИМ разбором и не могли разойтись. Печатает TSV:
ключ, файл, строка, запасная надпись.
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'tools'))

import check_resx_designer as C

# ⚠ Внутри самого `DoseRateCoefficients` обёртка зовётся БЕЗ имени класса —
# четыре ключа (`DoseRateEnergyNotPositive`, `DoseRateNoElement`,
# `DoseRateOutsideXcom`, `DoseRateOutsideIcrp`) записаны именно так, и первый
# заход по «`DoseRateCoefficients.Text`» нашёл 26 мест вместо 30.
CALL = re.compile(r'(?:\bDoseRateCoefficients\.Text|(?<![\w.])Text)\s*\(')

rows = []
for path in C.sources(os.path.join(ROOT, 'BecquerelMonitor'), designer=False):
    text = C.read(path).replace('\r\n', '\n')
    for m in CALL.finditer(text):
        args = C.balanced_args(text, m.end() - 1)
        if args is None:
            continue
        parts = C.split_args(args)
        if len(parts) < 2:
            continue
        key = re.match(r'\s*"([^"]*)"\s*\Z', parts[0], re.S)
        # запасная надпись может быть склейкой литералов через `+`
        pieces = re.findall(r'"((?:[^"\\]|\\.)*)"', parts[1])
        if not key:
            continue
        fallback = ''.join(pieces)
        rows.append((key.group(1), os.path.relpath(path, ROOT).replace('\\', '/'),
                     C.line_of(text, m.start()), fallback, parts[1].strip()))

out = io.open(os.path.join(HERE, 'fallbacks.tsv'), 'w', encoding='utf-8', newline='\n')
for key, path, num, fallback, raw in sorted(rows):
    out.write(u'%s\t%s:%d\t%s\n' % (key, path, num, fallback))
out.close()

uniq = {}
for key, path, num, fallback, raw in rows:
    uniq.setdefault(key, set()).add(fallback)
print(u'вызовов: %d, ключей: %d' % (len(rows), len(uniq)))
for key in sorted(uniq):
    if len(uniq[key]) > 1:
        print(u'РАЗНЫЕ НАДПИСИ у одного ключа: %s' % key)
        for f in sorted(uniq[key]):
            print(u'    %s' % f)
