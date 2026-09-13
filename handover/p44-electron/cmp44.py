# -*- coding: utf-8 -*-
"""П44 13.09.2026 — плечи ключей `ecomp`/`bpath` против арбитра Geant4 (логи П26/П27/П32, HIST, бин 1 кэВ).
Печатает пик (окно ±3 бина), полную, континуум, P/T, вылеты 511/1022 (окно ±3 бина у E−511 / E−1022,
только выше порога пар) и полосы континуума: наша/G4 − 1 по каждому плечу, шум G4 в полосе.

    python cmp44.py <set>      # bare | disk | point0
"""
import io
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
ROOT = 'C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/handover/'
OURS = 'D:/BqMoni_Claude/p44/g4/'
ARMS = ['off', 'bpath1', 'bpath2', 'ecomp', 'both']

G4 = {
    ('AS80_bare_gap5', '661.657'): ROOT + 'p26-amber22/g4/g4_AS80_bare_gap5_661.657_vacuum.log',
    ('AS80_bare_gap5', '2614.511'): ROOT + 'p27-electron-transport/g4/g4_AS80_bare_gap5_2614.511.log',
    ('RC103_bare_gap5', '661.657'): ROOT + 'p27-electron-transport/g4/g4_RC103_bare_gap5_661.657.log',
    ('RC103_bare_gap5', '2614.511'): ROOT + 'p27-electron-transport/g4/g4_RC103_bare_gap5_2614.511.log',
    ('AS80_th_disk', '2614.511'): ROOT + 'p26-amber22/g4/g4_AS80_th_disk_2614.511_air.log',
    ('AS80_th_disk', '583.187'): ROOT + 'p26-amber22/g4/g4_AS80_th_disk_583.187_air.log',
    ('AS80_point0', '661.657'): ROOT + 'p32-g4/g4/g4_AS80_point0_661.657_air.log',
    ('AS80_point0', '1332.5'): ROOT + 'p32-g4/g4/g4_AS80_point0_1332.5_air.log',
}
SETS = {
    'bare': [('AS80_bare_gap5', '661.657'), ('AS80_bare_gap5', '2614.511'),
             ('RC103_bare_gap5', '661.657'), ('RC103_bare_gap5', '2614.511')],
    'disk': [('AS80_th_disk', '2614.511'), ('AS80_th_disk', '583.187')],
    'point0': [('AS80_point0', '661.657'), ('AS80_point0', '1332.5')],
}
BANDS = {
    '661.657': [(0, 165), (165, 330), (330, 495), (495, 659)],
    '583.187': [(0, 50), (50, 100), (100, 150), (150, 200), (200, 300), (300, 400), (400, 500), (500, 580)],
    '1332.5': [(0, 100), (100, 200), (200, 333), (333, 666), (666, 1000), (1000, 1200), (1200, 1330)],
    '2614.511': [(0, 100), (100, 200), (200, 300), (300, 400), (400, 500), (500, 600), (600, 700), (700, 800),
                 (800, 900), (900, 1000), (1000, 1200), (1200, 1500), (1500, 2000), (2000, 2100), (2100, 2500),
                 (2500, 2612)],
}


def read_ours(path):
    h = {}
    for line in io.open(path, encoding='utf-8-sig'):
        parts = line.strip().split(',')
        if len(parts) != 2 or parts[0] == 'keV':
            continue
        h[int(round(float(parts[0])))] = float(parts[1])
    return h


def read_g4(path):
    decays = None
    h = {}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m:
            decays = int(m.group(3))
        m = re.match(r'HIST (\d+) (\d+)', line)
        if m:
            h[int(m.group(1))] = int(m.group(2))
    return {k: v / decays for k, v in h.items()}, h, decays


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


def window(h, c, w=3):
    return sum(h.get(k, 0.0) for k in range(c - w, c + w + 1))


def pct(a, b):
    return 100.0 * (a / b - 1.0) if b else float('nan')


def main():
    which = sys.argv[1] if len(sys.argv) > 1 else 'bare'
    for g, e in SETS[which]:
        g4p = G4[(g, e)]
        if not os.path.exists(g4p):
            print('== %s %s: нет лога Geant4 %s' % (g, e, g4p))
            continue
        g4, g4c, dec = read_g4(g4p)
        ours = {}
        for arm in ARMS:
            p = OURS + '%s/ours_%s_%s_%s.csv' % (which, g, e, arm)
            if os.path.exists(p):
                ours[arm] = read_ours(p)
        if not ours:
            print('== %s %s: наших файлов нет' % (g, e))
            continue
        arms = [a for a in ARMS if a in ours]
        any_h = ours[arms[0]]
        p = max(any_h) + 1
        pk = p - 1
        print('== %s %s кэВ (бин пика %d, Geant4 историй %d, %s)' % (g, e, pk, dec, os.path.basename(g4p)))
        head = '   %-22s' % 'величина' + ''.join('%12s' % a for a in arms) + '%12s' % 'G4'
        print(head)
        wg = window(g4, pk)
        tg = sum(g4.values())

        def row(name, f, gval, fmt='%+11.2f%%'):
            print('   %-22s' % name + ''.join(fmt % f(ours[a]) for a in arms) + '%12.4E' % gval)

        row('пик ±3', lambda h: pct(window(h, pk), wg), wg)
        row('полная', lambda h: pct(sum(h.values()), tg), tg)
        row('континуум (без ±3)', lambda h: pct(sum(h.values()) - window(h, pk), tg - wg), tg - wg)
        print('   %-22s' % 'P/T' + ''.join('%12.4f' % (window(ours[a], pk) / sum(ours[a].values())) for a in arms)
              + '%12.4f' % (wg / tg))
        ef = float(e)
        if ef > 1022:
            for name, c in (('вылет 511 ±3', int(round(ef - 511))), ('вылет 1022 ±3', int(round(ef - 1022)))):
                gw = window(g4, c)
                row(name, lambda h, c=c, gw=gw: pct(window(h, c), gw), gw)
        print('   %-22s' % 'полоса' + ''.join('%12s' % a for a in arms) + '%12s%9s' % ('G4 доля', 'шум G4'))
        for lo, hi in BANDS[e]:
            hi = min(hi, pk - 3)
            if hi <= lo:
                continue
            b = band(g4, lo, hi)
            c = band(g4c, lo, hi)
            print('   %5d-%-16d' % (lo, hi) + ''.join('%+11.2f%%' % pct(band(ours[a], lo, hi), b) for a in arms)
                  + '%12.4f%8.2f%%' % (b / tg, 100 / c ** 0.5 if c else 0))
        print()


if __name__ == '__main__':
    main()
