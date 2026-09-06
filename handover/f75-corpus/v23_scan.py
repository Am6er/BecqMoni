# -*- coding: utf-8 -*-
u"""V23, пункт (1): сплошной проход по .md дерева за цитатами «доли с вершиной».

Ищутся числа 71.2 / 73.4 / 75.9 в окне со словом о признаке («вершин», `top`,
«выделен»), чтобы не считать цитатой совпавшее число из другой величины.
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

NEED = ['71.2', '73.4', '75.9']
KEY = re.compile(u'вершин|top|выделен', re.I)
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

print(u'=== сплошной проход по .md дерева (кроме .git/packages/bin/obj) ===')
hits = 0
for root, dirs, files in os.walk(ROOT):
    dirs[:] = [d for d in dirs if d not in ('.git', 'packages', 'bin', 'obj')]
    for f in files:
        if not f.endswith('.md'):
            continue
        p = os.path.join(root, f)
        try:
            text = io.open(p, encoding='utf-8', errors='replace').read()
        except OSError:
            continue
        for i, line in enumerate(text.split('\n'), 1):
            near = []
            for n in NEED:
                for m in re.finditer(re.escape(n), line):
                    a, b = max(0, m.start() - 260), min(len(line), m.end() + 140)
                    if KEY.search(line[a:b]):
                        near.append(n)
                        break
            if near:
                hits += 1
                rel = os.path.relpath(p, ROOT).replace(os.sep, '/')
                print(u'  %s:%d  %s' % (rel, i, ', '.join(sorted(set(near)))))
print(u'  всего мест: %d' % hits)
