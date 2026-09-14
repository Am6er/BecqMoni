# -*- coding: utf-8 -*-
"""П42 13.09.2026, `AMBER22` — форма КОНТИНУУМА образа против Geant4 на сцене диска: наш сырой отклик физики 17
(`ours17_*.csv`, G4RawProbe --no-light, бин 1 кэВ) и физики 16 (`handover/p26-amber22/g4/ours_*.csv`, П26) против
тех же логов арбитра (`handover/p26-amber22/g4/g4_*_air.log`, HIST). Печатает пик (окно ±3 бина), полную, и
континуум ПОЛОСАМИ по 100 кэВ (наша/G4 − 1, шум G4 в полосе) — П26 §2 мерил лишь четверти E.

    python handover/p42-amber22/g4/cmp42.py
"""
import io
import os
import re
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
P26 = os.path.join(os.path.dirname(os.path.dirname(HERE)), 'p26-amber22', 'g4')


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


def main():
    for e in ('238.632', '583.187', '911.204', '2614.511'):
        o17p = os.path.join(HERE, 'ours17_AS80_th_disk_%s.csv' % e)
        o16p = os.path.join(P26, 'ours_AS80_th_disk_%s.csv' % e)
        g4p = os.path.join(P26, 'g4_AS80_th_disk_%s_air.log' % e)
        if not (os.path.exists(o17p) and os.path.exists(g4p)):
            print('== %s: нет файлов' % e)
            continue
        o17 = read_ours(o17p)
        o16 = read_ours(o16p) if os.path.exists(o16p) else None
        g4, g4c, dec = read_g4(g4p)
        p = max(o17) + 1
        pk = p - 1
        print('== %s кэВ (бин пика %d, Geant4 историй %d)' % (e, pk, dec))
        w17 = sum(o17.get(k, 0.0) for k in range(pk - 3, pk + 1))
        wg = sum(g4.get(k, 0.0) for k in range(pk - 3, pk + 1))
        w16 = sum(o16.get(k, 0.0) for k in range(pk - 3, pk + 1)) if o16 else float('nan')
        print('   пик ±3:  физ17/G4 %+.2f %%   физ16/G4 %+.2f %%   (G4 %.4E)' % (100 * (w17 / wg - 1), 100 * (w16 / wg - 1), wg))
        t17, tg = sum(o17.values()), sum(g4.values())
        t16 = sum(o16.values()) if o16 else float('nan')
        print('   полная:  физ17/G4 %+.2f %%   физ16/G4 %+.2f %%' % (100 * (t17 / tg - 1), 100 * (t16 / tg - 1)))
        c17, cg = t17 - w17, tg - wg
        c16 = t16 - w16
        print('   континуум (без ±3): физ17/G4 %+.2f %%   физ16/G4 %+.2f %%   P/T физ17 %.4f G4 %.4f' % (
            100 * (c17 / cg - 1), 100 * (c16 / cg - 1), w17 / t17, wg / tg))
        print('   %-11s %10s %10s %10s %8s' % ('полоса', 'физ17/G4', 'физ16/G4', 'G4 доля', 'шум G4'))
        step = 100 if pk > 700 else 50
        lo = 0
        while lo < pk - 3:
            hi = min(lo + step, pk - 3)
            a, b, c = band(o17, lo, hi), band(g4, lo, hi), band(g4c, lo, hi)
            b16 = band(o16, lo, hi) if o16 else float('nan')
            print('   %4d-%-6d %+9.2f %% %+9.2f %% %9.4f %7.2f %%' % (lo, hi, 100 * (a / b - 1) if b else 0, 100 * (b16 / b - 1) if b else 0, b / tg, 100 / c ** 0.5 if c else 0))
            lo = hi


if __name__ == '__main__':
    main()
