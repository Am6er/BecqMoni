# -*- coding: utf-8 -*-
"""ε пика из узла <Efficiency> спектра: python eff_read.py <xml> [кэВ...] — линейная интерполяция в log-log по узлам кривой."""
import sys, io, math, re
import xml.etree.ElementTree as ET

def curve(path):
    root = ET.parse(path).getroot()
    eff = root.find('ResultDataList/ResultData/Efficiency')
    if eff is None:
        return None, None, []
    pts = [(float(p.findtext('Energy')), float(p.findtext('Efficiency')), float(p.findtext('ErrorPercent') or 0))
           for p in eff.find('Curve')]
    return eff.findtext('Name'), eff.findtext('ComputeStamp'), pts

def at(pts, e):
    for (e0, v0, s0), (e1, v1, s1) in zip(pts, pts[1:]):
        if e0 <= e <= e1:
            if e0 == e: return v0, s0
            t = (math.log(e) - math.log(e0)) / (math.log(e1) - math.log(e0))
            return math.exp(math.log(v0) + t * (math.log(v1) - math.log(v0))), s0 + t * (s1 - s0)
    return float('nan'), 0

if __name__ == '__main__':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    path = sys.argv[1]
    want = [float(x) for x in sys.argv[2:]] or [661.657]
    name, stamp, pts = curve(path)
    print('%s: сцена %s, клеймо %s, узлов %d' % (path, name, stamp, len(pts)))
    for e in want:
        v, s = at(pts, e)
        print('   ε(%.1f) = %.6e (±%.2f %% МК)' % (e, v, s))
