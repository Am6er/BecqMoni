# -*- coding: utf-8 -*-
"""Посчитать omega_K САМИМ, а не взять готовой таблицей.

Три счёта:
  1. omega_K = Г_R / (Г_R + Г_A) из наших же ширин (eadl_radiative + eadl_auger).
     Заодно проверка нормировки: сумма обеих половин обязана быть единицей.
  2. Своя оценка по форме Бурхопа [w/(1-w)]^(1/4) = A + B*Z + C*Z^3,
     подогнанной по измерениям (xraylib: Krause ORNL-5399 + замены 2009-2021).
  3. Где сидит провал EADL против измерений и какова поправка поимённо.
"""
import sqlite3, io, sys, statistics as st
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

if len(sys.argv) != 3:
    sys.exit("usage: omega_fit.py <matdb.sqlite> <xraylib fluor_yield.dat>")
MATDB, FLUOR = sys.argv[1], sys.argv[2]
KVAC = 1

c = sqlite3.connect(MATDB)
rad = dict(c.execute("select z, sum(probability) from eadl_radiative "
                     "where vacancy_shell=? group by z", (KVAC,)))
aug = dict(c.execute("select z, sum(probability) from eadl_auger "
                     "where vacancy_shell=? group by z", (KVAC,)))
stored = dict(c.execute("select z, omega_k from xray_fluorescence"))

# --- измерения ---
meas = {}
for line in io.open(FLUOR, encoding="utf-8"):
    p = line.split()
    if len(p) == 3 and p[1] == "K":
        meas[int(p[0])] = float(p[2])

# ---------- 1. omega из ширин ----------
print("## 1. omega_K = Г_R/(Г_R+Г_A), посчитанная из наших ширин\n")
zs = sorted(set(rad) & set(aug))
norm = [(z, rad[z] + aug[z]) for z in zs]
worst = max(norm, key=lambda t: abs(t[1] - 1.0))
print("нормировка Г_R+Г_A: медиана %.9f, худший Z=%d -> %.9f, N=%d"
      % (st.median([n for _, n in norm]), worst[0], worst[1], len(zs)))

own = {z: rad[z] / (rad[z] + aug[z]) for z in zs}
d = [abs(own[z] - rad[z]) for z in zs]
print("своя omega против простой суммы Г_R: макс |разница| %.2e  (%s)"
      % (max(d), "одно и то же число" if max(d) < 1e-4 else "РАЗНЫЕ величины"))

# ---------- 2. своя подгонка по измерениям ----------
def solve(A, b):
    n = len(A)
    M = [row[:] + [b[i]] for i, row in enumerate(A)]
    for i in range(n):
        p = max(range(i, n), key=lambda r: abs(M[r][i]))
        M[i], M[p] = M[p], M[i]
        for r in range(n):
            if r != i:
                f = M[r][i] / M[i][i]
                for k in range(i, n + 1):
                    M[r][k] -= f * M[i][k]
    return [M[i][n] / M[i][i] for i in range(n)]

fitz = [z for z in sorted(meas) if 11 <= z <= 99 and 0 < meas[z] < 1]
def burhop_design(z):
    return [1.0, float(z), float(z) ** 3]
y = [(meas[z] / (1 - meas[z])) ** 0.25 for z in fitz]
X = [burhop_design(z) for z in fitz]
# нормальные уравнения
n = 3
ATA = [[sum(X[i][a] * X[i][b] for i in range(len(X))) for b in range(n)] for a in range(n)]
ATy = [sum(X[i][a] * y[i] for i in range(len(X))) for a in range(n)]
A, B, C = solve(ATA, ATy)
print("\n## 2. Своя подгонка формы Бурхопа по %d измерениям (11 <= Z <= 99)\n" % len(fitz))
print("   [w/(1-w)]^(1/4) = %.6f + %.6e*Z + %.6e*Z^3" % (A, B, C))

def wfit(z):
    t = A + B * z + C * z ** 3
    t4 = t ** 4
    return t4 / (1 + t4)

res = [wfit(z) / meas[z] for z in fitz]
print("   подгонка/измерение: медиана %.4f, разброс %.4f (Z=%d) … %.4f (Z=%d)"
      % (st.median(res),
         min(res), fitz[res.index(min(res))], max(res), fitz[res.index(max(res))]))
big = [(z, wfit(z) / meas[z]) for z in fitz if abs(wfit(z) / meas[z] - 1) > 0.03]
print("   хуже 3 %%: %d элементов %s" % (len(big), [z for z, _ in big][:12]))

# ---------- 3. где провал ----------
print("\n## 3. EADL против измерений и против своей подгонки\n")
print("   Z  элем  EADL      измер.    своя      EADL/изм  своя/изм")
SYM = {6:"C",10:"Ne",13:"Al",14:"Si",20:"Ca",26:"Fe",29:"Cu",30:"Zn",32:"Ge",
       35:"Br",40:"Zr",50:"Sn",53:"I",55:"Cs",56:"Ba",74:"W",82:"Pb",92:"U"}
for z in sorted(SYM):
    if z in own and z in meas and meas[z] > 0:
        print("%4d  %-4s %8.5f  %8.5f  %8.5f   %7.4f   %7.4f"
              % (z, SYM[z], own[z], meas[z], wfit(z), own[z] / meas[z], wfit(z) / meas[z]))

comm = [z for z in zs if z in meas and meas[z] > 0 and z >= 11]
e_r = [own[z] / meas[z] for z in comm]
f_r = [wfit(z) / meas[z] for z in comm]
print("\n   по %d элементам Z>=11:" % len(comm))
print("   EADL/измерение : медиана %.4f, худшее %.4f" % (st.median(e_r), min(e_r)))
print("   своя /измерение: медиана %.4f, худшее %.4f" % (st.median(f_r), min(f_r)))

# на скольких элементах своя подгонка ближе к измерению, чем EADL
better = sum(1 for z in comm if abs(wfit(z) - meas[z]) < abs(own[z] - meas[z]))
print("   своя ближе к измерению, чем EADL, на %d из %d элементов" % (better, len(comm)))

# ---------- поправочный множитель для того, что реально в работе ----------
print("\n## 4. Поправка к нашей omega_K поимённо (что умножать)")
for z in (13, 14, 26, 29, 30, 32, 35, 53, 55, 56, 82):
    if z in own and z in meas and meas[z] > 0:
        print("   %-3s Z=%-3d  x %.4f" % (SYM.get(z, "?"), z, meas[z] / own[z]))
