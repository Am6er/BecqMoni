# -*- coding: utf-8 -*-
"""П26 12.09.2026, `AMBER22` п. 1 — итоговая таблица: доля пика наша против Geant4 (окно пика ±3 бина,
как П20 §3 / 02.09 §9в; полная = сумма отклика; P/T = пик/полная). Читает ours_*.csv и g4_*.log из g4\\."""
import io
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))


def ours(p):
    h = {}
    for l in io.open(p, encoding='utf-8-sig'):
        a = l.strip().split(',')
        if len(a) == 2 and a[0] != 'keV':
            h[int(round(float(a[0])))] = float(a[1])
    return h


def g4(p):
    d = None
    h = {}
    for l in io.open(p, encoding='utf-8', errors='replace'):
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', l)
        if m:
            d = int(m.group(3))
        m = re.match(r'HIST (\d+) (\d+)', l)
        if m:
            h[int(m.group(1))] = int(m.group(2))
    return h, d


RUNS = [(661.657, 'AS80_bare_gap5_661.657', 'AS80_bare_gap5_661.657_vacuum', 'контроль: голый AS80, точка 5 мм, vacuum'),
        (238.632, 'AS80_th_disk_238.632', 'AS80_th_disk_238.632_air', 'диск, воздух'),
        (583.187, 'AS80_th_disk_583.187', 'AS80_th_disk_583.187_air', 'диск, воздух'),
        (911.204, 'AS80_th_disk_911.204', 'AS80_th_disk_911.204_air', 'диск, воздух'),
        (2614.511, 'AS80_th_disk_2614.511', 'AS80_th_disk_2614.511_air', 'диск, воздух'),
        (583.187, 'AS80_th_disk_583.187', 'AS80_th_disk_583.187_vacuum', 'диск, vacuum (вилка)'),
        (2614.511, 'AS80_th_disk_2614.511', 'AS80_th_disk_2614.511_vacuum', 'диск, vacuum (вилка)')]

print('| узел, кэВ | сцена | ε_пик наша | ε_пик G4 | Δ пик | ε_полн наша | ε_полн G4 | Δ полн | P/T наша | P/T G4 | Δ P/T | шум G4 (отсч.) |')
print('|---|---|---|---|---|---|---|---|---|---|---|---|')
for e, o, g, note in RUNS:
    ho = ours(os.path.join(HERE, 'ours_%s.csv' % o))
    hg, d = g4(os.path.join(HERE, 'g4_%s.log' % g))
    p = max(ho)
    po = sum(ho.get(k, 0) for k in range(p - 3, p + 1))
    to = sum(ho.values())
    cg = sum(hg.get(k, 0) for k in range(p - 3, p + 1))
    pg = cg / d
    tg = sum(hg.values()) / d
    print('| %.3f | %s | %.5f | %.5f | **%+.2f %%** | %.5f | %.5f | %+.2f %% | %.4f | %.4f | %+.2f %% | %.2f %% (%d) |' % (
        e, note, po, pg, 100 * (po / pg - 1), to, tg, 100 * (to / tg - 1), po / to, pg / tg,
        100 * ((po / to) / (pg / tg) - 1), 100 / cg ** 0.5, cg))
