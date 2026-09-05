# -*- coding: utf-8 -*-
"""F28: ВЕСЬ РАЗБОР текста в числа по доле П8 — сплошным поиском.

`Convert.ToInt32(строка)` / `int.Parse` / `double.TryParse` / `decimal.Parse`
и `Convert.ToString(число)`. Печатается оператор целиком.
"""
import os, re, sys, io
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from census import mine

ROOT = 'BecquerelMonitor'
SKIP = {'bin', 'obj', 'packages', '.git', '.vs'}
RX = re.compile(r'\b(?<!Xml)(?:(?:double|float|decimal|int|long|short|byte|uint|ulong|ushort|sbyte'
                r'|Double|Single|Decimal|Int32|Int64|Int16|Byte|UInt32|UInt64|UInt16|SByte'
                r'|DateTime|TimeSpan)\.(?:Parse|TryParse)|Convert\.To(?:String|Int32|Int64|Double|Decimal|Single|Int16))\s*\(')

rows = []
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = [d for d in dirnames if d not in SKIP]
    for fn in filenames:
        if not fn.endswith('.cs'):
            continue
        rel = os.path.join(dirpath, fn).replace(os.sep, '/')
        if not mine(rel):
            continue
        try:
            src = io.open(rel, encoding='utf-8-sig', newline='').read()
        except UnicodeDecodeError:
            src = io.open(rel, encoding='cp1251', newline='').read()
        lines = src.split('\n')
        for i, line in enumerate(lines):
            st = line.strip()
            if st.startswith('//') or st.startswith('*'):
                continue
            if not RX.search(line):
                continue
            parts = []
            j = i
            while j < len(lines) and j < i + 8:
                parts.append(lines[j].strip())
                if lines[j].rstrip().endswith(';'):
                    break
                j += 1
            whole = ' '.join(parts)
            mark = 'ИНВ ' if 'InvariantCulture' in whole else '??? '
            rows.append('%s%s:%d\t%s' % (mark, rel, i + 1, whole[:200]))

for r in sorted(rows):
    print(r)
print('---')
print('всего мест разбора/Convert:', len(rows),
      '; без инварианта:', sum(1 for r in rows if r.startswith('???')))
