# -*- coding: utf-8 -*-
r"""П176: сводка расхождений витрины из check_fsa_showcase_p176.log — по парам (спектр/режим): число расхождений
по заголовку, напечатанных строк, медиана и максимум |Δ| %, медиана |Δ| строки `model`; крупнейшие сдвиги.
"""
import io
import os
import re
import statistics
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

LOG = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'check_fsa_showcase_p176.log')
HEAD = re.compile(u'КАРТИНКА ИЗМЕНИЛАСЬ: (\\S+) / (\\S+) — (\\d+) расхождений')
ROW = re.compile(u'^\\s+(\\S+)\\s+(\\S+)\\s+(\\S+ кэВ)\\s+(\\S+)\\s+(-?[0-9.]+)\\s+(-?[0-9.]+)\\s+\\S+ \\(([+-]?[0-9.]+) %\\)')
pairs = {}
order = []
rows = []
for ln in io.open(LOG, encoding='utf-8'):
    m = HEAD.search(ln)
    if m:
        k = (m.group(1), m.group(2))
        pairs[k] = {'n': int(m.group(3)), 'd': [], 'model': []}
        order.append(k)
        continue
    m = ROW.match(ln)
    if m and (m.group(1), m.group(2)) in pairs:
        k = (m.group(1), m.group(2))
        d = abs(float(m.group(7)))
        pairs[k]['d'].append(d)
        if m.group(4) == 'model':
            pairs[k]['model'].append(d)
        rows.append((d, m.group(1), m.group(2), m.group(3), m.group(4), m.group(5), m.group(6), m.group(7)))
print(u'| спектр | режим | расхождений | напечатано | медиана |Δ| | макс |Δ| | `model`, медиана |Δ| |')
print(u'|---|---|---|---|---|---|---|')
tot = 0
for k in order:
    p = pairs[k]
    tot += p['n']
    med = statistics.median(p['d']) if p['d'] else float('nan')
    mx = max(p['d']) if p['d'] else float('nan')
    mm = statistics.median(p['model']) if p['model'] else float('nan')
    print(u'| `%s` | `%s` | %d | %d | %.2f %% | %.2f %% | %.2f %% |' % (k[0], k[1], p['n'], len(p['d']), med, mx, mm))
print(u'\nвсего расхождений: %d в %d парах' % (tot, len(order)))
print(u'\nкрупнейшие по |Δ| (строки с «было» ≥ 1000):')
for r in sorted((r for r in rows if abs(float(r[5])) >= 1000), reverse=True)[:12]:
    print(u'  %-22s %-16s %-12s %-16s %14s -> %14s  %s %%' % r[1:])
