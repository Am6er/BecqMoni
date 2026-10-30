# -*- coding: utf-8 -*-
"""AMBER279 (29.10.2026): степень фотонной непропорциональности σ_nPR кривых света NaI:Tl / CsI:Tl.

Что меряет (журнал handover/handover-2026-10-29-amber279-photon-npr.md):

 1. σ_nPR фотонных кривых `matdb.scint_photon_light_yield` мерой Ходюка и Доренбоса
    (IEEE TNS 59 (2012), arXiv:1204.4350, ур. 1): (1/(Emax − Emin)) ∫ |f(Emax) − f(E)| dE,
    Emax = 662 кэВ, Emin = 10 кэВ, f — фотопиковая нПР в %, f(662) = 100 %. Два способа
    интерполяции узлов (линейно по ln E, как `FsaLightScale.Interp`, и линейно по E) и два Emin.
 2. Откуда 6.83 / 6.78 % их табл. I: точки NaI:Tl и CsI:Tl рис. 15 той же статьи кончаются на
    100 кэВ (у Ходюка 2010, arXiv:1102.3799, NaI:Tl — синхротрон 9…100 кэВ и нормировка на 662),
    между 100 и 662 кэВ измерений нет; та же мера по точкам рис. 15 и ПРЯМОЙ 100 → 662 кэВ.
 3. NaI(Tl) выше 279 кэВ по измерению: Devare & Tandon, NIM 22 (1963) 253, и собственные точки
    COSINE-100 (Lee et al., EPJ C 84 (2024) 484, arXiv:2401.07462, рис. 6, нормировка на 662).
 4. Корпус: остатки опор привязки шкалы FSA действующей базы (`*_anchors.csv`, известная часть)
    по энергии линии и что с ними сделала бы кривая, дающая σ литературы (перепривязка усилением,
    как форма привязки `line` базы: усиление при нуле по съёмке; и с нулём — для сравнения).

Оцифровка рисунков: векторная графика PDF (pymupdf), калибровка осей по подписям. Встроенные
ряды — прореженная у K-края копия; `--trends-pdf=` / `--cosine-pdf=` оцифровывают заново и
сверяют со встроенными (положительный контроль встроенного).

Код возврата: 0 — все положительные контроли сошлись; 1 — нет. Пишет только в stdout.
Запуск: python tools/effmaker/probes/photon_npr_a279.py [--matdb=…] [--base=…] [--trends-pdf=…]
[--cosine-pdf=…]
"""
import collections
import csv
import glob
import math
import os
import sqlite3
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
EMIN, EMAX = 10.0, 661.657

# Рис. 15 Ходюка 2012, фотопиковая нПР, %: оцифровка векторной графики, прорежено до шага 0.5 кэВ
# на 29.5…40.5 кэВ (K-провал); СЫРЫЕ числа оцифровки — поправка подписей оси FIG15_OFFSET
# прибавляется при чтении (fig15()).
FIG15 = {
    "NaI:Tl": [
        (10.04, 113.70), (10.50, 114.08), (11.02, 114.62), (12.03, 115.53), (15.04, 116.56), (20.04, 116.90),
        (25.01, 116.53), (30.12, 115.48), (30.52, 115.40), (31.02, 115.32), (31.49, 115.22), (31.99, 115.12),
        (32.50, 115.01), (33.00, 114.85), (33.50, 114.30), (34.00, 114.00), (34.50, 113.88), (35.00, 113.90),
        (35.49, 113.98), (35.99, 114.10), (36.50, 114.23), (37.00, 114.37), (37.50, 114.49), (38.00, 114.62),
        (38.50, 114.71), (39.00, 114.79), (39.50, 114.89), (39.90, 114.95), (45.02, 115.53), (50.02, 115.50),
        (55.02, 115.25), (60.02, 114.68), (64.99, 114.19), (69.99, 113.79), (74.99, 113.28), (79.99, 112.79),
        (85.00, 112.33), (90.00, 111.79), (95.00, 111.25), (100.00, 110.85)],
    "CsI:Tl": [
        (12.03, 114.30), (15.04, 116.36), (20.04, 116.73), (25.01, 116.19), (30.01, 115.30), (32.09, 114.90),
        (32.52, 114.78), (33.01, 114.73), (33.52, 114.47), (34.02, 114.19), (34.51, 114.02), (35.04, 113.94),
        (35.52, 113.95), (36.02, 113.99), (36.50, 113.96), (36.99, 113.89), (37.52, 113.90), (38.02, 113.92),
        (38.51, 113.99), (39.02, 114.08), (39.51, 114.20), (39.93, 114.25), (45.02, 114.82), (50.02, 114.96),
        (55.02, 114.76), (60.02, 114.30), (69.99, 113.33), (79.99, 112.45), (90.00, 111.48), (100.00, 110.88)],
}
FIG15_OFFSET = 0.3      # подписи оси ниже засечек: текст Ходюка 2010 даёт 111.2/115.8/114.1/117.2


