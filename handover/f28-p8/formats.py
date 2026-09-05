# -*- coding: utf-8 -*-
"""F28: ВСЕ `string.Format`/`String.Format` доли П8 БЕЗ провайдера — с доводами.

Печатает сам оператор целиком (до ';'), чтобы разряд «числа среди доводов нет»
ставился по ТЕКСТУ, а не по первому доводу из таблицы сканера.
"""
import os, re, sys, io
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from census import mine

ROOT = 'BecquerelMonitor'
SKIP = {'bin', 'obj', 'packages', '.git', '.vs'}
RX = re.compile(r'\b(string|String)\.Format\s*\(')

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
            m = RX.search(line)
            if not m:
                continue
            # оператор целиком
            parts = []
            j = i
            while j < len(lines) and j < i + 10:
                parts.append(lines[j].strip())
                if lines[j].rstrip().endswith(';'):
                    break
                j += 1
            whole = ' '.join(parts)
            # ⛔ Провайдер часто стоит на СЛЕДУЮЩЕЙ строке — смотреть только
            #    хвост своей строки значило бы объявить 12 верных мест
            #    (все отказы `DoseRate`) дефектными.
            tail = whole[whole.index('.Format(') + 8:].lstrip()
            if tail.startswith('CultureInfo.InvariantCulture'):
                continue
            rows.append('%s:%d\t%s' % (rel, i + 1, whole[:220]))

for r in rows:
    print(r)
print('---')
print('string.Format БЕЗ инварианта в доле П8:', len(rows))
