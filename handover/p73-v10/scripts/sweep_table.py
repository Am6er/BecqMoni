# -*- coding: utf-8 -*-
r"""Таблица развёртки зазора: ε_пик(662) при d=0 и d=50 мм по узлам кривых store_sweep\spectra; изм/ожид при каждом зазоре."""
import sys, io, os, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.path.insert(0, r'D:\BqMoni_Claude\p73')
from eff_read import curve, at
E = 661.657; I = 0.851
A0, A50 = 5564.3, 5235.6
M = {  # измеренные cps: (BG difference, Covell, FSA-эквивалент = decay_s*I*ε0? нет — FSA даёт Бк напрямую)
    0: dict(bgd=26.7373, cov=25.2036, fsa_bq=2466.0),
    50: dict(bgd=0.8425, cov=0.7790, fsa_bq=5495.1),
}
SP = r'D:\BqMoni_Claude\p73\store_sweep\spectra'
rows = {}
for f in sorted(os.listdir(SP)):
    m = re.match(r'RC103_g(\d+)_d(\d+)\.xml', f)
    if not m: continue
    g, d = int(m.group(1)), int(m.group(2))
    name, stamp, pts = curve(os.path.join(SP, f))
    v, s = at(pts, E)
    rows.setdefault(g, {})[d] = (v, s)
e0_ref = rows[0][0][0]; e50_ref = rows[0][50][0]
print('зазор, мм | ε(0 мм)      ±МК | ε(50 мм)     ±МК | ε/ε(g=0) 0 мм | ε/ε(g=0) 50 мм | изм/ожид 0 мм: BGdiff / Covell / FSA | изм/ожид 50 мм: BGdiff / Covell / FSA')
for g in sorted(rows):
    (e0, s0), (e50, s50) = rows[g][0], rows[g][50]
    r0 = [M[0]['bgd'] / (A0 * I * e0), M[0]['cov'] / (A0 * I * e0), M[0]['fsa_bq'] / A0 * e0_ref / e0]
    r50 = [M[50]['bgd'] / (A50 * I * e50), M[50]['cov'] / (A50 * I * e50), M[50]['fsa_bq'] / A50 * e50_ref / e50]
    print('%9d | %.4e %4.1f%% | %.4e %4.1f%% | %13.3f | %14.3f | %5.3f / %5.3f / %5.3f | %5.3f / %5.3f / %5.3f' % (
        g, e0, s0, e50, s50, e0 / e0_ref, e50 / e50_ref, *r0, *r50))