def fig15(m):
    return [(e, y + FIG15_OFFSET) for e, y in FIG15[m]]
KHODYUK2010_TEXT = [(100.0, 111.2), (50.0, 115.8), (34.5, 114.1), (20.0, 117.2)]
TABLE_I = {"NaI:Tl": 6.83, "CsI:Tl": 6.78}      # σ_nPR, табл. I Ходюка 2012
CSI_10KEV = 112.0                                # CsI:Tl на 10 кэВ — табл. I (экстраполяция авторов)
# Рис. 6 COSINE 2024 (NaI(Tl), к 662 кэВ), оцифровка векторной графики, ±0.0001 по засечкам
DEVARE = [(279.0, 102.46), (323.0, 102.26), (412.0, 101.17), (511.0, 100.87), (835.0, 99.24), (889.0, 98.56),
          (1114.0, 98.31), (1274.0, 98.16), (1369.0, 97.65), (1837.0, 96.90), (2754.0, 96.85)]
COSINE = [(294.0, 103.47), (509.0, 99.80), (605.0, 99.22), (1273.0, 97.83), (1459.0, 97.84), (1759.0, 97.74),
          (2611.0, 97.25)]
COSINE_ERR = 1.0     # полуширина планок рис. 6, % (оцифровка: 0.99…1.02)
FAIL = []


def check(ok, what):
    print("  [%s] %s" % ("ok" if ok else "ОТКАЗ", what))
    if not ok:
        FAIL.append(what)


def interp(t, e, log=True):
    if e <= t[0][0]:
        return t[0][1]
    for i in range(1, len(t)):
        if e <= t[i][0]:
            (e0, r0), (e1, r1) = t[i - 1], t[i]
            u = math.log(e / e0) / math.log(e1 / e0) if log else (e - e0) / (e1 - e0)
            return r0 + (r1 - r0) * u
    return t[-1][1]


def sigma(f, emin=EMIN, emax=EMAX, n=60000):
    """ур. 1 Ходюка 2012 средней точкой; f(E) в %, f(emax) := 100."""
    h = (emax - emin) / n
    return sum(abs(100.0 - f(emin + (k + 0.5) * h)) for k in range(n)) / n


def band(f, a, b, n=20000):
    h = (b - a) / n
    return sum(abs(100.0 - f(a + (k + 0.5) * h)) for k in range(n)) * h / (EMAX - EMIN)


def read_matdb(path):
    c = sqlite3.connect("file:%s?mode=ro" % path.replace("\\", "/"), uri=True)
    out = {}
    for m in ("NaI:Tl", "CsI:Tl", "LaBr3:Ce", "CeBr3"):
        t = c.execute("select energy_kev, yield_rel from scint_photon_light_yield where material=? "
                      "order by energy_kev", (m,)).fetchall()
        if t:
            ref = interp(t, EMAX)
            out[m] = [(e, 100.0 * r / ref) for e, r in t]
    c.close()
    return out


