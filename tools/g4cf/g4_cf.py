# Сборка CF из логов g4cf: CF(k) = p_k * eps_пик(mono, k) / eps_кажущаяся(ion, k).
# Интенсивности — те же nucdb-значения, что печатает CoincCfProbe (v_gamma_coincidence_line),
# чтобы наша формула и G4-арбитр делили одни и те же p_k.
import math, re, sys, os

SP = os.path.dirname(os.path.abspath(__file__))

# p_k, доля на распад (nucdb): Co-60 и Cs-134
P = {
    ("co60", 1173.2): 0.9985, ("co60", 1332.5): 0.999826,
    ("cs134", 475.4): 0.01460, ("cs134", 563.2): 0.08380, ("cs134", 569.3): 0.15430,
    ("cs134", 604.7): 0.97560, ("cs134", 795.9): 0.85440, ("cs134", 801.9): 0.08730,
    ("cs134", 1038.6): 0.00990, ("cs134", 1168.0): 0.01800, ("cs134", 1365.2): 0.03040,
}

def read_windows(path):
    out = {}
    decays = None
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r"RESULT decays=(\d+)", line)
        if m:
            decays = int(m.group(1))
        m = re.match(r"RESULT window=([\d.]+) counts=(\d+) eps=([\deE.+-]+)", line)
        if m:
            out[float(m.group(1))] = (int(m.group(2)), float(m.group(3)))
    return decays, out

def mono_peak(e_tag):
    path = os.path.join(SP, "g4_mono_%s.log" % e_tag)
    if not os.path.exists(path):
        return None
    decays, wins = read_windows(path)
    if not wins:
        return None
    (counts, eps), = wins.values()
    return counts, eps

MONO_TAG = {604.7: "604", 795.9: "795", 1173.2: "1173", 1332.5: "1332",
            1365.2: "1365", 1168.0: "1168"}

for nuc, log in (("co60", "g4_ion_co60.log"), ("cs134", "g4_ion_cs134.log")):
    path = os.path.join(SP, log)
    if not os.path.exists(path):
        continue
    decays, wins = read_windows(path)
    if not wins:
        print("%s: ион-лог ещё пуст" % nuc)
        continue
    print("=== %s, распадов %d" % (nuc, decays))
    for e in sorted(wins):
        counts, eps_app = wins[e]
        err_app = 100.0 / math.sqrt(counts) if counts else float("nan")
        line = "  окно %7.1f: кажущаяся %.4e +-%.2f%%" % (e, eps_app, err_app)
        tag = MONO_TAG.get(e)
        pk = P.get((nuc, round(e, 1)))
        if tag and pk:
            mono = mono_peak(tag)
            if mono:
                mc, meps = mono
                err_mono = 100.0 / math.sqrt(mc) if mc else float("nan")
                cf = pk * meps / eps_app if eps_app else float("nan")
                err_cf = math.hypot(err_app, err_mono)
                line += "  eps_пик %.4e +-%.2f%%  CF = %.4f +- %.4f" % (
                    meps, err_mono, cf, cf * err_cf / 100.0)
        print(line)
