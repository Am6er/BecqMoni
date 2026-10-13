# -*- coding: utf-8 -*-
"""Сверка опорных линий nucdb с DDEP (LNHB, файлы Nucleide-Lara) — AMBER132, П169.

Читает ddep/<нуклид>.lara.txt (скачаны с http://www.lnhb.fr/nuclides/<нуклид>.lara.txt
28.09.2026), базу — только чтением. Опорная линия: γ или K-рентген с I(DDEP) >= 1 %.
Пишет ddep_compare.tsv и печатает таблицу.
"""
import os, re, sys, math, csv
HERE = os.path.dirname(os.path.abspath(__file__))
# chains.py дерева: из handover/p169/ — ../../tools/CORPUS/scripts, иначе worktree П169
_SCRIPTS = os.path.normpath(os.path.join(HERE, "..", "..", "tools", "CORPUS", "scripts"))
if not os.path.isdir(_SCRIPTS):
    _SCRIPTS = r"D:\BqMoni_Claude\p147\wt\tools\CORPUS\scripts"
sys.path.insert(0, _SCRIPTS)
import chains  # noqa: E402  (LEVEL_CLAUSE — правило уровня родителя приложения)
NUCS = ["K-40", "Cs-137", "Am-241", "Eu-152", "Na-22", "Y-88", "Ti-44", "Sc-44", "Ba-133",
        "Cd-109", "Co-60", "Ce-139", "Mn-54", "I-131", "Co-57", "Bi-207", "Zn-65",
        "Ra-226", "Rn-222", "Pb-214", "Bi-214", "Pb-210",
        "Ra-228", "Ac-228", "Th-228", "Ra-224", "Rn-220", "Pb-212", "Bi-212", "Tl-208", "Th-232",
        "U-238", "Th-234", "Pa-234m", "Pa-234", "U-234",
        "U-235", "Th-231", "Pa-231", "Ac-227", "Ra-223", "Rn-219", "Pb-211", "Bi-211", "Tl-207",
        "Po-214", "Po-218", "Po-212", "Po-210"]
XMAP = {"XKa1": ["KA1"], "XKa2": ["KA2"], "XK'b1": ["KpB1"], "XK'b2": ["KpB2"]}
MIN_I = 1.0


def nucid(name):
    el, rest = name.split("-")
    m = re.match(r"(\d+)(m?)$", rest)
    return m.group(1) + el.upper() + ("m1" if m.group(2) else "")


def ensdf_unc(value, unc):
    """'10.66','13' -> 0.13; 'LT'/'?'/'' -> None."""
    if not unc or not re.match(r"^\d+$", unc.strip()):
        return None
    v = value.strip().upper()
    mant, _, exp = v.partition("E")
    dec = len(mant.split(".")[1]) if "." in mant else 0
    u = int(unc) * 10.0 ** (-dec)
    if exp:
        u *= 10.0 ** int(exp)
    return u


def lara(name):
    path = os.path.join(HERE, "ddep", name + ".lara.txt")
    text = open(path, encoding="latin-1").read()
    if "Emissions" not in text:
        return None, None
    ref = re.search(r"^Reference ; (.*)$", text, re.M)
    # Файлы DDEP новых редакций (Pb-212, NIST 2025) несут излучения ВСЕГО ряда;
    # в сверку идут только линии дочерей самого распада (и их K/L-рентген)
    dau = re.search(r"^Daughter\(s\) ; (.*)$", text, re.M).group(1).split(" ; ")
    allowed = set()
    for k in range(1, len(dau), 3):
        allowed.add(dau[k].strip())
        allowed.add(dau[k].split("-")[0].strip())
    out = []
    started = False
    for line in text.splitlines():
        if line.startswith("Energy (keV)"):
            started = True
            continue
        if not started or line.startswith("==="):
            continue
        f = [x.strip() for x in line.split(";")]
        if len(f) < 5 or not f[0]:
            continue
        try:
            e = float(f[0]); i = float(f[2])
        except ValueError:
            continue
        di = float(f[3]) if f[3] else None
        origin = f[5] if len(f) > 5 else ""
        if origin not in allowed:
            continue
        out.append((e, i, di, f[4], origin, f[2], f[3]))
    return out, ref.group(1).strip() if ref else ""


