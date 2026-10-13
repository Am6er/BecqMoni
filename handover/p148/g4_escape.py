# -*- coding: utf-8 -*-
"""П148, AMBER99: арбитр Geant4 — теряют ли пики вылета и 511 «вне кристалла»
ту же долю при суммировании каскада, что и пик полного поглощения.

r_X = (площадь линии X на распад Tl-208, ион-режим с каскадом)
      / (площадь той же линии на квант, моно-режим, без каскада).
r_X = Y·(1 − L_out_X) (+ влёт). Выход Y сокращается в r_X / r_FEP того же кванта:
1.000 — линия X теряет ровно столько, сколько пик; 1/(1 − L_out) — не теряет вовсе.
"""
import sys
import numpy as np
from g4hist import load

ion, step, n_ion = load('g4_ion_tl208.log')
m26, s26, n26 = load('g4_mono_2614.log')
m58, s58, n58 = load('g4_mono_583.log')
assert step == s26 == s58


def area(h, e, half=2, side=(0.6, 3.0)):
    """Площадь дельта-линии на энергии e: бины e±half·шаг минус подложка по боковым полосам."""
    k = int(e / step + 0.5)
    lo, hi = k - half, k + half
    win = h[lo:hi + 1].sum()
    a0, a1 = int(side[0] / step), int(side[1] / step)
    left = h[max(0, k - a1):k - a0]
    right = h[k + a0:min(len(h) - 1, k + a1)]  # последний бин моно — сам пик, в подложку не брать
    sb = np.concatenate([left, right]) if len(right) else left
    bg = sb.mean() * (hi - lo + 1)
    var = win + (hi - lo + 1) ** 2 * sb.var() / max(1, len(sb))
    return win - bg, np.sqrt(max(var, 1.0))


def ratio(e_ion, mono, n_mono, e_mono=None, half=2):
    a_i, s_i = area(ion, e_ion, half)
    a_m, s_m = area(mono, e_mono if e_mono is not None else e_ion, half)
    r = (a_i / n_ion) / (a_m / n_mono)
    sr = r * np.sqrt((s_i / a_i) ** 2 + (s_m / a_m) ** 2)
    return r, sr, a_i, a_m


def kesc_bins(mono, e_lo, e_hi):
    """Бины K-вылета: в моно-спектре — выше подложки на 6σ."""
    k0, k1 = int(e_lo / step + 0.5), int(e_hi / step + 0.5)
    seg = mono[k0:k1 + 1]
    med = np.median(seg)
    return [k0 + j for j, v in enumerate(seg) if v > med + 6 * np.sqrt(max(med, 1.0))], med


def ratio_bins(bins, mono, n_mono):
    def one(h, bins):
        k0, k1 = min(bins) - 60, max(bins) + 60
        others = [h[j] for j in range(k0, k1 + 1) if all(abs(j - b) > 3 for b in bins)]
        bg = np.median(others) * len(bins)
        a = sum(h[b] for b in bins) - bg
        return a, np.sqrt(sum(h[b] for b in bins) + len(bins) ** 2 * np.var(others) / len(others))
    a_i, s_i = one(ion, bins)
    a_m, s_m = one(mono, bins)
    r = (a_i / n_ion) / (a_m / n_mono)
    return r, r * np.sqrt((s_i / a_i) ** 2 + (s_m / a_m) ** 2), a_i, a_m


print("Geant4 11.4.2, сцена G1S_contact (NaI Ø63×63, точка в контакте), Tl-208: ион %d распадов; моно 2614.5 и 583.187 по %d квантов" % (n_ion, n26))
print("%-44s %10s %10s %9s %9s %12s" % ("линия", "ион", "моно", "r", "σ(r)", "r / r_пика"))
rows = []
r_fep, s_fep, ai, am = ratio(2614.5, m26, n26)
rows.append(("2614.5 полное поглощение (канал Peak)", ai, am, r_fep, s_fep, 1.0))
for label, e in (("2103.5 одиночный вылет (EscapeAnnihilation)", 2103.5),
                 ("1592.5 двойной вылет (EscapeAnnihilationDouble)", 1592.5)):
    r, s, ai, am = ratio(e, m26, n26)
    rows.append((label, ai, am, r, s, r / r_fep))
# 511 вне кристалла: окно ±1 бин вокруг 511.00, чтобы не задеть линию Tl-208 510.77
r, s, ai, am = ratio(511.0, m26, n26, half=1)
rows.append(("511.0 аннигиляция вне кристалла (AnnihilationOutside)", ai, am, r, s, r / r_fep))
r58, s58r, ai, am = ratio(583.2, m58, n58)
rows.append(("583.19 полное поглощение (канал Peak)", ai, am, r58, s58r, 1.0))
bins, med = kesc_bins(m58, 545.0, 557.0)
r, s, ai, am = ratio_bins(bins, m58, n58)
rows.append(("583 K-вылет (%d бинов %.2f…%.2f, EscapeXrayK)" % (len(bins), min(bins) * step, max(bins) * step), ai, am, r, s, r / r58))
for label, ai, am, r, s, rr in rows:
    print("%-44s %10.0f %10.0f %9.5f %9.5f %12.4f" % (label, ai, am, r, s, rr))
print()
print("1 − L_out по пику 2614.5 (без выхода): r_пика / Y; Y(2614.5) Geant4 ≈ 0.9975 → %.4f; CF = %.4f" % (r_fep / 0.9975, 0.9975 / r_fep))
print("1 − L_out по пику 583.19: Y(583) Geant4 ≈ 0.8450 → %.4f" % (r58 / 0.845))
