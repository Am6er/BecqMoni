# -*- coding: utf-8 -*-
# Читатель веера LightScaleProbe П17: таблица «до/после/Ходюк», провал к тренду, контроль порта кривой.
import glob, io, math, os, sys
sys.stdout.reconfigure(encoding='utf-8')
OUT = "C:/Users/moroz/bqp17_out/"

def read_ls(path):
    r = {}
    for line in io.open(path, encoding='utf-8'):
        p = line.split()
        if len(p) >= 3:
            try:
                e = float(p[0]); v = float(p[1])
            except ValueError:
                continue
            r[round(e, 1)] = v
    return r

def read_curve(path):
    r = {}
    for line in io.open(path, encoding='utf-8'):
        if line.startswith('#') or line.startswith('E_kev'):
            continue
        p = line.split()
        if len(p) == 2:
            r[float(p[0])] = float(p[1])
    return r

# 1. контроль порта: кривая в коде (η=0.33, без продолжения) против таблицы базы
if os.path.exists(OUT + 'curve_k0_eta0.33.txt') and os.path.exists(OUT + 'curve_kdip0.txt'):
    a = read_curve(OUT + 'curve_kdip0.txt'); b = read_curve(OUT + 'curve_k0_eta0.33.txt')
    worst = max(abs(b[e] - a[e]) / a[e] for e in a if e >= 1.0)
    print("контроль порта кривой (η=0.33 в коде против таблицы базы, E ≥ 1 кэВ): max |отн. расх.| = %.2e" % worst)

KH = {10: 1.12, 20: 1.172, 34.5: 1.141, 50: 1.158, 100: 1.112}
runs = ['before', 'k3', 'k1_eq0', 'k2', 'k1_eq0.7', 'k1_eq1.0', 'k1_eq1.3', 'k1_eq1.6', 'k1_eq2.0']
data = {}
for r in runs:
    p = OUT + 'ls_%s.txt' % r
    if os.path.exists(p):
        data[r] = read_ls(p)
es = [10, 20, 30, 32, 33, 34, 35, 36, 40, 50, 60, 81, 100, 122, 200, 356, 661.7, 1000]
have = [r for r in runs if r in data and 661.7 in data[r]]
print("\nфотонная кривая, нормировка на ФОТОН 662 (r(E)/r(662)); Ходюк — из текста статьи")
print("%7s | " % "E" + " | ".join("%9s" % r for r in have) + " | Ходюк")
for e in es:
    row = []
    for r in have:
        d = data[r]
        row.append("%9.4f" % (d[e] / d[661.7]) if e in d else "%9s" % "-")
    kh = KH.get(e, KH.get(34.5) if e == 34 else None)
    print("%7.1f | " % e + " | ".join(row) + " | " + ("%.3f" % KH[e] if e in KH else ""))
print("\nпризнаки провала (сырые, к электрону 662):")
print("%9s | ступень 33→34 % | 34 к тренду(20↔50) % | 10/20 | 20/662 | 122/662 | 662 сырое" )
for r in have:
    d = data[r]
    if not all(k in d for k in (20, 33, 34, 50)):
        continue
    trend = d[20] + (d[50] - d[20]) * (14.0 / 30.0)
    print("%9s | %+8.2f | %+8.2f | %.4f | %.4f | %.4f | %.4f" % (
        r, (d[34] / d[33] - 1) * 100, (d[34] / trend - 1) * 100, d[10] / d[20] if 10 in d else float('nan'),
        d[20] / d[661.7], d[122] / d[661.7], d[661.7]))
# кривая электронов
print("\nкривая электронов y(E) (файлы --curve=):")
cs = {r: read_curve(OUT + 'curve_%s.txt' % r) for r in ['kdip0', 'k1_eq0', 'k1_eq1.0', 'k1_eq1.3', 'k1_eq2.0'] if os.path.exists(OUT + 'curve_%s.txt' % r)}
pts = [0.1, 0.3, 0.5, 1.0, 1.5, 2.0, 3.0, 5.0, 10.0, 20.0, 30.0, 60.0, 100.0]
def at(c, e):
    ks = sorted(c)
    if e <= ks[0]: return c[ks[0]]
    if e >= ks[-1]: return c[ks[-1]]
    lo = max(k for k in ks if k <= e); hi = min(k for k in ks if k > e)
    f = (math.log(e) - math.log(lo)) / (math.log(hi) - math.log(lo))
    return c[lo] + f * (c[hi] - c[lo])
print("%6s | " % "E" + " | ".join("%9s" % r for r in cs))
for e in pts:
    print("%6.1f | " % e + " | ".join("%9.4f" % at(cs[r], e) for r in cs))
