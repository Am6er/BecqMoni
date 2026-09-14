# -*- coding: utf-8 -*-
r"""П76 — кривые `<Efficiency>` двух контактных спектров RC103 ДО (узел из git HEAD) и ПОСЛЕ (рабочее дерево,
зазор 3.5 мм): ε по узлам и отношение после/до. Разделитель дробной части — точка.

    python handover/p76-e43/curves_ab.py [--csv=<файл>]
"""
import io
import os
import re
import subprocess
import sys

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
SPECTRA = 'tools/CORPUS/corpus/spectra/'
KEYS = ('RC103_Cs137_0cm', 'RC103_Lu176')
SHOW = (30.0, 60.0, 100.0, 186.0, 300.0, 352.0, 662.0, 1000.0, 1461.0, 2615.0)
POINT = re.compile(r'<ROIEfficiencyData><Energy>([^<]+)</Energy><Efficiency>([^<]+)</Efficiency>')


def curve(text):
    # внешний узел, а не внутренний `<Efficiency>значение</Efficiency>` точки кривой
    m = re.search(r'<Efficiency><Guid>.*?</UseResponseMatrix></Efficiency>', text, re.S)
    if not m:
        return {}, None
    gap = re.search(r'<FrontGapThickness>([^<]+)</FrontGapThickness>', m.group(0))
    return dict((float(e), float(v)) for e, v in POINT.findall(m.group(0))), (gap.group(1) if gap else '?')


def main():
    out = None
    for a in sys.argv[1:]:
        if a.startswith('--csv='):
            out = a[6:]
    rows = []
    for k in KEYS:
        path = SPECTRA + k + '.xml'
        before = subprocess.run(['git', '-C', ROOT, 'show', 'HEAD:' + path], stdout=subprocess.PIPE).stdout.decode('utf-8', 'replace')
        after = io.open(os.path.join(ROOT, *path.split('/')), encoding='utf-8-sig').read()
        ca, ga = curve(before)
        cb, gb = curve(after)
        print(u'== %s: зазор %s -> %s мм, узлов %d -> %d' % (k, ga, gb, len(ca), len(cb)))
        print(u'%8s %14s %14s %8s' % (u'E, кэВ', u'до', u'после', u'после/до'))
        for e in sorted(set(ca) | set(cb)):
            a, b = ca.get(e), cb.get(e)
            r = (b / a) if (a and b) else float('nan')
            rows.append((k, e, a, b, r))
            if e in SHOW or e in (306.8, 201.8):
                print(u'%8.1f %14.5e %14.5e %8.3f' % (e, a if a else float('nan'), b if b else float('nan'), r))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write(u'spectrum,energy_kev,eff_before,eff_after,ratio\n')
            for k, e, a, b, r in rows:
                fh.write(u'%s,%r,%r,%r,%r\n' % (k, e, a, b, r))
    return 0


if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass
    sys.exit(main())
