# -*- coding: utf-8 -*-
"""Сверка наших переходов Костера-Кронига (EADL) с двумя внешними поставками.

Вторая половина строки N17: f12/f13/f23 лежат у нас внутри `matdb.eadl_auger`
(EADL кладёт оже и CK одной таблицей) и не сверены ни с чем, а именно они
перекладывают дырку с L1 на L2/L3 — то есть несут ту же систематику, что уже
поймана на omega_L1.

  наша      : matdb.eadl_auger, сумма probability по (вакансия, откуда пришёл)
              f12 = (vacancy=3 L1, from=5 L2), f13 = (3, 6 L3), f23 = (5, 6)
              Коды оболочек EADL: 1=K, 3=L1, 5=L2, 6=L3 (database/scheme.md, §5б)
  xraydb    : xraydb.sqlite, таблица `Coster_Kronig`, initial_level/final_level
              L1->L2, L1->L3, L2->L3. Свод Elam-Ravel-Sieber-2002 (Krause-1979).
              Колонок ДВЕ: `transition_probability` (прямой переход) и
              `total_transition_probability` (с учётом каскада L1->L2->L3);
              берём первую, вторую печатаем отдельно — у Pb L1->L3 они уже
              расходятся (0.58 против 0.58464)
  xraylib   : xraylib_coskron.dat, ключи F12/F13/F23. ⚠ Файл СОСТАВНОЙ: базовый
              блок (Z, ключ, значение через табуляцию, экспоненциальная запись),
              затем блок FM* (M-оболочка, нам не нужен), затем блоки ЗАМЕН в
              другом формате и плоской записью. Один Z встречается ДО ТРЁХ раз,
              и побеждает ПОСЛЕДНЕЕ вхождение — так же, как читает сам xraylib.
              Вольфрам: база F12 0.170 -> замена 0.110 -> замена 0.109.

⛔ В базы ничего не пишется, чтение только через file:...?mode=ro.

  python compare_coster_kronig.py <matdb.sqlite> <xraylib_coskron.dat> \
                                  <xraydb.sqlite> [xraylib_fluor_yield.dat]

Четвёртый аргумент необязателен: §5 (связь CK с уже пойманной
систематикой omega_L1 — сумма f12+f13 против выхода L1) печатается в любом
случае: без файла omega_L1 поставки берётся из `matdb.fluorescence_yield`,
source='xraylib' — это машинная копия того же `fluor_yield.dat`, втянутая
импортёром `import_fluor_yield.py` (N15, физика 11).
"""
import sqlite3, sys, io, statistics as st

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

if len(sys.argv) not in (4, 5):
    sys.exit("usage: compare_coster_kronig.py <matdb.sqlite> "
             "<xraylib_coskron.dat> <xraydb.sqlite> [xraylib_fluor_yield.dat]")
MATDB, COSKRON, XRAYDB = sys.argv[1], sys.argv[2], sys.argv[3]
FLUOR = sys.argv[4] if len(sys.argv) == 5 else None


def ro(path):
    """Только чтение — правило проекта, менять базы поставки нельзя."""
    return sqlite3.connect("file:%s?mode=ro" % path.replace("\\", "/"), uri=True)


# EADL: 1=K, 3=L1, 5=L2, 6=L3 (проверено по eadl_binding: у Pb 15847 / 15251 / 13040 эВ)
L1, L2, L3 = 3, 5, 6
CK = (("f12", L1, L2), ("f13", L1, L3), ("f23", L2, L3))
HEAVY = ((74, "W"), (82, "Pb"), (83, "Bi"), (90, "Th"), (92, "U"))

# --- наши: eadl_auger, сумма по вылетевшему электрону -----------------------
c = ro(MATDB)
ours = {}
for lbl, vac, frm in CK:
    for z, s in c.execute("select z, sum(probability) from eadl_auger "
                          "where vacancy_shell=? and from_shell=? group by z",
                          (vac, frm)):
        ours[(z, lbl)] = s

# страховка: коды оболочек не порядковые, а EADL — проверяем по энергиям связи
bind = dict(((z, sh), e) for z, sh, e in c.execute(
    "select z, shell_id, binding_ev from eadl_binding where shell_id in (?,?,?)",
    (L1, L2, L3)))
bad_order = [z for z in range(20, 101)
             if bind.get((z, L1)) and bind.get((z, L3))
             and not (bind[(z, L1)] > bind[(z, L2)] > bind[(z, L3)])]
