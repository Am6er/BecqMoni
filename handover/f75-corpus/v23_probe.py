# -*- coding: utf-8 -*-
u"""V23, пункт (1): где на самом деле живут «доли с вершиной» 71.2 / 73.4 / 75.9.

Считается по файлам, а не по памяти. Положительный контроль обязателен: тем же
способом ищутся строки, которые в этих файлах ТОЧНО есть.
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

FILES = [
    os.path.join(ROOT, 'tools', 'CORPUS', 'README.md'),
    os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'SUMMARY.md'),
    os.path.join(ROOT, 'TODO.md'),
    os.path.join(ROOT, 'DONE.md'),
]
NEEDLES = ['71.2', '73.4', '75.9']
CONTROL = ['V14', 'вершин', 'корпус']

print('%-26s %s' % ('файл', ' | '.join(NEEDLES + ['<- искомое'] + CONTROL + ['<- контроль'])))
for path in FILES:
    if not os.path.isfile(path):
        print('%-26s НЕТ ФАЙЛА' % os.path.basename(path))
        continue
    text = io.open(path, encoding='utf-8', errors='replace').read()
    cnt = [text.count(n) for n in NEEDLES]
    ctl = [len(re.findall(re.escape(c), text)) for c in CONTROL]
    print('%-26s %s   |   %s' % (os.path.basename(path),
                                 '  '.join('%s=%d' % (n, c) for n, c in zip(NEEDLES, cnt)),
                                 '  '.join('%s=%d' % (n, c) for n, c in zip(CONTROL, ctl))))

# Где именно в TODO.md — по строкам реестра.
todo = io.open(os.path.join(ROOT, 'TODO.md'), encoding='utf-8', errors='replace')
print()
print('строки TODO.md с этими числами:')
for i, line in enumerate(todo, 1):
    hit = [n for n in NEEDLES if n in line]
    if not hit:
        continue
    m = re.match(r'\|\s*~?~?\*\*([A-Z]+\d+)\*\*', line)
    print('  :%-6d строка %-6s числа %s' % (i, m.group(1) if m else '?', ', '.join(hit)))
