# -*- coding: utf-8 -*-
r"""П85: A22/A44 каскада по F-коэффициентам (Ферентц–Розенцвейг) с обоими знаками δ первого перехода —
чтобы понять расхождение с Geant4 на смешанных переходах.
    python fcoef.py
"""
from math import factorial, sqrt

def tri(a, b, c):
    return factorial(a + b - c) * factorial(a - b + c) * factorial(-a + b + c) / factorial(a + b + c + 1)

def w3j(j1, j2, j3, m1, m2, m3):
    # целые j, m (для нашего случая достаточно)
    if m1 + m2 + m3 != 0 or j3 > j1 + j2 or j3 < abs(j1 - j2): return 0.0
    if abs(m1) > j1 or abs(m2) > j2 or abs(m3) > j3: return 0.0
    pref = sqrt(tri(j1, j2, j3) * factorial(j1 + m1) * factorial(j1 - m1) * factorial(j2 + m2) * factorial(j2 - m2)
                * factorial(j3 + m3) * factorial(j3 - m3))
    s = 0.0
    for t in range(0, 50):
        a = j3 - j2 + t + m1; b = j3 - j1 + t - m2; c = j1 + j2 - j3 - t; d = j1 - t - m1; e = j2 - t + m2
        if min(a, b, c, d, e) < 0: continue
        s += (-1) ** t / (factorial(t) * factorial(a) * factorial(b) * factorial(c) * factorial(d) * factorial(e))
    return (-1) ** (j1 - j2 - m3) * pref * s

def w6j(j1, j2, j3, j4, j5, j6):
    def ok(a, b, c): return abs(a - b) <= c <= a + b
    if not (ok(j1, j2, j3) and ok(j1, j5, j6) and ok(j4, j2, j6) and ok(j4, j5, j3)): return 0.0
    pref = sqrt(tri(j1, j2, j3) * tri(j1, j5, j6) * tri(j4, j2, j6) * tri(j4, j5, j3))
    s = 0.0
    for t in range(0, 60):
        a = t - j1 - j2 - j3; b = t - j1 - j5 - j6; c = t - j4 - j2 - j6; d = t - j4 - j5 - j3
        e = j1 + j2 + j4 + j5 - t; f = j2 + j3 + j5 + j6 - t; g = j3 + j1 + j6 + j4 - t
        if min(a, b, c, d, e, f, g) < 0: continue
        s += (-1) ** t * factorial(t + 1) / (factorial(a) * factorial(b) * factorial(c) * factorial(d) * factorial(e) * factorial(f) * factorial(g))
    return pref * s

def F(k, L, Lp, jOther, jMid):
    three = w3j(L, Lp, k, 1, -1, 0)
    six = w6j(L, Lp, k, jMid, jMid, jOther)
    sign = -1.0 if (jOther + jMid - 1) % 2 else 1.0
    return sign * sqrt((2 * L + 1) * (2 * Lp + 1) * (2 * jMid + 1) * (2 * k + 1)) * three * six

def Ak(k, L, Lp, delta, jOther, jMid):
    if L == Lp: return F(k, L, L, jOther, jMid)
    return (F(k, L, L, jOther, jMid) + 2 * delta * F(k, L, Lp, jOther, jMid) + delta ** 2 * F(k, Lp, Lp, jOther, jMid)) / (1 + delta ** 2)

def cascade(j1, jm, j2, L1, L1p, d1, L2, L2p, d2):
    return tuple(Ak(k, L1, L1p, d1, j1, jm) * Ak(k, L2, L2p, d2, j2, jm) for k in (2, 4))

CASES = [
    ('Co-60 1173(E2+M3 δ=-0.0025)+1332(E2)', 4, 2, 0, 2, 3, -0.0025, 2, 2, 0.0, (0.0995, 0.0104)),
    ('Cs-134 569(M1+E2 δ=0.26)+796(E2)', 4, 4, 2, 1, 2, 0.26, 2, 2, 0.0, (0.1029, 0.0083)),
    ('Cs-134 563(M1+E2 δ=-7.4)+605(E2)', 2, 2, 0, 1, 2, -7.4, 2, 2, 0.0, (0.0255, 0.3230)),
    ('Eu-152 1408(E1+M2 δ=0.043)+122(E2)', 2, 2, 0, 1, 2, 0.043, 2, 2, 0.0, (0.2165, 0.0042)),
    ('Eu-152 1112(M1+E2 δ=-8.7)+122(E2)', 3, 2, 0, 1, 2, -8.7, 2, 2, 0.0, (-0.2913, -0.0811)),
    ('Eu-152 964(E2+M1 «403» δ=-9.3)+122(E2)', 2, 2, 0, 2, 1, -9.3, 2, 2, 0.0, (0.3261, 0.0015)),
]
print('%-40s %18s %18s %18s' % ('каскад', 'наш знак δ1 (A22,A44)', 'обратный знак δ1', 'Geant4 angcorr'))
for name, j1, jm, j2, L1, L1p, d1, L2, L2p, d2, g4 in CASES:
    a = cascade(j1, jm, j2, L1, L1p, d1, L2, L2p, d2)
    b = cascade(j1, jm, j2, L1, L1p, -d1, L2, L2p, d2)
    print('%-40s %8.4f %8.4f   %8.4f %8.4f   %8.4f %8.4f' % (name, a[0], a[1], b[0], b[1], g4[0], g4[1]))
# контроль: учебные
print('контроль 4(2)2(2)0:', cascade(4, 2, 0, 2, 2, 0, 2, 2, 0), ' 3(1)2(2)0:', cascade(3, 2, 0, 1, 1, 0, 2, 2, 0), ' 0(2)2(2)0:', cascade(0, 2, 0, 2, 2, 0, 2, 2, 0))