def compare(c):
    """Сверка по соединению `c` с nucdb; строки таблицы (dr_pks — ключи строк базы)."""
    rows = []
    for name in NUCS:
        lines, ref = lara(name)
        if lines is None:
            rows.append(dict(nuclide=name, line="—", kind="", ours="", ours_unc="", ddep="", ddep_unc="",
                             rel_pct="", sigma="", ref="DDEP-файла нет", dr_pks="", energy="", nucid=nucid(name),
                             ddep_text="", ddep_unc_text=""))
            continue
        nid = nucid(name)
        ours = c.execute("select type_a, type_c, energy_num, intensity, intensity_unc, intensity_num, dr_pk"
                         " from decay_radiations where parent_nucid = $n and type_a in ('G','X')"
                         " and energy_num not null" + chains.LEVEL_CLAUSE,
                         {chains.LEVEL_PARAM: nid}).fetchall()
        for e, i, di, typ, origin, i_txt, di_txt in lines:
            if i < MIN_I:
                continue
            if typ == "g":
                # окно ±0.6 кэВ с ОБЕИХ сторон: дублет одной поставки против
                # суммы другой (Eu-152 443.96 + 444.01, Pa-234 880.5 ×2)
                win = 0.6
                cand = [o for o in ours if o[0] == "G" and abs(o[2] - e) <= win]
                dd = [x for x in lines if x[3] == "g" and abs(x[0] - e) <= win]
                if len(dd) > 1 and min(x[0] for x in dd) < e:
                    continue          # дублет DDEP считается один раз, по младшей линии
                i = sum(x[1] for x in dd)
                if len(dd) > 1:
                    i_txt = di_txt = ""        # сумма дублета — текста DDEP у неё нет
                di = math.sqrt(sum((x[2] or 0.0) ** 2 for x in dd)) or None
                label = "%.2f" % e + (" (%d+%d)" % (len(cand), len(dd)) if len(cand) > 1 or len(dd) > 1 else "")
            elif typ in XMAP:
                cand = [o for o in ours if o[0] == "X" and (o[1] or "").strip() in XMAP[typ]
                        and abs(o[2] - e) <= 1.0]
                cand.sort(key=lambda o: abs(o[2] - e))
                cand = cand[:1]
                label = "%s %.2f" % (typ[1:], e)
            else:
                continue
            if not cand:
                rows.append(dict(nuclide=name, line=label, kind=typ, ours="нет", ours_unc="", ddep=i,
                                 ddep_unc=di, rel_pct="", sigma="", ref=ref, dr_pks="", energy=e, nucid=nid,
                                 ddep_text=i_txt, ddep_unc_text=di_txt))
                continue
            our_i = sum(o[5] for o in cand)
            us = [ensdf_unc(o[3] or "", o[4] or "") for o in cand]
            our_u = None if any(u is None for u in us) else math.sqrt(sum(u * u for u in us))
            rel = 100.0 * (our_i / i - 1.0)
            s = None
            if di and our_u is not None:
                s = (our_i - i) / math.hypot(di, our_u)
            elif di:
                s = (our_i - i) / di
            rows.append(dict(nuclide=name, line=label, kind=typ, ours=our_i, ours_unc=our_u, ddep=i,
                             ddep_unc=di, rel_pct=rel, sigma=s, ref=ref,
                             dr_pks=",".join(str(o[6]) for o in cand), energy=e, nucid=nid,
                             ddep_text=i_txt, ddep_unc_text=di_txt))
    return rows


def main():
    rows = compare(chains.conn())
    with open(os.path.join(HERE, "ddep_compare.tsv"), "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()), delimiter="\t")
        w.writeheader()
        for r in rows:
            w.writerow(r)
    fmt = lambda v, p: ("%." + str(p) + "g") % v if isinstance(v, float) else str(v)
    n_over1 = n_over2 = 0
    for r in rows:
        s = r["sigma"]
        mark = ""
        if isinstance(s, float):
            if abs(s) > 2: mark = "  ⛔>2σ"; n_over2 += 1
            elif abs(s) > 1: mark = "  ⚠>1σ"; n_over1 += 1
        print("%-8s %-14s наша %-8s ±%-7s DDEP %-8s ±%-7s %7s %% %6s σ  %s%s" % (
            r["nuclide"], r["line"], fmt(r["ours"], 5), fmt(r["ours_unc"], 2), fmt(r["ddep"], 5),
            fmt(r["ddep_unc"], 2), fmt(r["rel_pct"], 3) if r["rel_pct"] != "" else "",
            ("%+.1f" % s) if isinstance(s, float) else "", r["ref"], mark))
    print("опорных линий %d; расхождение >1σ (до 2σ) %d, >2σ %d" % (
        sum(1 for r in rows if r["line"] != "—"), n_over1, n_over2))


if __name__ == "__main__":
    main()
