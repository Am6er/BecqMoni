# -*- coding: utf-8 -*-
"""Положительный контроль критерия A82: каково распределение k/локальная медиана.

Сторож с порогом 2x обязан быть проверен на чувствительность: если при
пороге 1.3x находок тоже ноль, критерий не молчит по делу, а слеп.
"""
import csv
import io
import sys
from collections import defaultdict

path = sys.argv[1]
JUDGE_FROM = 50.0
EMPTY = 99.0
WINDOW = 5

scenes = defaultdict(list)
with io.open(path, encoding='utf-8-sig', newline='') as f:
    for row in csv.DictReader(f):
        scenes[row['scene']].append((float(row['energy_kev']),
                                     float(row['noise_pct']),
                                     float(row['k']) if row['k'] not in ('NaN', '') else 0.0))

ratios = []
for scene, nodes in scenes.items():
    ks = []
    for e, err, k in nodes:
        judged = k > 0.0 and err < EMPTY and e >= JUDGE_FROM
        ks.append(k if judged else 0.0)
    for i, k in enumerate(ks):
        if k <= 0.0:
            continue
        win = [ks[j] for j in range(max(0, i - WINDOW), min(len(ks), i + WINDOW + 1))
               if j != i and ks[j] > 0.0]
        if len(win) < 3:
            continue
        win.sort()
        loc = win[len(win) // 2]
        if loc > 0.0:
            ratios.append((k / loc, scene, nodes[i][0]))

ratios.sort(reverse=True)
n = len(ratios)
print(u'судимых узлов со сравнением: %d по %d сценам' % (n, len(scenes)))
for thr in (1.1, 1.2, 1.3, 1.5, 2.0, 3.0):
    hi = sum(1 for r, _, _ in ratios if r > thr or r < 1.0 / thr)
    print(u'  порог %.1fx: вне хода %d узлов (%.2f %%)' % (thr, hi, 100.0 * hi / n))

print(u'\n  десять самых отклонившихся:')
for r, scene, e in ratios[:10]:
    print(u'    %-34s %8.1f кэВ  %.2fx' % (scene, e, r))
print(u'\n  десять самых просевших:')
for r, scene, e in ratios[-10:]:
    print(u'    %-34s %8.1f кэВ  %.2fx' % (scene, e, r))