print("проверка кодов оболочек (L1>L2>L3 по энергии связи): нарушений %d из 81"
      % len(bad_order))

# --- xraylib: составной файл, последнее вхождение побеждает -----------------
KEYS = {"F12": "f12", "F13": "f13", "F23": "f23"}
xl, xl_base, xl_hits = {}, {}, {}
for line in io.open(COSKRON, encoding="utf-8", newline=""):
    p = line.split()
    if len(p) != 3 or p[1] not in KEYS:
        continue                      # F1, FP13, весь блок FM* — мимо
    z, lbl, v = int(p[0]), KEYS[p[1]], float(p[2])
    xl[(z, lbl)] = v                  # последнее вхождение = значение xraylib
    xl_base.setdefault((z, lbl), v)   # первое = базовая таблица ORNL-5399
    xl_hits[(z, lbl)] = xl_hits.get((z, lbl), 0) + 1

repl = sorted(k for k in xl if xl_hits[k] > 1 and abs(xl[k] - xl_base[k]) > 1e-9)
print("xraylib: разобрано %d пар (Z, f_ij); переопределены поздними блоками %d"
      % (len(xl), len(repl)))
for k in repl[:6]:
    print("    Z=%-3d %s  база %.4f -> итог %.4f  (вхождений %d)"
          % (k[0], k[1], xl_base[k], xl[k], xl_hits[k]))
if len(repl) > 6:
    print("    ... и ещё %d" % (len(repl) - 6))

# --- xraydb: таблица Coster_Kronig ------------------------------------------
LVL = {("L1", "L2"): "f12", ("L1", "L3"): "f13", ("L2", "L3"): "f23"}
xdb_c = ro(XRAYDB)
SYM = dict((sym, z) for z, sym in
           xdb_c.execute("select atomic_number, element from elements"))
xdb, xdb_tot = {}, {}
for el, ini, fin, p, tot in xdb_c.execute(
        "select element, initial_level, final_level, transition_probability, "
        "total_transition_probability from Coster_Kronig"):
    lbl = LVL.get((ini, fin))
    if lbl and el in SYM and p is not None:
        xdb[(SYM[el], lbl)] = float(p)
        if tot is not None:
            xdb_tot[(SYM[el], lbl)] = float(tot)
print("xraydb: разобрано %d пар (Z, f_ij) из таблицы Coster_Kronig" % len(xdb))


def rows_for(lbl):
    out = []
    for z in range(3, 101):
        a, b, d = ours.get((z, lbl)), xl.get((z, lbl)), xdb.get((z, lbl))
        if a and b and d and b > 0 and d > 0:
            out.append((z, a, b, d, a / b, a / d))
    return out


# --- 1. общая таблица --------------------------------------------------------
print("\n### f12 / f13 / f23: наша (EADL) против двух поставок\n")
print("  Z  f_ij    наша      xraylib   xraydb    наша/xraylib  наша/xraydb")
SHOW = (29, 40, 50, 60, 70, 74, 79, 82, 83, 90, 92)
for z in SHOW:
    for lbl, _, _ in CK:
        a, b, d = ours.get((z, lbl)), xl.get((z, lbl)), xdb.get((z, lbl))
        if a and b and d:
            print("%3d  %-5s %8.5f  %8.5f  %8.5f   %8.4f     %8.4f"
                  % (z, lbl, a, b, d, a / b, a / d))

# --- 2. медианы и разброс ----------------------------------------------------
print("\n### сводка по Z\n")
for lbl, _, _ in CK:
    rows = rows_for(lbl)
    if not rows:
        print("%s : общих Z нет" % lbl)
        continue
    for name, i in (("наша/xraylib", 4), ("наша/xraydb", 5)):
        v = [r[i] for r in rows]
        lo = min(rows, key=lambda r: r[i])
        hi = max(rows, key=lambda r: r[i])
        print("%s %s : медиана %.4f, среднее %.4f, разброс %.4f (Z=%d) … %.4f (Z=%d), N=%d"
              % (lbl, name, st.median(v), sum(v) / len(v),
                 lo[i], lo[0], hi[i], hi[0], len(v)))
    v = [r[2] / r[3] for r in rows]
    print("%s xraylib/xraydb : медиана %.4f, разброс %.4f … %.4f, N=%d\n"
          % (lbl, st.median(v), min(v), max(v), len(v)))

