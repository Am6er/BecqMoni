# -*- coding: utf-8 -*-
"""П166, AMBER124/125: арбитр Geant4 — континуум каскадной пары Co-60 на контакте G1S.
Истина — ион-режим (4e6 распадов, изотропно, без corr). Модели собраны из МОНО-гистограмм
тех же квантов (2e6 каждый) по правилам приложения:
  «как сейчас»  — пик и вылеты × (1 − ε_T партнёра), комптон × 1, сумм-пик P⊗P;
  «свёртка»     — точная свёртка D1⊗D2 (D = δ0·(1−ε_T) + h): то, к чему ведёт правка
                  (все каналы × (1 − ε_T партнёра) + P⊗непик + непик⊗P + непик⊗непик + P⊗P).
"""
import sys
import numpy as np
sys.path.insert(0, r'D:\BqMoni_Claude\p166\wt\handover\p148')
from g4hist import load

import os
NUC=os.environ.get('NUC','co60')
CFG={'co60':('ion_co60','mono_1173','mono_1332',0.9988,0.0012,'Co-60'),'y88':('ion_y88','mono_898','mono_1836',0.937,0.055,'Y-88')}[NUC]
ion, st, nion = load(r'D:\BqMoni_Claude\p166\g4\%s.log' % CFG[0])
m1, s1, n1 = load(r'D:\BqMoni_Claude\p166\g4\%s.log' % CFG[1])
m2, s2, n2 = load(r'D:\BqMoni_Claude\p166\g4\%s.log' % CFG[2])
assert st == s1 == s2
L = len(ion)
def per(h, n):
    out = np.zeros(L); out[:len(h)] = h / n; out[0] = 0.0   # бин 0 — «ничего» (edep < шаг/2)
    return out
h1, h2 = per(m1, n1), per(m2, n2)
t = ion / nion; t[0] = 0.0
# Co-60 (DDEP): β⁻ на уровень 2505.7 — 99.88 %, на 1332.5 — 0.12 %
p_pair, p_single = CFG[3], CFG[4]   # вторая линия пары бывает и одна (питание её уровня прямо)
def split(h):
    """пик = последний непустой бин моно; вылеты = узкие выбросы над медианой ±60 бинов (6σ); прочее — комптон."""
    k_peak = np.nonzero(h)[0].max()
    peak = np.zeros(L); peak[k_peak] = h[k_peak]
    esc = np.zeros(L)
    for k in range(20, k_peak - 2):
        seg = h[max(1, k - 60):k + 60]
        med = np.median(seg); sd = np.sqrt(med / 1.0) if med > 0 else 0
        # в долях на квант: порог по числу отсчётов
        cnt, cmed = h[k] * 2e6, med * 2e6
        if cnt > cmed + 6 * np.sqrt(max(cmed, 1.0)) and cnt > 1.5 * cmed:
            esc[k] = h[k] - med
    comp = h - peak - esc
    return peak, esc, comp
P1, E1, C1 = split(h1); P2, E2, C2 = split(h2)
eT1, eT2 = h1.sum(), h2.sum()
ep1, ep2 = P1.sum(), P2.sum()
def conv(a, b):
    c = np.convolve(a, b)[:L]
    return c
k1 = int(np.nonzero(P1)[0][0]); k2 = int(np.nonzero(P2)[0][0])
# «как сейчас»
cur = p_pair * ((P1 + E1) * (1 - eT2) + C1 + (P2 + E2) * (1 - eT1) + C2 + conv(P1, P2)) + p_single * h2
# точная свёртка
D1 = h1.copy(); D1[0] = 1 - eT1
D2 = h2.copy(); D2[0] = 1 - eT2
full = conv(D1, D2); full[0] = 0.0
ex = p_pair * full + p_single * h2
def band(a, lo, hi):
    return a[int(lo / st + 0.5):int(hi / st + 0.5)].sum()
print("Geant4 11.4.2, G1S_contact (NaI Ø63×63, точка в контакте), %s: ион %d распадов, моно по %d квантов" % (CFG[5], nion, n1))
print("ε_T(1-я) %.5f  ε_p %.5f  вылеты %.5f  комптон %.5f" % (eT1, ep1, E1.sum(), C1.sum()))
print("ε_T(2-я) %.5f  ε_p %.5f  вылеты %.5f  комптон %.5f" % (eT2, ep2, E2.sum(), C2.sum()))
print("C1·C2 = %.5f (двойной счёт «оба частично»), (a1 a2 − p1 p2) = %.5f" % (C1.sum() * C2.sum(), (ep1 + E1.sum()) * (ep2 + E2.sum()) - ep1 * ep2))
print()
print("%-26s %11s %11s %11s %9s %9s" % ("полоса, кэВ", "ион (ист.)", "как сейчас", "свёртка", "сейч/ист", "свёрт/ист"))
BANDS = {'co60': ((30, 1100, "континуум 30–1100"), (1100, 1400, "пики 1100–1400"), (1400, 2450, "сумм-континуум 1400–2450"),
                     (2450, 2560, "сумм-пик 2450–2560"), (30, 2700, "всё 30–2700")),
         'y88': ((30, 850, "континуум 30–850"), (850, 950, "пик 898"), (950, 1780, "континуум 950–1780"), (1780, 1880, "пик 1836"),
                 (2150, 2270, "SE 1836 + 898 (2223)"), (1880, 2680, "сумм-континуум 1880–2680"), (2680, 2790, "сумм-пик 2734"), (30, 3000, "всё 30–3000"))}[NUC]
for lo, hi, name in BANDS:
    a, b, c = band(t, lo, hi), band(cur, lo, hi), band(ex, lo, hi)
    na = np.sqrt(band(ion, lo, hi)) / nion
    print("%-26s %11.6f %11.6f %11.6f %9.4f %9.4f   (σ ист. %.2f %%)" % (name, a, b, c, b / a, c / a, 100 * na / a))
