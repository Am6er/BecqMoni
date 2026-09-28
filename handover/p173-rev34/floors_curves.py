# -*- coding: utf-8 -*-
"""П173: пол полосы FSA по кривым корпуса при долях 0.01/0.05/0.10 — тем же правилом, что
FsaEfficiency.FloorAtFraction (максимум в логарифмах, первое пересечение уровня лог-лог
интерполянтом). Печатает по каждой кривой (одна строка на имя кривой) пол и отношение
ε/max на линиях L-рентгена Np (Am-241) и 26.34 кэВ.

    python floors_curves.py <каталог spectra> [фильтр подстрокой]
"""
import math, os, re, sys
from collections import OrderedDict

LINES = [11.87, 13.95, 16.84, 17.75, 20.78, 26.34]
FRACS = [0.01, 0.03, 0.05, 0.10]

def curve_of(path):
    t = open(path, encoding='utf-8-sig', errors='replace').read()
    m = re.search(r'<Efficiency>.*?<Name>([^<]*)</Name>.*?<Curve>(.*?)</Curve>', t, re.S)
    if not m:
        return None, None
    pts = [(float(e), float(v)) for e, v in
           re.findall(r'<Energy>([^<]+)</Energy><Efficiency>([^<]+)</Efficiency>', m.group(2))]
    pts = [(e, v) for e, v in pts if e > 0 and v > 0]
    return m.group(1), pts

def floor(pts, frac):
    le = [math.log(e) for e, _ in pts]; lv = [math.log(v) for _, v in pts]
    want = max(lv) + math.log(frac)
    for i in range(len(lv)):
        if lv[i] >= want:
            if i > 0 and lv[i] > lv[i - 1]:
                t = (want - lv[i - 1]) / (lv[i] - lv[i - 1])
                return math.exp(le[i - 1] + t * (le[i] - le[i - 1]))
            return math.exp(le[i])
    return 0.0

def rel(pts, e):
    top = max(v for _, v in pts)
    for (e0, v0), (e1, v1) in zip(pts, pts[1:]):
        if e0 <= e <= e1:
            t = (math.log(e) - math.log(e0)) / (math.log(e1) - math.log(e0))
            return math.exp(math.log(v0) + t * (math.log(v1) - math.log(v0))) / top
    return float('nan')

def main():
    d = sys.argv[1]; flt = sys.argv[2] if len(sys.argv) > 2 else ''
    seen = OrderedDict()
    for f in sorted(os.listdir(d)):
        if not f.endswith('.xml') or flt not in f:
            continue
        name, pts = curve_of(os.path.join(d, f))
        if not pts:
            continue
        seen.setdefault(name, (pts, []))[1].append(f[:-4])
    hdr = 'кривая'.ljust(28) + ''.join(('пол@%.2f' % x).rjust(10) for x in FRACS) + \
          ''.join(('%.2f' % e).rjust(8) for e in LINES) + '  спектры'
    print(hdr)
    print(' ' * 28 + ' ' * 30 + '  ε/max, %  (срезано при 0.10 — «*»)')
    for name, (pts, specs) in seen.items():
        fl = [floor(pts, x) for x in FRACS]
        cells = ''
        for e in LINES:
            r = rel(pts, e)
            mark = '*' if e < fl[3] else ' '
            cells += ('%6.2f%s' % (100 * r, mark)).rjust(8)
        print(name[:27].ljust(28) + ''.join(('%10.2f' % x) for x in fl) + cells + '  ' + ','.join(specs))

if __name__ == '__main__':
    main()
