# -*- coding: utf-8 -*-
"""Сверка нашей omega_K (EADL, расчёт) с двумя внешними поставками измерений.

  наша      : matdb.eadl_radiative, сумма probability по вакансии K (= xray_fluorescence.omega_k)
  xraylib   : data/fluor_yield.dat  — Krause-ORNL-5399(1978) + замены Campbell-2009, Ayri-2021, Kaur-2021
  xraydb    : xraydb.sqlite, xray_levels.fluorescence_yield — свод Elam-Ravel-Sieber-2002 (Krause-1979)
"""
import sqlite3, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

if len(sys.argv) != 4:
    sys.exit("usage: omega_compare.py <matdb.sqlite> <fluor_yield.dat> <xraydb.sqlite>")
MATDB, FLUOR, XRAYDB = sys.argv[1], sys.argv[2], sys.argv[3]

SHELL_K = 1  # vacancy_shell в eadl_radiative: 1 = K

# --- наши ---
c = sqlite3.connect(MATDB)
ours = dict(c.execute(
    "select z, sum(probability) from eadl_radiative where vacancy_shell=? group by z", (SHELL_K,)))
stored = dict(c.execute("select z, omega_k from xray_fluorescence"))

# --- xraylib ---
xl = {}          # (Z, shell) -> omega
for line in io.open(FLUOR, encoding="utf-8"):
    p = line.split()
    if len(p) == 3:
        xl[(int(p[0]), p[1])] = float(p[2])

# --- xraydb ---
SYM = {}
xdb_c = sqlite3.connect(XRAYDB)
for z, sym in xdb_c.execute("select atomic_number, element from elements"):
    SYM[sym] = z
xdb = {}
for el, edge, fy in xdb_c.execute(
        "select element, iupac_symbol, fluorescence_yield from xray_levels"):
    if el in SYM and fy is not None:
        xdb[(SYM[el], edge)] = float(fy)

# --- сверка omega_K ---
print("### omega_K: наша (EADL) против двух поставок измерений\n")
print("  Z   наша      xraylib   xraydb    наша/xraylib  наша/xraydb")
rows = []
for z in sorted(ours):
    a = ours[z]
    b = xl.get((z, "K"))
    d = xdb.get((z, "K"))
    if not b or not d or b <= 0 or d <= 0:
        continue
    rows.append((z, a, b, d, a / b, a / d))
    if z % 10 == 0 or z in (5, 26, 29, 53, 55, 82, 92):
        print("%3d  %8.5f  %8.5f  %8.5f   %8.4f     %8.4f" % (z, a, b, d, a / b, a / d))

import statistics as st
for name, i in (("наша/xraylib", 4), ("наша/xraydb", 5)):
    v = [r[i] for r in rows]
    lo = min(rows, key=lambda r: r[i]); hi = max(rows, key=lambda r: r[i])
    print("\n%s : медиана %.4f, среднее %.4f, разброс %.4f (Z=%d) … %.4f (Z=%d), N=%d"
          % (name, st.median(v), sum(v) / len(v), lo[i], lo[0], hi[i], hi[0], len(v)))

# xraylib против xraydb — насколько независимы сами поставки
v = [r[2] / r[3] for r in rows]
print("xraylib/xraydb: медиана %.4f, разброс %.4f … %.4f" % (st.median(v), min(v), max(v)))

# --- omega_L ---
print("\n### omega_L1..L3: у нас (EADL) против поставок")
LSH = {3: "L1", 5: "L2", 6: "L3"}   # EADL: 1=K, 2=L, 3=L1, 4=L23, 5=L2, 6=L3
oursL = {}
for vac, lbl in LSH.items():
    for z, s in c.execute("select z, sum(probability) from eadl_radiative "
                          "where vacancy_shell=? group by z", (vac,)):
        oursL[(z, lbl)] = s
print("  Z  shell   наша      xraylib   xraydb    наша/xraylib")
for z in (29, 53, 55, 74, 82, 92):
    for lbl in ("L1", "L2", "L3"):
        a = oursL.get((z, lbl)); b = xl.get((z, lbl)); d = xdb.get((z, lbl))
        if a and b:
            print("%3d  %-5s %8.5f  %8.5f  %8s   %8.4f"
                  % (z, lbl, a, b, ("%.5f" % d) if d else "-", a / b))
for lbl in ("L1", "L2", "L3"):
    v = [oursL[(z, lbl)] / xl[(z, lbl)] for z in range(20, 101)
         if oursL.get((z, lbl)) and xl.get((z, lbl))]
    if v:
        print("%s : медиана %.4f, разброс %.4f … %.4f, N=%d"
              % (lbl, st.median(v), min(v), max(v), len(v)))

# --- хранимая копия против пересчёта (C-7) ---
bad = [(z, stored[z], ours[z]) for z in stored if abs(stored[z] - ours.get(z, -1)) > 1e-9]
print("\nxray_fluorescence.omega_k против пересчёта по eadl_radiative: расхождений %d из %d"
      % (len(bad), len(stored)))
