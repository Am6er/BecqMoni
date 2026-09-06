# -*- coding: utf-8 -*-
"""Счёт мест вызова FwhmCalibration.DefaultCalibration — старой и новой подписью.

Считается ВЫЗОВ, а не строка: аргументы бывают на двух строках, и грепом по
строке они разъезжаются. Объявления самого метода (FwhmCalibration.cs)
исключены поимённо.
"""
import io
import os
import re
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'
SCOPE = sys.argv[2] if len(sys.argv) > 2 else 'BecquerelMonitor'

pat = re.compile(r'DefaultCalibration\s*\(')
old, new, decl = [], [], []

for dirpath, dirnames, filenames in os.walk(os.path.join(ROOT, SCOPE)):
    dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
    for fn in sorted(filenames):
        if not fn.endswith('.cs'):
            continue
        path = os.path.join(dirpath, fn)
        text = io.open(path, encoding='utf-8-sig', newline='', errors='replace').read()
        for m in pat.finditer(text):
            i = m.end()
            depth = 1
            while i < len(text) and depth:
                if text[i] == '(':
                    depth += 1
                elif text[i] == ')':
                    depth -= 1
                i += 1
            args = text[m.end():i - 1]
            line = text.count('\n', 0, m.start()) + 1
            # объявление метода: перед именем стоит тип возврата
            head = text[max(0, m.start() - 90):m.start()]
            where = '%s:%d' % (os.path.relpath(path, ROOT).replace('\\', '/'), line)
            if 'SimpleSqrtFwhmCalibration DefaultCalibration' in head + 'DefaultCalibration':
                if re.search(r'static\s+SimpleSqrtFwhmCalibration\s*$', head):
                    decl.append(where)
                    continue
            if re.search(r'\bout\s+\w', args):
                new.append(where)
            else:
                old.append(where)

print('=== %s ===' % os.path.join(ROOT, SCOPE))
print('объявлений метода: %d' % len(decl))
for w in decl:
    print('    ' + w)
print('вызовов НОВОЙ подписью (out refusal): %d' % len(new))
for w in new:
    print('    ' + w)
print('вызовов СТАРОЙ подписью: %d' % len(old))
for w in old:
    print('    ' + w)