def straight_from_100(curve_pts, log=True):
    """Кривая до 100 кэВ, дальше прямая по E до 100 % на 662 (как мера с точками до 100 кэВ)."""
    y100 = interp(curve_pts, 100.0, log)

    def f(e):
        if e <= 100.0:
            return interp(curve_pts, e, log)
        if e >= EMAX:
            return interp(curve_pts, e, log)
        return y100 + (100.0 - y100) * (e - 100.0) / (EMAX - 100.0)
    return f


def digitize_trends(pdf):
    """Рис. 15 Ходюка 2012 (стр. 8): чёрные точки NaI:Tl, красные кружки CsI:Tl."""
    import pymupdf
    p = pymupdf.open(pdf)[7]
    glyphs = [g["rect"] for g in p.get_drawings() if g.get("type") == "f"
              and str(g.get("fill")) == "(0.0, 0.0, 0.0)" and 3 < g["rect"].height < 8]
    xrow = sorted((r for r in glyphs if 330 < r.y0 < 336 and 340 < r.x0 < 560), key=lambda r: r.x0)
    # подписи оси x: «0» отдельно, «20»…«90» парами цифр; масштаб — по центрам «20» и «90»
    x0 = (xrow[0].x0 + xrow[0].x1) / 2
    c20 = (xrow[3].x0 + xrow[4].x1) / 2
    c90 = (xrow[17].x0 + xrow[18].x1) / 2
    kx = (c90 - c20) / 70.0
    ycol = [r for r in glyphs if 334 < r.x0 < 338 and 185 < r.y0 < 260]     # «1» у 130/120/110/100
    yc = sorted(set(round((r.y0 + r.y1) / 2, 2) for r in ycol))
    ky = (yc[-1] - yc[0]) / 30.0
    y100 = yc[-1]
    pts = {"NaI:Tl": [], "CsI:Tl": []}
    for g in p.get_drawings():
        r = g["rect"]
        if not (1.9 < r.width < 2.1 and 1.9 < r.height < 2.1 and 340 < r.x0 < 560 and 180 < r.y0 < 330):
            continue
        e = ((r.x0 + r.x1) / 2 - x0) / kx
        y = 100.0 + (y100 - (r.y0 + r.y1) / 2) / ky
        if y <= 100.0:
            continue
        if g.get("type") == "fs" and str(g.get("fill")) == "(0.0, 0.0, 0.0)":
            pts["NaI:Tl"].append((e, y))
        elif g.get("type") == "fs" and str(g.get("color")) == "(1.0, 0.0, 0.0)":
            pts["CsI:Tl"].append((e, y))
    return {m: sorted(v) for m, v in pts.items()}, (x0, kx, y100, ky)


def digitize_cosine(pdf):
    """Рис. 6 COSINE 2024 (стр. 8): чёрные точки — свои данные, оранжевые — Devare & Tandon."""
    import pymupdf
    p = pymupdf.open(pdf)[7]
    spans = {}
    for b in p.get_text("dict")["blocks"]:
        for ln in b.get("lines", []):
            for s in ln["spans"]:
                spans.setdefault(s["text"].strip(), []).append(s["bbox"])
    cx = lambda t: [(b[0] + b[2]) / 2 for b in spans[t] if 440 < b[1] < 450 and b[0] < 280][0]
    cy = lambda t: [(b[1] + b[3]) / 2 for b in spans[t] if b[0] < 80 and 320 < b[1] < 445][0]
    kx = (cx("2500") - cx("500")) / 2000.0
    x0 = cx("500") - 500.0 * kx
    ky = (cy("0.96") - cy("1.04")) / 0.08
    y1 = cy("1.00")
    own, ref, ticks = [], [], []
    for g in p.get_drawings():
        r = g["rect"]
        if not (70 <= r.x0 <= 275 and 320 <= r.y0 <= 440):
            continue
        e = ((r.x0 + r.x1) / 2 - x0) / kx
        y = 100.0 * (1.0 + (y1 - (r.y0 + r.y1) / 2) / ky)
        if abs(r.width - 3.0) < 0.05 and abs(r.height - 3.0) < 0.05 and str(g.get("fill")) == "(0.0, 0.0, 0.0)":
            own.append((e, y))
        elif abs(r.width - 1.73) < 0.05 and g.get("fill") and g["fill"][0] > 0.8:
            ref.append((e, y))
        elif r.height < 0.05 and abs(r.width - 3.5) < 0.05:
            ticks.append(y)
    # легенда (точка на 1915 кэВ, 101.4 %) — не данные
    own = sorted(q for q in own if not (1900 < q[0] < 1930))
    ref = sorted(q for q in ref if not (1900 < q[0] < 1930))
    return own, ref, sorted(ticks)


