# -*- coding: utf-8 -*-
# Сверка откликов: наша гистограмма (effsim --hist, OURHIST) против Geant4
# (g4cf hist, HIST). Оба — доли на бин поглощённой энергии на квант в 4π.
#   python cmp_hist.py our.log g4.log
import io, math, re, sys
sys.stdout.reconfigure(encoding="utf-8")


def read_ours(path):
    out = {}
    e = None
    for line in io.open(path, encoding="utf-8", errors="replace"):
        m = re.match(r"OURHISTBEGIN e_kev=([\d.]+) bins=(\d+) bin_kev=([\d.]+)", line)
        if m:
            e = float(m.group(1))
            out[e] = {"bins": int(m.group(2)), "bin": float(m.group(3)), "h": {}}
        m = re.match(r"OURHIST (\d+) ([\deE.+-]+)", line)
        if m and e is not None:
            out[e]["h"][int(m.group(1))] = float(m.group(2))
    return out


def read_g4(path):
    decays = bins = None
    bin_kev = None
    h = {}
    for line in io.open(path, encoding="utf-8", errors="replace"):
        m = re.match(r"HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)", line)
        if m:
            bins, bin_kev, decays = int(m.group(1)), float(m.group(2)), int(m.group(3))
        m = re.match(r"HIST (\d+) (\d+)", line)
        if m:
            h[int(m.group(1))] = int(m.group(2))
    return {"bins": bins, "bin": bin_kev, "decays": decays, "h": h}


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


ours_all = read_ours(sys.argv[1])
g4 = read_g4(sys.argv[2])
for e, ours in sorted(ours_all.items()):
    if abs(ours["bin"] - g4["bin"]) > 1e-9 or ours["bins"] != g4["bins"]:
        continue
    n = ours["bins"]
    bk = ours["bin"]
    gh = {k: v / g4["decays"] for k, v in g4["h"].items()}
    oh = ours["h"]
    print("=== E=%.1f кэВ, бин %.1f, бинов %d" % (e, bk, n))
    peak_o, peak_g = oh.get(n - 1, 0.0), gh.get(n - 1, 0.0)
    print("  пик:    наша %.4e  G4 %.4e  отн. %.3f"
          % (peak_o, peak_g, peak_o / peak_g if peak_g else float("nan")))
    tot_o, tot_g = sum(oh.values()), sum(gh.values())
    print("  полная: наша %.4e  G4 %.4e  отн. %.3f" % (tot_o, tot_g, tot_o / tot_g))
    for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 0.999)):
        a, b = band(oh, int(lo * n), int(hi * n)), band(gh, int(lo * n), int(hi * n))
        print("  [%3.0f..%3.0f%%E): наша %.4e  G4 %.4e  отн. %s"
              % (100 * lo, 100 * hi, a, b, "%.3f" % (a / b) if b else "-"))
    if e > 1022.0:
        for name, esc in (("вылет 511", 511.0), ("вылет 1022", 1022.0)):
            c = int((e - esc) / bk + 0.5)
            a = sum(oh.get(k, 0.0) for k in (c - 1, c, c + 1))
            b = sum(gh.get(k, 0.0) for k in (c - 1, c, c + 1))
            print("  %s (бин %d±1): наша %.4e  G4 %.4e  отн. %s"
                  % (name, c, a, b, "%.3f" % (a / b) if b else "-"))
