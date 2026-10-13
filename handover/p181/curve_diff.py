# -*- coding: utf-8 -*-
r"""П181 (29.09.2026): кривые <Efficiency> спектров корпуса — HEAD (физика 24) против рабочего дерева (физика 25).
  python curve_diff.py <worktree> [--at=32,60] [--spec=<подстрока>]
По сцене (guid): узлов, отношение new/old по полосам энергии (медиана, худшее), шум узла; клейма.
Разделитель дробной части — точка.
"""
import collections
import math
import os
import statistics
import subprocess
import sys
import xml.etree.ElementTree as ET

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

WT = sys.argv[1]
AT = [32.0, 60.0]
SPEC = None
for a in sys.argv[2:]:
    if a.startswith('--at='):
        AT = [float(x) for x in a.split('=', 1)[1].split(',')]
    if a.startswith('--spec='):
        SPEC = a.split('=', 1)[1]
SP = os.path.join(WT, 'tools', 'CORPUS', 'corpus', 'spectra')


def parse(xml_bytes):
    root = ET.fromstring(xml_bytes)
    rd = root.find('ResultDataList/ResultData')
    eff = rd.find('Efficiency') if rd is not None else None
    if eff is None:
        return None
    pts = []
    for p in eff.findall('Curve/ROIEfficiencyData'):
        pts.append((float(p.findtext('Energy')), float(p.findtext('Efficiency')), float(p.findtext('ErrorPercent') or 'nan')))
    stamp = None
    for tag in ('Stamp', 'SimulationStamp', 'Comment', 'Name'):
        v = eff.findtext(tag)
        if v and 'phys=' in v:
            stamp = v
    if stamp is None:
        txt = ET.tostring(eff, encoding='unicode')
        i = txt.find('phys=')
        stamp = txt[i:i + 260].split('<')[0] if i >= 0 else ''
    return eff.findtext('Guid'), eff.findtext('Name'), pts, stamp


def interp(pts, e):
    for (e0, v0, s0), (e1, v1, s1) in zip(pts, pts[1:]):
        if e0 <= e <= e1 and v0 > 0 and v1 > 0:
            t = (math.log(e) - math.log(e0)) / (math.log(e1) - math.log(e0))
            return math.exp(math.log(v0) + t * (math.log(v1) - math.log(v0))), max(s0, s1)
    return float('nan'), float('nan')


changed = subprocess.run(['git', '-C', WT, 'diff', '--name-only', '--', 'tools/CORPUS/corpus/spectra'],
                         capture_output=True, text=True).stdout.split()
by_scene = collections.OrderedDict()
for rel in changed:
    name = os.path.basename(rel)[:-4]
    if SPEC and SPEC not in name:
        continue
    old = subprocess.run(['git', '-C', WT, 'show', 'HEAD:' + rel], capture_output=True).stdout
    new = open(os.path.join(WT, rel), 'rb').read()
    o = parse(old)
    n = parse(new)
    if not o or not n:
        print(u'%s: кривой нет (old %s, new %s)' % (name, bool(o), bool(n)))
        continue
    key = n[1]
    by_scene.setdefault(key, {'spectra': [], 'o': o, 'n': n})['spectra'].append(name)

bands = [(0, 40), (40, 100), (100, 3001)]
print(u'сцена                            спектров узлов(о/н) | медиана n/o−1 и худшее по полосам <40 / 40–100 / >100 кэВ | ' +
      ' | '.join(u'%g кэВ n/o−1 (шум)' % e for e in AT))
allr = []
for key, d in by_scene.items():
    o, n = d['o'], d['n']
    oe = [p[0] for p in o[2]]
    ne = [p[0] for p in n[2]]
    same_grid = oe == ne
    cells = []
    for lo, hi in bands:
        rs = []
        for (e, v, s) in n[2]:
            if lo <= e < hi and v > 0:
                ov, _ = interp(o[2], e)
                if ov and ov == ov and ov > 0:
                    rs.append(v / ov - 1.0)
        allr.extend((key, lo, r) for r in rs)
        if rs:
            worst = max(rs, key=abs)
            cells.append(u'%+.2f%% (%+.2f%%)' % (100 * statistics.median(rs), 100 * worst))
        else:
            cells.append(u'—')
    at = []
    for e in AT:
        nv, ns = interp(n[2], e)
        ov, os_ = interp(o[2], e)
        at.append(u'%+.2f%% (%.2f%%)' % (100 * (nv / ov - 1.0), ns) if ov == ov and ov > 0 else u'—')
    print(u'%-32s %3d %3d/%3d%s | %s | %s' % (key, len(d['spectra']), len(oe), len(ne), '' if same_grid else u' сетка≠',
                                            ' / '.join(cells), ' | '.join(at)))
print(u'клеймо было: %s' % list(by_scene.values())[0]['o'][3][:300])
print(u'клеймо стало: %s' % list(by_scene.values())[0]['n'][3][:300])
for lo, hi in bands:
    rs = [r for k, l, r in allr if l == lo]
    if rs:
        print(u'все сцены, %g–%g кэВ: узлов %d, медиана %+.3f%%, |Δ| медиана %.3f%%, худшее %+.2f%%'
              % (lo, hi, len(rs), 100 * statistics.median(rs), 100 * statistics.median(abs(r) for r in rs),
                 100 * max(rs, key=abs)))
