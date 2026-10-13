# П111 (M13): сводка стенда пластин — наша LayerReturnProbe --brem (lr/<name>.txt) против опоры Geant4 g4brem
# (g4brem/out/<name>_<E>_a<ang>.log). Числа на пущенный электрон: квантов k ≥ 5 кэВ, излучённая энергия, вперёд/назад
# (полусфера относительно нормали слоя: вперёд = глубже), путь в веществе г/см², квантов на г/см², η; гистограмма по k.
# Разделитель дробной части — точка.
import glob, os, re, sys

root = r"D:\BqMoni_Claude\p111"
lr_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, "lr")
g4_dir = os.path.join(root, "g4brem", "out")
out_path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, "tables", "slab_cmp.txt")
edges = [5, 10, 20, 30, 50, 70, 100, 150, 200, 300, 500, 700, 1000, 1500, 2000]
labels = ["%g-%g" % (edges[i], edges[i + 1]) for i in range(len(edges) - 1)] + ["2000+"]

def kv(line):
    d = {}
    for m in re.finditer(r"(\w+)=(\S+)", line):
        d[m.group(1)] = m.group(2)
    return d

ours = {}   # (name, E, ang) -> dict
for path in glob.glob(os.path.join(lr_dir, "*.txt")):
    name = os.path.splitext(os.path.basename(path))[0]
    lines = open(path, encoding="utf-8").read().splitlines()
    for i, ln in enumerate(lines):
        s = ln.strip()
        if s.startswith("BREM "):
            d = kv(s)
            e = float(d["T"]); ang = float(d["angle"])
            rec = {k: float(v) for k, v in d.items() if k not in ("captured",)}
            rec["captured"] = d.get("captured", "")
            # гистограмма — следующая строка HIST all
            h = lines[i + 1].strip()
            if h.startswith("HIST all"):
                rec["hist"] = [float(x) for x in h.split()[2:]]
            ours[(name, e, ang)] = rec

g4 = {}
for path in glob.glob(os.path.join(g4_dir, "*.log")):
    base = os.path.splitext(os.path.basename(path))[0]
    m = re.match(r"(.+)_(\d+)_a(\d+)$", base)
    if not m:
        continue
    name, e, ang = m.group(1), float(m.group(2)), float(m.group(3))
    rec = {}
    for ln in open(path, encoding="utf-8", errors="replace"):
        s = ln.strip()
        if s.startswith("BREM "):
            d = kv(s)
            for k, v in d.items():
                try:
                    rec[k] = float(v.replace("+-", ""))
                except ValueError:
                    pass
            # +- после brem_all
            mm = re.search(r"brem_all=(\S+) \+-(\S+)", s)
            if mm:
                rec["brem_all"] = float(mm.group(1)); rec["brem_all_err"] = float(mm.group(2))
        elif s.startswith("HIST all"):
            rec["hist"] = [float(x) for x in s.split()[2:]]
        elif s.startswith("HIST prim"):
            rec["hist_prim"] = [float(x) for x in s.split()[2:]]
        elif s.startswith("HIST esc"):
            rec["hist_esc"] = [float(x) for x in s.split()[2:]]
    if rec:
        g4[(name, e, ang)] = rec

def r(a, b):
    return a / b if b else float("nan")

os.makedirs(os.path.dirname(out_path), exist_ok=True)
w = open(out_path, "w", encoding="utf-8")
def P(s=""):
    print(s); w.write(s + "\n")

P("СТЕНД ПЛАСТИН П111: тормозное электрона в слое по ходу переноса — наша сторона (LayerReturnProbe --brem, класс I, без δ) против Geant4 option4 (g4brem; e- cut 1 мм, γ cut 0.001 мм; brem_all — от всех лептонов, brem_prim — от первичного).")
P("Числа на пущенный электрон, квантов с k ≥ 5 кэВ; вперёд/назад — полусфера относительно нормали слоя (вперёд = глубже в слой); путь — г/см² в веществе; на г/см² — квантов на единицу пути.")
P()
hdr = "%-8s %6s %4s | %9s %9s %6s | %8s %8s %6s | %7s %7s | %7s %7s %6s | %7s %7s %6s | %7s %7s"
P(hdr % ("связка", "T,кэВ", "θ°", "наша N", "G4 N", "отн", "наша E", "G4 E", "отн", "наз.наша", "наз.G4", "путь н", "путь G4", "отн", "на г н", "на г G4", "отн", "η наша", "η G4"))
ORDER = ["ptfe20", "al20", "ptfe1", "ptfe03", "al1", "rc103", "foil_ptfe0.01", "foil_ptfe0.03"]
keys = sorted(set(ours) | set(g4), key=lambda k: (ORDER.index(k[0]) if k[0] in ORDER else 99, k[2], k[1]))
for k in keys:
    o = ours.get(k); g = g4.get(k)
    if not o or not g:
        P("%-8s %6g %4g | %s" % (k[0], k[1], k[2], "нет " + ("нашей" if not o else "G4") + " стороны"))
        continue
    P(hdr % (k[0], k[1], k[2],
             "%.5f" % o["brem_all"], "%.5f" % g["brem_all"], "%.3f" % r(o["brem_all"], g["brem_all"]),
             "%.3f" % o["brem_E"], "%.3f" % g["brem_E"], "%.3f" % r(o["brem_E"], g["brem_E"]),
             "%.3f" % r(o["back"], o["brem_all"]), "%.3f" % r(g["back"], g["brem_all"]),
             "%.4f" % o["path"], "%.4f" % g["path_prim"], "%.3f" % r(o["path"], g["path_prim"]),
             "%.4f" % o["per_g"], "%.4f" % r(g["brem_all"], g["path_all"]), "%.3f" % r(o["per_g"], r(g["brem_all"], g["path_all"])),
             "%.4f" % o["eta"], "%.4f" % g["eta"]))
P()
P("ГИСТОГРАММА ПО ЭНЕРГИИ КВАНТА (наша/G4 по полосам; в скобках — доля полосы у G4, %):")
P("%-8s %6s %4s | " % ("связка", "T,кэВ", "θ°") + " ".join("%9s" % l for l in labels))
for k in keys:
    o = ours.get(k); g = g4.get(k)
    if not o or not g or "hist" not in o or "hist" not in g:
        continue
    tot = sum(g["hist"]) or 1.0
    nev = g.get("N", 100000.0)
    cells = []
    for i in range(len(labels)):
        gv = g["hist"][i]; ov = o["hist"][i]
        if gv * nev < 30:      # мало событий у опоры — не читать
            cells.append("%9s" % "-")
        else:
            cells.append("%5.2f(%2.0f)" % (ov / gv, 100.0 * gv / tot))
    P("%-8s %6g %4g | " % (k[0], k[1], k[2]) + " ".join(cells))
P()
P("G4: доля вышедших из вещества квантов (esc/all) и назад из вышедших:")
for k in keys:
    g = g4.get(k)
    if not g:
        continue
    esc = g["esc_fwd"] + g["esc_back"]
    P("%-8s %6g %4g | esc/all=%.3f esc_back/esc=%.3f esc_E/brem_E=%.3f brem_prim/brem_all=%.3f path_all/path_prim=%.3f" %
      (k[0], k[1], k[2], r(esc, g["brem_all"]), r(g["esc_back"], esc), r(g["esc_E"], g["brem_E"]), r(g["brem_prim"], g["brem_all"]), r(g["path_all"], g["path_prim"])))
w.close()
print("->", out_path)
