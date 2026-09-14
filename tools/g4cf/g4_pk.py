# -*- coding: utf-8 -*-
# Родные p_k Geant4: β-ветви из RadioactiveDecay + каскад из PhotonEvaporation.
# p(гамма) на распад = населённость уровня × доля перехода × 1/(1+α).
# Печатает p_k и CF_G4, пересобранные на них (кажущиеся — из ион-логов).
import io, math, re, sys
sys.stdout.reconfigure(encoding="utf-8")

import os
G4 = r"C:\Users\moroz\source\repos\GEANT4"
SP = os.path.dirname(os.path.abspath(__file__))   # ион-логи кладут рядом


def parse_photon(path):
    """Уровни и переходы: idx -> (E, [(final, Egamma, rel, alpha)])."""
    levels = {}
    current = None
    for line in io.open(path):
        parts = line.split()
        if not parts:
            continue
        # Строка уровня: idx, флаг +-, E, T1/2, спин, число переходов
        if len(parts) >= 2 and parts[1] in "+-" and not line.startswith("      "):
            idx = int(parts[0])
            levels[idx] = (float(parts[2]), [])
            current = idx
        elif current is not None and line.startswith("      "):
            final = int(parts[0])
            egamma = float(parts[1])
            rel = float(parts[2])
            alpha = float(parts[5]) if len(parts) > 5 else 0.0
            levels[current][1].append((final, egamma, rel, alpha))
    return levels


def parse_beta(path, mode="BetaMinus"):
    """Ветви распада: Ex дочернего уровня -> интенсивность, %."""
    feeds = {}
    in_parent = False
    for line in io.open(path):
        if line.startswith("P"):
            # только основное состояние родителя (Ex = 0)
            parts = line.split()
            in_parent = float(parts[1]) == 0.0
            continue
        parts = line.split()
        if in_parent and parts and parts[0] == mode and len(parts) >= 4:
            # НАКОПЛЕНИЕ, не присваивание: две ветви на один уровень
            # (разные записи с одним Ex) перезаписывали друг друга.
            ex = float(parts[1])
            feeds[ex] = feeds.get(ex, 0.0) + float(parts[3])   # Ex, '-', I, Q
    return feeds


def populations(levels, feeds):
    """Населённость уровней на 100 распадов; отдаёт и выходы гамма."""
    # сопоставить Ex ветви ближайшему уровню
    pop = {idx: 0.0 for idx in levels}
    for ex, intensity in feeds.items():
        best = min(levels, key=lambda i: abs(levels[i][0] - ex))
        if abs(levels[best][0] - ex) < 0.5:
            pop[best] += intensity
        else:
            # Молчаливый выброс ветви занижал p_k без следа — для Co-60 и
            # Cs-134 сверено измерением, для прочих нуклидов гарантии не было.
            sys.stderr.write("g4_pk: ветвь Ex=%.1f (I=%.3f%%) без уровня ближе "
                             "0.5 кэВ — выброшена\n" % (ex, intensity))
    gammas = {}
    for idx in sorted(levels, reverse=True):
        if pop[idx] <= 0.0 or idx == 0:
            continue
        total = sum(rel for _, _, rel, _ in levels[idx][1])
        if total <= 0.0:
            continue
        for final, egamma, rel, alpha in levels[idx][1]:
            share = pop[idx] * rel / total
            gammas[egamma] = gammas.get(egamma, 0.0) + share / (1.0 + alpha)
            pop[final] += share
    return gammas


def read_ion(path):
    out = {}
    for line in io.open(path, encoding="utf-8", errors="replace"):
        m = re.match(r"RESULT window=([\d.]+) counts=(\d+) eps=([\deE.+-]+)", line)
        if m:
            out[float(m.group(1))] = (int(m.group(2)), float(m.group(3)))
    return out


MONO = {604.7: 1.403560e-02, 795.9: 9.509450e-03, 1168.0: 5.657950e-03,
        1365.2: 4.599650e-03, 1173.2: 5.621100e-03, 1332.5: 4.752300e-03}
NUCDB = {604.7: 97.560, 795.9: 85.440, 1168.0: 1.800, 1365.2: 3.040,
         1173.2: 99.850, 1332.5: 99.983}

for tag, zphot, zbeta, log in (
        ("cs134", r"\PhotonEvaporation6.1.2\z56.a134",
         r"\RadioactiveDecay6.1.2\z55.a134", r"\g4_ion_cs134.log"),
        ("co60", r"\PhotonEvaporation6.1.2\z28.a60",
         r"\RadioactiveDecay6.1.2\z27.a60", r"\g4_ion_co60.log")):
    levels = parse_photon(G4 + zphot)
    feeds = parse_beta(G4 + zbeta)
    gammas = populations(levels, feeds)
    ion = read_ion(SP + log)
    print("=== %s (β-ветви: %s)" % (tag, {k: round(v, 3) for k, v in sorted(feeds.items())}))
    for win in sorted(ion):
        counts, eps_app = ion[win]
        near = [(e, p) for e, p in gammas.items() if abs(e - win) < 0.6]
        if not near or win not in MONO:
            continue
        p_g4 = sum(p for _, p in near) / 100.0
        p_db = NUCDB[win] / 100.0
        cf_db = p_db * MONO[win] / eps_app
        cf_g4 = p_g4 * MONO[win] / eps_app
        print("  %7.1f: p_G4 %.5f против nucdb %.5f (%+.2f %%)  CF: %.4f -> %.4f"
              % (win, p_g4, p_db, 100.0 * (p_g4 / p_db - 1.0), cf_db, cf_g4))
