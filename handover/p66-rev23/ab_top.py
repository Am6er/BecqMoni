# -*- coding: utf-8 -*-
"""П66 (копия П51): из вывода per_spectrum.py — лучшие/худшие по Δχ²/ndf, счёт побитовых, медиана невязки.

    python handover/p66-rev23/ab_top.py handover/p66-rev23/ab_rev21_vs_a_full.txt [N]
"""
import io, re, statistics, sys
sys.stdout.reconfigure(encoding='utf-8')
path = sys.argv[1]
n = int(sys.argv[2]) if len(sys.argv) > 2 else 8
rows = []
for line in io.open(path, encoding='utf-8', newline=''):
    m = re.match(r'^(\S+)\s+(known|unknown)\s+([\d.]+)\s+([\d.]+)\s+([+-][\d.]+)\s+\|\s+([\d.]+)\s+([\d.]+)\s+\|\s+([\d.]+)\s+([\d.]+)', line)
    if m:
        rows.append((m.group(1), m.group(2)) + tuple(float(m.group(i)) for i in range(3, 10)))
rows.sort(key=lambda r: r[4])
print(u'изменившихся строк: %d; |Δ| <= 0.005: %d; Δ > +0.05: %d; Δ < -0.05: %d; Σ Δ = %+.3f'
      % (len(rows), sum(abs(r[4]) <= 0.005 for r in rows), sum(r[4] > 0.05 for r in rows),
         sum(r[4] < -0.05 for r in rows), sum(r[4] for r in rows)))
if rows:
    print(u'медиана невязки модели: A %.1f %% -> B %.1f %%'
          % (statistics.median(r[7] for r in rows), statistics.median(r[8] for r in rows)))
print(u'лучшие по Δχ²/ndf:')
for r in rows[:n]:
    print(u'  %-24s %7.3f -> %7.3f  %+.3f   χ²p %6.1f -> %6.1f   нев %5.1f -> %5.1f' % (r[0], r[2], r[3], r[4], r[5], r[6], r[7], r[8]))
print(u'худшие:')
for r in rows[-n:]:
    print(u'  %-24s %7.3f -> %7.3f  %+.3f   χ²p %6.1f -> %6.1f   нев %5.1f -> %5.1f' % (r[0], r[2], r[3], r[4], r[5], r[6], r[7], r[8]))