def main(argv):
    opt = dict(a[2:].split("=", 1) for a in argv if a.startswith("--") and "=" in a)
    matdb = opt.get("matdb", os.path.join(ROOT, "BecquerelMonitor", "matdb.sqlite"))
    base = opt.get("base", os.path.join(ROOT, "tools", "pie", "out_rev51_full"))
    curves = read_matdb(matdb)

    print("== 0. Положительные контроли меры")
    check(abs(sigma(lambda e: 100.0)) < 1e-12, "σ пропорционального = 0")
    lin = lambda e: 110.0 - 10.0 * (e - EMIN) / (EMAX - EMIN)          # прямая 110 → 100: σ = 5 точно
    check(abs(sigma(lin) - 5.0) < 1e-6, "σ прямой 110 %% → 100 %% = 5.000 (вышло %.6f)" % sigma(lin))
    for e, y in KHODYUK2010_TEXT:
        got = interp(fig15("NaI:Tl"), e, log=False)
        check(abs(got - y) <= 0.25, "оцифровка рис. 15 NaI:Tl %g кэВ: %.2f против текста Ходюка 2010 %.1f"
              % (e, got, y))

    print("\n== 1. σ_nPR кривых matdb (ур. 1 Ходюка 2012), %")
    for m, t in curves.items():
        fl = lambda e, t=t: interp(t, e, True)
        fi = lambda e, t=t: interp(t, e, False)
        print("  %-8s ln E: %.2f (Emin 10.5: %.2f); по E: %.2f;  полосы ln E 10–100 %.2f, 100–200 %.2f, "
              "200–356 %.2f, 356–662 %.2f" % (m, sigma(fl), sigma(fl, 10.5), sigma(fi), band(fl, 10, 100),
                                               band(fl, 100, 200), band(fl, 200, 356), band(fl, 356, EMAX)))
    for m in ("NaI:Tl", "CsI:Tl"):
        if m in curves:
            print("  %s к 662: " % m + "  ".join("%g: %.2f" % (e, interp(curves[m], e)) for e in
                                                  (10, 20, 50, 100, 122, 200, 279, 356, 412, 511, 1000, 2614)))

    print("\n== 2. Откуда 6.83 / 6.78: точки рис. 15 (до 100 кэВ) + прямая 100 → 662 кэВ")
    for m in ("NaI:Tl", "CsI:Tl"):
        k = fig15(m)
        if k[0][0] > EMIN + 0.5:
            k = [(EMIN, CSI_10KEV)] + k
        f_rec = straight_from_100(k, log=False)
        s_rec = sigma(f_rec)
        check(abs(s_rec - TABLE_I[m]) <= 0.05, "%s: реконструкция %.2f %% против табл. I %.2f %% "
              "(10–100 кэВ %.2f, 100–662 кэВ %.2f — интерполяция прямой)" % (
                  m, s_rec, TABLE_I[m], band(f_rec, 10, 100), band(f_rec, 100, EMAX)))
        if m in curves:
            f_m = straight_from_100(curves[m])
            print("     модель matdb с той же прямой 100 → 662: %.2f %%; модель целиком: %.2f %%; "
                  "10–100 кэВ модель / рис. 15: %.2f / %.2f" % (sigma(f_m), sigma(lambda e: interp(curves[m], e)),
                                                               band(lambda e: interp(curves[m], e), 10, 100),
                                                               band(f_rec, 10, 100)))
    for m in ("LaBr3:Ce", "CeBr3"):
        if m in curves:
            print("  к сведению, %s: целиком %.2f %%, с прямой 100 → 662 %.2f %%" % (
                m, sigma(lambda e: interp(curves[m], e)), sigma(straight_from_100(curves[m]))))

    print("\n== 3. NaI(Tl) выше 279 кэВ: измерения против модели (к 662), %")
    nai = curves["NaI:Tl"]
    line = straight_from_100(nai)
    for lab, data in (("Devare–Tandon 1963 (рис. 6 COSINE)", DEVARE), ("COSINE-100 2024 (±%.0f %%)" % COSINE_ERR,
                                                                         COSINE)):
        print("  %s:" % lab)
        print("     " + "  ".join("%g: %.2f/%.2f (%+.2f)" % (e, interp(nai, e), y, interp(nai, e) - y)
                                  for e, y in data))
        k = fig15("NaI:Tl") + [q for q in data if q[0] < EMAX] + [(EMAX, 100.0)]
        print("     σ по рис. 15 + этим точкам (ln E между ними): %.2f %%" % sigma(lambda e, k=k: interp(k, e)))
    print("  прямая 100 → 662 там же: " + "  ".join("%g: %.2f" % (e, line(e)) for e, _ in DEVARE[:4]))
    dev = [interp(nai, e) - y for e, y in DEVARE if e < 1000]
    check(max(abs(x) for x in dev) < 1.5, "модель NaI:Tl в 1.5 %% от Devare–Tandon на 279…511 кэВ (макс %.2f)"
          % max(abs(x) for x in dev))
    devl = [line(e) - y for e, y in DEVARE[:4]]
    print("  прямая 100 → 662 от Devare–Tandon на 279…511 кэВ: " + " ".join("%+.2f" % x for x in devl))

    if "trends-pdf" in opt:
        print("\n== 2а. Повторная оцифровка рис. 15 из PDF")
        pts, cal = digitize_trends(opt["trends-pdf"])
        print("  калибровка: x0 %.2f, %.4f pt/кэВ; 100 %% на %.2f, %.4f pt/%%" % cal)
        for m in ("NaI:Tl", "CsI:Tl"):
            raw = pts[m]
            cor = [(e, y + FIG15_OFFSET) for e, y in raw]
            k = cor if m == "NaI:Tl" else [(EMIN, CSI_10KEV)] + cor
            s = sigma(straight_from_100(k, log=False))
            # встроенный ряд — средние по 0.5 кэВ у K-провала, отсюда до ~0.15 % на изгибе
            d = max(abs(interp(raw, e, False) - y) for e, y in FIG15[m])
            check(d < 0.15 and abs(s - TABLE_I[m]) <= 0.05,
                  "%s: точек %d, встроенный ряд от новой оцифровки ≤ %.3f %%; σ полной оцифровки %.2f "
                  "(табл. I %.2f)" % (m, len(raw), d, s, TABLE_I[m]))
    if "cosine-pdf" in opt:
        print("\n== 3а. Повторная оцифровка рис. 6 COSINE из PDF")
        own, ref, ticks = digitize_cosine(opt["cosine-pdf"])
        check(len(ticks) >= 5 and max(abs(t - v) for t, v in zip(ticks, (96, 98, 100, 102, 104))) < 0.02,
              "засечки оси y читаются как 96/98/100/102/104: " + " ".join("%.3f" % t for t in ticks))
        d1 = max(abs(a[1] - b[1]) for a, b in zip(own, COSINE))
        ref = [q for q in ref if abs(q[0] - EMAX) > 5.0]        # точка нормировки 662 кэВ = 100 % — не данные
        d2 = max(abs(a[1] - b[1]) for a, b in zip(ref, DEVARE))
        check(len(own) == len(COSINE) and d1 < 0.02, "свои точки COSINE: %d, от встроенных ≤ %.3f %%" % (len(own), d1))
        check(len(ref) == len(DEVARE) and d2 < 0.02, "точки Devare–Tandon: %d, от встроенных ≤ %.3f %%"
              % (len(ref), d2))

    print("\n== 4. Корпус: остатки опор привязки FSA (%s, известная часть), кэВ" % os.path.basename(base))
    mat = {"G1S16": "NaI:Tl", "G1S24": "NaI:Tl", "AS80x80": "NaI:Tl", "ASN16": "CsI:Tl", "RC103": "CsI:Tl"}
    rows = []
    for fn in glob.glob(os.path.join(base, "*_anchors.csv")):
        with open(fn, encoding="utf-8-sig") as fh:
            rows += [r for r in csv.DictReader(fh) if r["det"] in mat and r["part"] == "known"]
    if not rows:
        check(False, "нет опор в %s" % base)
    else:
        rc = lambda m, e: interp(curves[m], e) / 100.0
        rl = lambda m, e: straight_from_100(curves[m])(e) / 100.0
        dls = [abs(float(r["light_shift_kev"]) - float(r["line_kev"]) * (rc(mat[r["det"]], float(r["line_kev"])) - 1))
               for r in rows if float(r["light_shift_kev"]) != 0.0]
        check(max(dls) < 0.01, "световой сдвиг опоры = E·(r(E) − 1) по кривой matdb: %d опор, max |Δ| %.4f кэВ"
              % (len(dls), max(dls)))
        by = collections.defaultdict(list)
        for r in rows:
            if r["used"] == "1":
                by[r["spectrum"]].append(r)
        bands = [(0, 100), (100, 200), (200, 300), (300, 450), (450, 600), (600, 700), (700, 3000)]
        for with_zero in (False, True):
            acc = collections.defaultdict(list)
            for sp, an in by.items():
                if len(an) < (3 if with_zero else 2):
                    continue
                m = mat[an[0]["det"]]
                e = [float(a["line_kev"]) for a in an]
                x = [v * rc(m, v) for v in e]
                w = [1.0 / float(a["sigma_kev"]) ** 2 for a in an]
                d = [v * (rl(m, v) - rc(m, v)) for v in e]
                if with_zero:
                    S, Sx, Sy = sum(w), sum(a * b for a, b in zip(w, x)), sum(a * b for a, b in zip(w, d))
                    Sxx = sum(a * b * b for a, b in zip(w, x))
                    Sxy = sum(a * b * c for a, b, c in zip(w, x, d))
                    D = S * Sxx - Sx * Sx
                    a0, b1 = ((Sxx * Sy - Sx * Sxy) / D, (S * Sxy - Sx * Sy) / D) if abs(D) > 1e-12 else (0, Sxy / Sxx)
                else:
                    a0, b1 = 0.0, sum(a * b * c for a, b, c in zip(w, x, d)) / sum(a * b * b for a, b in zip(w, x))
                for a, v, xx, dd in zip(an, e, x, d):
                    old = float(a["shift_kev"])
                    new = old - (dd - (a0 + b1 * xx))
                    for lo, hi in bands:
                        if lo <= v < hi:
                            acc[(m, lo, hi)].append((old, new, float(a["sigma_kev"])))
            print("  перепривязка %s:" % ("усиление + ноль" if with_zero else "усиление (форма line базы)"))
            for m in ("NaI:Tl", "CsI:Tl"):
                for lo, hi in bands:
                    v = acc.get((m, lo, hi))
                    if not v:
                        continue
                    mo, mn = sum(q[0] for q in v) / len(v), sum(q[1] for q in v) / len(v)
                    zo = math.sqrt(sum((q[0] / q[2]) ** 2 for q in v) / len(v))
                    zn = math.sqrt(sum((q[1] / q[2]) ** 2 for q in v) / len(v))
                    print("    %s %4d–%-4d n=%3d: сейчас %+6.2f (ср.кв. %4.2f σ) → кривая σ литературы %+6.2f "
                          "(ср.кв. %5.2f σ)" % (m, lo, hi, len(v), mo, zo, mn, zn))
    print("\nИТОГ: %s" % ("все контроли сошлись" if not FAIL else "ОТКАЗ контролей: " + "; ".join(FAIL)))
    return 0 if not FAIL else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
