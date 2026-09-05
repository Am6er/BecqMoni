# -*- coding: utf-8 -*-
"""F28: слепые пятна сканера по МОЕЙ доле.

Сканер `scan_culture.py` не видит:
  * StringBuilder.Append(число) / AppendLine(число) / AppendFormat без провайдера
  * string.Join(разделитель, числа)
  * string.Concat над object[] (число боксируется, ToString зовёт склейка)
  * интерполяцию с числом внутри
Ищем их сплошным поиском по файлам доли П8.
"""
import os, re, sys, io
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from census import mine

ROOT = 'BecquerelMonitor'
SKIP = {'bin', 'obj', 'packages', '.git', '.vs'}

PATTERNS = [
    ('Append',      re.compile(r'\.Append(Line|Format)?\s*\(')),
    ('string.Join', re.compile(r'(string|String)\.Join\s*\(')),
    ('Concat',      re.compile(r'(string|String)\.Concat\s*\(')),
    ('interp',      re.compile(r'\$"')),
    ('ToString()',  re.compile(r'\.ToString\s*\(\s*\)')),
    ('ToString(f)', re.compile(r'\.ToString\s*\(\s*"[^"]*"\s*\)')),
]

counts = {}
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
        for i, line in enumerate(src.split('\n'), 1):
            st = line.strip()
            if st.startswith('//') or st.startswith('///') or st.startswith('*'):
                continue
            for name, rx in PATTERNS:
                if rx.search(line):
                    counts[name] = counts.get(name, 0) + 1
                    rows.append('%s:%d\t%s\t%s' % (rel, i, name, line.strip()[:150]))

out = sys.argv[1] if len(sys.argv) > 1 else None
if out:
    io.open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(rows) + '\n')
for k in sorted(counts):
    print('%-12s %d' % (k, counts[k]))
print('всего строк:', len(rows))
