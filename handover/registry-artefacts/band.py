# -*- coding: utf-8 -*-
"""A85: вклад канала L-вылета в отклик ГОЛОГО кристалла NaI 80x80.

Считает полосу 55...59 кэВ (и соседей) по плечам прогонов G4RawProbe и
кладёт рядом дамп арбитра Geant4 (g4cf, vacuum, 5 млн).
Разброс по зёрнам = шум замера; ошибка арбитра = пуассон по счёту.
"""
import io, os, re, sys, math, glob
sys.stdout.reconfigure(encoding='utf-8')

HERE = os.path.dirname(os.path.abspath(__file__))
SEEDS = [20260901, 20260903, 20260905, 20260907, 20260909, 20260911]
ARMS = ['on', 'off', 'on_noesc', 'off_noesc']
BANDS = [(1, 12), (13, 25), (26, 31), (32, 54), (55, 59), (60, 60)]


def load_csv(path):
    v = {}
    for l in io.open(path, encoding='utf-8-sig'):
        l = l.strip()
        if not l or l.startswith('keV'):
            continue
        k, r = l.split(',')
        v[int(round(float(k)))] = float(r)
    return v


def load_g4(path):
    h, dec = {}, None
    for l in io.open(path, encoding='utf-8', errors='replace'):
        if l.startswith('HISTBEGIN'):
            dec = float(l.split('decays=')[1])
        elif l.startswith('HIST ') :
            p = l.split()
            h[int(p[1])] = int(p[2])
    return h, dec


def band(v, a, b):
    return sum(v.get(i, 0.0) for i in range(a, b + 1))


def stat(xs):
    n = len(xs)
    m = sum(xs) / n
    sd = math.sqrt(sum((x - m) ** 2 for x in xs) / (n - 1)) if n > 1 else 0.0
    return m, sd, sd / math.sqrt(n)


g4, dec = load_g4(os.path.join(HERE, 'g4_bare_vac_a85.txt'))
print('арбитр: g4cf vacuum, %g распадов' % dec)
for a, b in BANDS:
    c = sum(g4.get(i, 0) for i in range(a, b + 1))
    print('  %2d...%2d кэВ: %.5e  (счёт %d, пуассон %.1f %%)'
          % (a, b, c / dec, c, 100.0 * math.sqrt(c) / c if c else 0.0))

print()
res = {}
for arm in ARMS:
    per = {bd: [] for bd in BANDS}
    lx = []
    for s in SEEDS:
        v = load_csv(os.path.join(HERE, '%s_%d.csv' % (arm, s)))
        for bd in BANDS:
            per[bd].append(band(v, *bd))
        log = io.open(os.path.join(HERE, '%s_%d.log' % (arm, s)),
                      encoding='utf-8', errors='replace').read()
        m = re.search(r'L-квантов (\d+)', log)
        lx.append(int(m.group(1)) if m else -1)
    res[arm] = {bd: stat(per[bd]) for bd in BANDS}
    print('плечо %-10s  L-квантов по зёрнам: %s' % (arm, lx))
    for bd in BANDS:
        m, sd, se = res[arm][bd]
        print('   %2d...%2d: %.5e  ± %.2e (СКО по 6 зёрнам, %.2f %%),'
              ' ош.среднего %.2e' % (bd[0], bd[1], m, sd,
                                     100.0 * sd / m if m else 0.0, se))
    print()

# Вклад канала = разность плеч, ошибка разности = квадратичная сумма СКО.
print('=== ВКЛАД L-КАНАЛА (разность плеч), полоса 55...59 кэВ ===')
for pair in [('on', 'off'), ('on_noesc', 'off_noesc')]:
    m1, sd1, _ = res[pair[0]][(55, 59)]
    m2, sd2, _ = res[pair[1]][(55, 59)]
    d = m1 - m2
    sd = math.sqrt(sd1 ** 2 + sd2 ** 2)
    print('  %s - %s = %.4e ± %.2e  (%.1f сигм)'
          % (pair[0], pair[1], d, sd, d / sd if sd else 0))

g4b = sum(g4.get(i, 0) for i in range(55, 60)) / dec
print()
print('=== РЯДОМ С АРБИТРОМ (полоса 55...59 кэВ) ===')
print('  арбитр Geant4          %.4e' % g4b)
for arm in ARMS:
    m, sd, _ = res[arm][(55, 59)]
    print('  наш, плечо %-10s %.4e ± %.2e   доля арбитра %.1f %%'
          % (arm, m, sd, 100.0 * m / g4b))
