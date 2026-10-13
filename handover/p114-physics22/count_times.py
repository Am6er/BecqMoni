# -*- coding: utf-8 -*-
r"""П114 — времена сцен по логам обоих заходов (count.log 19.09, count2.log 21.09) -> art\count_times.csv и сводка."""
import io
import re
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
LANE = r'D:\BqMoni_Claude\p114'
rows = []
for log in ('count.log', 'count2.log'):
    t = io.open(LANE + '\\' + log, encoding='utf-8', errors='replace').read()
    for m in re.finditer(r'^== (\S+) ==\n\s*клеймо\s*:\s*\S+\n\s*время\s*:\s*([\d.]+) с на часах, ядер ([\d.]+)', t, re.M):
        rows.append((m.group(1), float(m.group(2)), float(m.group(3)), log))
with io.open(LANE + r'\art\count_times.csv', 'w', encoding='utf-8', newline='') as f:
    f.write('scene,seconds,cores,log\n' + ''.join('%s,%.1f,%.1f,%s\n' % r for r in rows))
print(len(rows), 'сцен; всего %.1f мин чистого счёта' % (sum(r[1] for r in rows) / 60))


def grp(pat):
    v = [r[1] for r in rows if re.search(pat, r[0])]
    return ('%.0f…%.0f с (%d)' % (min(v), max(v), len(v))) if v else None


for name, pat in (('Дента', 'denta'), ('маринелли', 'mar1l'), ('Петри', 'petri'), ('RC103', 'RC103'), ('AS80/ASN16', 'AS80|ASN16'), ('G1S точки', 'G1S_point')):
    print(name, grp(pat))
print('ядер мин/макс', min(r[2] for r in rows), max(r[2] for r in rows))
print('RC103_point0', [r for r in rows if r[0] == 'RC103_point0'])
print('заход 1: %d сцен, %.1f мин; заход 2: %d сцен, %.1f мин' % (
    sum(1 for r in rows if r[3] == 'count.log'), sum(r[1] for r in rows if r[3] == 'count.log') / 60,
    sum(1 for r in rows if r[3] == 'count2.log'), sum(r[1] for r in rows if r[3] == 'count2.log') / 60))
