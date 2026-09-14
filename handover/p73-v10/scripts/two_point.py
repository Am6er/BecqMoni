# -*- coding: utf-8 -*-
r"""П73 (V10): разделение «зазор / масштаб» по двум точкам (0 и 50 мм) на развёртке зазора (store_sweep).

Модель: ε_ист(d) = S · ε_МК(d, g), где g — торцевой зазор (кристалл утоплен за наружной гранью корпуса модели),
S — общий множитель эффективности (размер кристалла / мёртвый слой / паспорт). Два измерения — два уравнения:
  r0(g)  = изм0  / (A0  · I · ε_МК(0, g))   = S
  r50(g) = изм50 / (A50 · I · ε_МК(50, g))  = S
Решение — пересечение r0(g) и r50(g) (лог-линейная интерполяция по сетке зазора). Отдельно — «только зазор» (S = 1):
g, при котором r0 = 1, и что тогда выходит на 50 мм. Ошибки — статистика счёта + МК узлов кривых (в квадратуре).
"""
import sys, io, os, re, math
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.path.insert(0, r'D:\BqMoni_Claude\p73')
from eff_read import curve, at

E, I = 661.657, 0.851
A0, A50 = 5564.3, 5235.6
# измерения: (значение, ошибка) — cps для зон, Бк для FSA (FSA-Бк пересчитываются в cps через ε_МК(g=0): изм_cps = Бк·I·ε0)
MEAS = {
    'BG difference': dict(m0=(26.7373, 0.1752), m50=(0.8425, 0.0158)),
    'Covell':        dict(m0=(25.2036, 0.1890), m50=(0.7790, 0.0181)),
    'FSA':           dict(bq0=(2466.0, 2466.0 / 114.17), bq50=(5495.1, 5495.1 / 61.34)),
}
SP = r'D:\BqMoni_Claude\p73\store_sweep\spectra'
eps = {}
for f in os.listdir(SP):
    m = re.match(r'RC103_g(\d+)_d(\d+)\.xml', f)
    if not m:
        continue
    name, stamp, pts = curve(os.path.join(SP, f))
    eps[(int(m.group(1)), int(m.group(2)))] = at(pts, E)
gaps = sorted({g for g, d in eps})
e0g0, e50g0 = eps[(0, 0)][0], eps[(0, 50)][0]


def ratios(method):
    d = MEAS[method]
    if 'm0' in d:
        m0, s0 = d['m0']; m50, s50 = d['m50']
    else:
        # FSA: активность в Бк при ε(g=0) -> эквивалентный cps пика
        m0, s0 = d['bq0'][0] * I * e0g0, d['bq0'][1] * I * e0g0
        m50, s50 = d['bq50'][0] * I * e50g0, d['bq50'][1] * I * e50g0
    r0, r50 = {}, {}
    for g in gaps:
        (v0, mc0), (v50, mc50) = eps[(g, 0)], eps[(g, 50)]
        r0[g] = (m0 / (A0 * I * v0), math.hypot(s0 / m0, mc0 / 100.0))
        r50[g] = (m50 / (A50 * I * v50), math.hypot(s50 / m50, mc50 / 100.0))
    return r0, r50


def cross(r0, r50):
    """g, где ln r0(g) - ln r50(g) меняет знак; линейная интерполяция по g."""
    f = {g: math.log(r0[g][0]) - math.log(r50[g][0]) for g in gaps}
    for a, b in zip(gaps, gaps[1:]):
        if f[a] <= 0 <= f[b] or f[b] <= 0 <= f[a]:
            t = f[a] / (f[a] - f[b])
            g = a + t * (b - a)
            S = math.exp(math.log(r0[a][0]) + t * (math.log(r0[b][0]) - math.log(r0[a][0])))
            return g, S, (a, b)
    return None, None, None


def solve_one(r):
    """g, где r(g) = 1 (S = 1)."""
    f = {g: math.log(r[g][0]) for g in gaps}
    for a, b in zip(gaps, gaps[1:]):
        if f[a] <= 0 <= f[b] or f[b] <= 0 <= f[a]:
            t = f[a] / (f[a] - f[b])
            return a + t * (b - a)
    return None


print('ε_МК(662): g=0: 0 мм %.4e, 50 мм %.4e; A0 = %.1f Бк (23.01.2024), A50 = %.1f Бк (14.09.2026), I = %.3f' % (e0g0, e50g0, A0, A50, I))
for method in MEAS:
    r0, r50 = ratios(method)
    print('\n== %s' % method)
    print('   изм/ожид при g=0: контакт %.3f ± %.3f, 50 мм %.3f ± %.3f' % (r0[0][0], r0[0][0] * r0[0][1], r50[0][0], r50[0][0] * r50[0][1]))
    g1 = solve_one(r0)
    if g1 is not None:
        # r50 при g1 — интерполяция
        a = max(g for g in gaps if g <= g1); b = min(g for g in gaps if g >= g1)
        t = 0.0 if b == a else (g1 - a) / (b - a)
        r50_at = math.exp(math.log(r50[a][0]) + t * (math.log(r50[b][0]) - math.log(r50[a][0])))
        print('   только зазор (S=1): контакт требует g = %.2f мм; при нём 50 мм даёт изм/ожид %.3f (натяжка %+.1f %%)' % (g1, r50_at, 100 * (r50_at - 1)))
    g2, S, seg = cross(r0, r50)
    if g2 is not None:
        # ошибка g2 из ошибок r0, r50: d(ln r0 - ln r50)/dg на отрезке
        a, b = seg
        slope = ((math.log(r0[b][0]) - math.log(r50[b][0])) - (math.log(r0[a][0]) - math.log(r50[a][0]))) / (b - a)
        dg = math.hypot(r0[a][1], r50[a][1]) / abs(slope)
        print('   зазор + масштаб: g = %.2f ± %.2f мм, S = %.3f (то есть модель занижает ε в %.2f раза при таком зазоре)' % (g2, dg, S, S))
    print('   развёртка: ' + ', '.join('g=%d: %.3f/%.3f' % (g, r0[g][0], r50[g][0]) for g in gaps if g <= 8))
