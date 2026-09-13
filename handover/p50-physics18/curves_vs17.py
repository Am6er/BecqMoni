# -*- coding: utf-8 -*-
r"""Кривые физики 18 (worktree p50) против кривых физики 17 (основное дерево) — ТОЛЬКО ЧТЕНИЕ (П50, 14.09.2026).

Читает узел `<Efficiency><Curve>` из каждого спектра понятной части (85 файлов, у которых в
worktree клеймо `phys=18`), сопоставляет с тем же спектром основного дерева (`phys=17`) по
энергии, печатает: медиану |Δ| по всем точкам всех кривых, медиану Δ (со знаком) по энергиям
662 / 1461 / 2615, и по спектрам — наибольшее |Δ| и где. Разделитель дробной части — точка.

  python curves_vs17.py > <out>
"""
import io
import os
import re
import sys
import statistics

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
WT = r'D:\BqMoni_Claude\p50\wt'
SP = os.path.join('tools', 'CORPUS', 'corpus', 'spectra')
RX = re.compile(r'<ROIEfficiencyData><Energy>([0-9.]+)</Energy><Efficiency>([-0-9.E+e]+)</Efficiency>')
STAMP = re.compile(r'<ComputeStamp>([^<]*)</ComputeStamp>')


def curve(path):
    t = io.open(path, encoding='utf-8', newline='').read()
    m = STAMP.search(t)
    if not m:
        return None, None
    return m.group(1), {float(e): float(v) for e, v in RX.findall(t)}


def main():
    names = sorted(f for f in os.listdir(os.path.join(WT, SP)) if f.endswith('.xml'))
    all_d = []
    at = {662.0: [], 1461.0: [], 2615.0: [], 60.0: [], 100.0: [], 300.0: []}
    per = []
    n = 0
    for f in names:
        s18, c18 = curve(os.path.join(WT, SP, f))
        if not s18 or not s18.startswith('phys=18;'):
            continue
        s17, c17 = curve(os.path.join(ROOT, SP, f))
        if not s17 or not s17.startswith('phys=17;'):
            print(u'%-32s в основном дереве не phys=17: %s' % (f, s17))
            continue
        n += 1
        ds = []
        worst = (0.0, 0.0)
        for e, v18 in sorted(c18.items()):
            v17 = c17.get(e)
            if v17 is None or v17 <= 0 or e < 20:
                continue
            d = 100.0 * (v18 - v17) / v17
            ds.append(d)
            all_d.append(abs(d))
            if e in at:
                at[e].append(d)
            if abs(d) > abs(worst[1]):
                worst = (e, d)
        per.append((f[:-4], statistics.median(ds) if ds else float('nan'), worst))
    print(u'спектров сравнено: %d (phys=18 против phys=17), точек от 20 кэВ: %d' % (n, len(all_d)))
    print(u'медиана |Δ| по всем точкам: %.3f %%' % statistics.median(all_d))
    for e in sorted(at):
        v = at[e]
        if v:
            print(u'  %6.0f кэВ: медиана Δ %+.3f %%, мин %+.3f, макс %+.3f (n=%d)' % (
                e, statistics.median(v), min(v), max(v), len(v)))
    print(u'%-32s %10s %22s' % (u'спектр', u'медиана Δ,%', u'худшая точка (кэВ, Δ%)'))
    for name, med, (we, wd) in per:
        print(u'%-32s %+10.3f %10.0f %+10.3f' % (name, med, we, wd))
    return 0


if __name__ == '__main__':
    sys.exit(main())
