# -*- coding: utf-8 -*-
r"""П114 — кривые физики 22 против физики 21 по узлам `<Efficiency>` спектров понятной части (образец — П50 §6.1
curves_vs17.py): worktree (phys=22) против блоба HEAD d318dfd0 (phys=21). Δ = (22 − 21) / 21 по точкам от 20 кэВ;
медиана |Δ| по всем точкам и медиана/мин/макс Δ у энергий 60, 100, 300, 662, 1461, 2615 (ближайшая точка).
  python curves_vs21.py
"""
import glob
import io
import math
import os
import re
import statistics
import subprocess
import sys
import xml.etree.ElementTree as ET

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

WT = r'D:\BqMoni_Claude\p114\wt'
BASE = 'd318dfd0'
SPECTRA = os.path.join(WT, 'tools', 'CORPUS', 'corpus', 'spectra')


def curve_of(text):
    # ⚠ ловушка T30: внутри узла <Efficiency> стоят теги <Efficiency> точек — регулярное выражение
    # до первого </Efficiency> режет узел; берём узел разбором всего документа.
    root = ET.fromstring(text.encode('utf-8'))
    node = root.find('ResultDataList/ResultData/Efficiency')
    if node is None:
        return None, None
    stamp = node.findtext('ComputeStamp') or ''
    pts = []
    for e in node.iter('ROIEfficiencyData'):
        try:
            pts.append((float(e.findtext('Energy')), float(e.findtext('Efficiency'))))
        except (TypeError, ValueError):
            pass
    return stamp, sorted(pts)


def main():
    all_d = []
    at = {60: [], 100: [], 300: [], 662: [], 1461: [], 2615: []}
    n = 0
    stamps = set()
    for p in sorted(glob.glob(os.path.join(SPECTRA, '*.xml'))):
        rel = 'tools/CORPUS/corpus/spectra/' + os.path.basename(p)
        new = io.open(p, encoding='utf-8', errors='replace').read()
        s_new, c_new = curve_of(new)
        if not c_new or 'phys=22' not in s_new:
            continue
        old = subprocess.run(['git', 'show', BASE + ':' + rel], cwd=WT, capture_output=True).stdout.decode('utf-8', 'replace')
        s_old, c_old = curve_of(old)
        if not c_old:
            continue
        stamps.add(re.sub(r'grid=[^;]*;', 'grid=…;', s_new))
        n += 1
        d_e = {}
        for (e1, v1), (e0, v0) in zip(c_new, c_old):
            if abs(e1 - e0) > 1e-6 or e1 < 20 or v0 <= 0:
                continue
            d = (v1 / v0 - 1.0) * 100.0
            all_d.append(abs(d))
            d_e[e1] = d
        for e in at:
            if d_e:
                k = min(d_e, key=lambda x: abs(x - e))
                if abs(k - e) < 0.15 * e:
                    at[e].append(d_e[k])
    print(u'спектров сравнено: %d; точек от 20 кэВ: %d; медиана |Δ| %.3f %%' % (n, len(all_d), statistics.median(all_d)))
    for e in sorted(at):
        v = at[e]
        if v:
            print(u'  %5d кэВ: медиана %+.2f %% (мин %+.2f … макс %+.2f), n=%d' % (e, statistics.median(v), min(v), max(v), len(v)))
    print(u'клейма (сетка свёрнута):')
    for s in sorted(stamps):
        print(u'  ' + s)
    return 0


if __name__ == '__main__':
    sys.exit(main())