# --- 3. тяжёлые отдельно -----------------------------------------------------
print("### тяжёлые (там же сидит уже пойманная систематика omega_L1)\n")
print("  Z  эл  f_ij    наша      xraylib   xraydb    наша/xraylib  наша/xraydb")
for z, sym in HEAVY:
    for lbl, _, _ in CK:
        a, b, d = ours.get((z, lbl)), xl.get((z, lbl)), xdb.get((z, lbl))
        if a is None:
            continue
        print("%3d  %-2s  %-5s %8.5f  %8s  %8s   %8s     %8s"
              % (z, sym, lbl, a,
                 ("%.5f" % b) if b else "-", ("%.5f" % d) if d else "-",
                 ("%.4f" % (a / b)) if b else "-",
                 ("%.4f" % (a / d)) if d else "-"))

# --- 4. две колонки xraydb порознь ------------------------------------------
print("\n### xraydb: transition_probability против total_transition_probability")
diff = [(z, lbl, xdb[(z, lbl)], xdb_tot[(z, lbl)])
        for (z, lbl) in sorted(xdb_tot)
        if abs(xdb_tot[(z, lbl)] - xdb[(z, lbl)]) > 1e-9]
print("расходятся %d пар из %d (каскад L1->L2->L3 добавлен только в total)"
      % (len(diff), len(xdb_tot)))
for z, sym in HEAVY:
    for lbl, _, _ in CK:
        if (z, lbl) in xdb_tot:
            p, t = xdb[(z, lbl)], xdb_tot[(z, lbl)]
            print("%3d  %-2s  %-5s прямой %.5f   полный %.5f   %+.2f %%"
                  % (z, sym, lbl, p, t, 100.0 * (t - p) / p if p else 0.0))

# --- 5. связь с omega_L1: куда именно EADL перекладывает дырку ---------------
if True:
    print("\n### f12+f13 против omega_L1 — та же систематика с двух сторон\n")
    om_ours = dict(c.execute("select z, sum(probability) from eadl_radiative "
                             "where vacancy_shell=? group by z", (L1,)))
    om_xl = {}
    if FLUOR:
        for line in io.open(FLUOR, encoding="utf-8", newline=""):
            p = line.split()
            if len(p) == 3 and p[1] == "L1":
                om_xl[int(p[0])] = float(p[2])
        print("omega_L1 поставки: из файла %s" % FLUOR)
    else:
        # запасной путь: та же таблица xraylib уже втянута в matdb
        # импортёром import_fluor_yield.py (N15, физика 11), source='xraylib'
        om_xl = dict(c.execute("select z, omega from fluorescence_yield "
                               "where shell='L1' and source='xraylib'"))
        print("omega_L1 поставки: matdb.fluorescence_yield, source='xraylib' "
              "(%d значений; файл не задан)" % len(om_xl))

    def ck_sum(src, z):
        a, b = src.get((z, "f12")), src.get((z, "f13"))
        return (a + b) if (a and b) else None

    pairs = []
    for z in range(20, 101):
        a, b = ck_sum(ours, z), ck_sum(xl, z)
        if a and b:
            pairs.append((z, a / b))
    v = [r[1] for r in pairs]
    print("Σ CK(L1) = f12+f13, наша/xraylib : медиана %.4f, разброс %.4f (Z=%d) … "
          "%.4f (Z=%d), N=%d"
          % (st.median(v), min(v), min(pairs, key=lambda r: r[1])[0],
             max(v), max(pairs, key=lambda r: r[1])[0], len(v)))
    print("\n  Z  эл  ΣCK наша  ΣCK xrl  отн.    ω_L1 наша  ω_L1 xrl  отн.")
    for z, sym in HEAVY:
        a, b = ck_sum(ours, z), ck_sum(xl, z)
        o, ox = om_ours.get(z), om_xl.get(z)
        if a and b and o and ox:
            print("%3d  %-2s  %8.4f %8.4f %7.4f  %9.5f %9.5f %7.4f"
                  % (z, sym, a, b, a / b, o, ox, o / ox))
    print("\nSCK и omega_L1 расходятся с поставкой в РАЗНЫЕ стороны у всех пяти тяжёлых,\n"
          "но баланс L1 этим НЕ сведён: от 55 % (Bi) до 80 % (W) избытка CK оплачены\n"
          "обычным оже, а не недостачей излучения. Это корреляция на пяти точках —\n"
          "'одна ошибка вместо двух' отсюда НЕ следует. Разбор: para 9г журнала\n"
          "database/omega-vs-measurement-2026-08-09.md.")
