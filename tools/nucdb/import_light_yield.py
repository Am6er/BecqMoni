# -*- coding: utf-8 -*-
"""
Втягивает в nucdb.sqlite непропорциональность светового выхода сцинтилляторов
(TODO F11): параметры механистической модели Пейна и посчитанные из них кривые
относительного выхода L(E)/E для электронов.

Модель (Payne III, ур. 3; форма сверена с Breitenmoser 2023, Nature Comm. 14,
7790, ур. 1 и алгоритм S1 — экспонента ловушек ВЛОЖЕНА в онсагеровскую):

    l(S) = [1 − η · exp(−(S/S_Ons) · exp(−S_Trap/S))] / [1 + S/S_Birks]

где S — коллизионная тормозная способность в МэВ/см, η — доля свободных
e-/h пар после термализации, S_Ons = 36.4 МэВ/см (зафиксирован у Пейна для
всех веществ), S_Trap — экранировка ловушками, S_Birks — тушение
экситон-экситонной аннигиляцией. Свет электрона начальной энергии E:

    L(E) = ∫_0^E l(S(E')) dE'          (Breitenmoser, ур. S30)

и в таблицу пишется относительный выход yield_rel = (L(E)/E) / (L(662)/662),
то есть 1.0 на 662 кэВ — привязка та же, что у фотонных кривых Ходюка.

Умолчания — строки `*_eta_fit_photon`: η ОТКАЛИБРОВАН по измеренной ФОТОННОЙ
непропорциональности сквозь наш же перенос (проба LightScaleProbe, геометрия
Nano16Pro, 1e6 историй, 07.08.2026). Причина: параметры Пейна подогнаны под
электронные данные SLYNCI, а те систематически круче фотонных измерений
(известное расхождение SLYNCI ↔ K-dip-спектроскопия); чистый Payne III через
наш перенос даёт фотонный горб 1.209 у CsI:Tl против измеренных 1.12
(Khodyuk 2012, таблица I). Прибор меряет фотонную кривую — по ней и калибр:

    CsI:Tl  η 0.438 → 0.375: фотонная 10/662 = 1.120 (цель 1.12) ✓
    NaI:Tl  η 0.3725 → 0.33: фотонная 10/662 ≈ 1.141 (цель 1.14) ✓
    CsI:Na  η 0.465 → 0.398: тот же относительный сдвиг, что у CsI:Tl
            (сквозь перенос не проверить — симулятор ставит CsI:Tl по
            веществу кристалла; строка лежит на будущее)

Остаток (в TODO): середина шкалы всё ещё круче измеренной (NaI 60 кэВ:
1.069 у нас против ~1.10 у Ходюка) — одним η форму не выправить; провал у
K-края в переносе выходит ступенькой ВВЕРХ, а не вниз — не хватает
подкэвного обвала кривой (ESTAR кончается на 1 кэВ, кривая зажата) и
раздельного каскада (оже-электроны слиплись с фотоэлектроном). Это вторая
половина F11 — пофит по своим спектрам.

Откуда параметры моделей (все при комнатной температуре +20 °C):

  * CsI:Tl, CsI:Na, NaI:Tl — Payne et al., «Nonproportionality of Scintillator
    Detectors. III. Temperature Dependence» (IEEE TNS 61 (2014) 2771,
    LLNL-JRNL-648819, свободный PDF osti.gov/servlets/purl/1762905), таблица I:
    колонки +40/0/−40 °C, значения растут к холоду; +20 °C — линейная
    интерполяция колонок 0 и +40. LaBr3:Ce, CeBr3 — там же, таблица II
    (колонки +40/0/−35 °C).
  * NaI:Tl (второй источник) — Breitenmoser et al. 2023 (Nature Comm. 14,
    7790, arXiv:2302.05641), байесовский MAP по комптоновскому краю, sum mode:
    η = 0.596, S_Trap = 14.6, S_Birks = 322 МэВ/см. Умолчанием НЕ взят:
    их данные — комптоновские края 477–1612 кэВ, ниже модель чистая
    экстраполяция, и кривая из MAP выходит на 1.72 к 5 кэВ (сходится с их же
    рис. S27b, ось до 1.8) — это противоречит измеренному ФОТОННОМУ отклику
    NaI:Tl (114 % на 10 кэВ, Khodyuk 2012, таблица I). Параметры Пейна
    ограничены электронными измерениями по всей шкале — они и умолчание.
    Строка Breitenmoser оставлена как заготовка под будущий пофит по своим
    спектрам (вторая половина F11).

Тормозная способность S(E): NIST ESTAR для готовых соединений — CESIUM IODIDE
(matno=141) и SODIUM IODIDE (matno=252), с эффектом плотности и штатными I.
Сырые ответы сервера лежат в data/estar/ (получены 07.08.2026 POST-запросом
  curl --data "matno=141&ShowDefault=on&Energies=0.00100%0D%0A..."
       https://physics.nist.gov/cgi-bin/Star/e_table-t.pl
с добавкой сетки 1–9 кэВ; штатная сетка начинается с 10 кэВ). Они же
складываются в таблицу estar_collision_stopping — коллизионной тормозной
для СОЕДИНЕНИЙ в базе до сих пор не было (была только радиационная по
элементам). Ниже 1 кэВ интеграл доводится членом l(S(1 кэВ))·1 кэВ — перенос
электроны ниже 1 кэВ всё равно не различает.

Кривые LaBr3:Ce и CeBr3 не считаются: у NIST ESTAR нет этих соединений в
списке готовых, а мешать элементные тормозные аддитивностью Брэгга без
проверки не стал — параметры лежат в таблице, кривая появится вместе с
тормозной (строка в TODO).

    python import_light_yield.py <nucdb.sqlite>
"""
import math
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# S_Ons общий для всех строк (Payne II/III, Breitenmoser)
DEDX_ONS = 36.4

# material, source, is_default, T °C, η, S_Trap, S_Birks, примечание
PARAMS = [
    ("CsI:Tl", "payne2014_eta_fit_photon", 1, 20.0, 0.375, 24.5, 251.5,
     "Trap/Birks из Payne III, eta по фотонной 1.12 на 10 кэВ (Khodyuk т.I)"),
    ("CsI:Tl", "payne2014_tableI_20C", 0, 20.0, 0.438, 24.5, 251.5,
     "интерполяция колонок 0/+40 C: eta 43/44.6 %, Trap 21/28, Birks 218/285"),
    ("CsI:Na", "payne2014_eta_fit_photon", 1, 20.0, 0.398, 21.5, 255.0,
     "Trap/Birks из Payne III, eta сжат фактором калибровки CsI:Tl (0.856)"),
    ("CsI:Na", "payne2014_tableI_20C", 0, 20.0, 0.465, 21.5, 255.0,
     "интерполяция колонок 0/+40 C: eta 46/47 %, Trap 18/25, Birks 235/275"),
    ("NaI:Tl", "payne2014_eta_fit_photon", 1, 20.0, 0.33, 28.25, 415.0,
     "Trap/Birks из Payne III, eta по фотонной 1.14 на 10 кэВ (Khodyuk т.I)"),
    ("NaI:Tl", "payne2014_tableI_20C", 0, 20.0, 0.3725, 28.25, 415.0,
     "интерполяция колонок 0/+40 C: eta 37/37.5 %, Trap 28/28.5, Birks 405/425"),
    ("NaI:Tl", "breitenmoser2023_map_sum", 0, 20.0, 0.596, 14.6, 322.0,
     "MAP по комптон-краю, sum mode; ниже ~100 кэВ экстраполяция, кривой не давать"),
    ("LaBr3:Ce", "payne2014_tableII_20C", 1, 20.0, 0.18, 0.0, 465.0,
     "таблица II, интерполяция 0/+40 C: Birks 440/490, Trap 0, eta 18 %"),
    ("CeBr3", "payne2014_tableII_20C", 1, 20.0, 0.345, 20.0, 132.5,
     "таблица II, интерполяция 0/+40 C: Birks 128/137, Trap 20/20, eta 34.5 %"),
]

# материал кривой -> (файл ESTAR, star_materials.id)
CURVES = {
    "CsI:Tl": ("estar_141_csi.txt", 141),
    "CsI:Na": ("estar_141_csi.txt", 141),
    "NaI:Tl": ("estar_252_nai.txt", 252),
}

NORM_KEV = 661.657


# ---------------------------------------------------------------- ESTAR

def parse_estar(path):
    """Текстовый ответ e_table-t.pl: заголовок Z/A · плотность · I, дальше
    строки «T CLOSS RLOSS TLOSS DELTA» (МэВ, МэВ·см²/г)."""
    with open(path, encoding="latin-1") as f:
        text = f.read().replace("<br>", "\n")
    text = re.sub(r"<[^>]+>", "", text)
    lines = text.splitlines()
    za = density = ionization = None
    rows = []
    for i, line in enumerate(lines):
        if "Z/A" in line and "Density" in line:
            vals = lines[i + 1].split()
            za, density, ionization = (float(v) for v in vals[:3])
        parts = line.split()
        if len(parts) == 5:
            try:
                vals = [float(p) for p in parts]
            except ValueError:
                continue
            rows.append(vals)
    if density is None or not rows:
        raise SystemExit("не разобран %s" % path)
    energies = [r[0] for r in rows]
    if any(b <= a for a, b in zip(energies, energies[1:])):
        raise SystemExit("сетка не монотонна: %s" % path)
    return za, density, ionization, rows


def interp_loglog(xs, ys, x):
    """Лог-лог интерполяция по узлам (xs возрастают, ys > 0)."""
    if x <= xs[0]:
        return ys[0]
    if x >= xs[-1]:
        return ys[-1]
    lo, hi = 0, len(xs) - 1
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if xs[mid] <= x:
            lo = mid
        else:
            hi = mid
    t = (math.log(x) - math.log(xs[lo])) / (math.log(xs[hi]) - math.log(xs[lo]))
    return math.exp(math.log(ys[lo]) + t * (math.log(ys[hi]) - math.log(ys[lo])))


# ---------------------------------------------------------------- модель

def local_yield(s_mev_cm, eta, trap, birks):
    inner = (s_mev_cm / DEDX_ONS) * math.exp(-trap / s_mev_cm)
    return (1.0 - eta * math.exp(-inner)) / (1.0 + s_mev_cm / birks)


def build_curve(estar_rows, density, eta, trap, birks):
    """Кривая (энергия кэВ, относительный выход) на лог-сетке 1 кэВ – 3 МэВ.

    Интеграл ур. S30 — трапециями по плотной сетке (500 точек на декаду);
    отрезок ниже 1 кэВ приближён постоянным l(S(1 кэВ))."""
    e_kev = [r[0] * 1000.0 for r in estar_rows]
    s_cm = [r[1] * density for r in estar_rows]

    def s_of(e):
        return interp_loglog(e_kev, s_cm, e)

    e_lo, e_hi = 1.0, 3000.0
    steps = int(500 * math.log10(e_hi / e_lo))
    grid = [e_lo * (e_hi / e_lo) ** (i / float(steps)) for i in range(steps + 1)]
    light = [local_yield(s_of(e_lo), eta, trap, birks) * e_lo]
    for a, b in zip(grid, grid[1:]):
        la = local_yield(s_of(a), eta, trap, birks)
        lb = local_yield(s_of(b), eta, trap, birks)
        light.append(light[-1] + 0.5 * (la + lb) * (b - a))

    def rel(e):
        return interp_loglog(grid, light, e) / e

    norm = rel(NORM_KEV)
    points_per_decade = 20
    n = int(points_per_decade * math.log10(e_hi / e_lo)) + 1
    out = []
    for i in range(n):
        e = e_lo * (e_hi / e_lo) ** (i / float(n - 1))
        out.append((e, rel(e) / norm))
    return out


# ---------------------------------------------------------------- база

def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    db = sqlite3.connect(sys.argv[1])
    cur = db.cursor()

    cur.executescript("""
        drop table if exists scint_npsm_params;
        create table scint_npsm_params (
            material           text not null,
            source             text not null,
            is_default         integer not null,
            temperature_c      real not null,
            eta_eh             real not null,
            dedx_ons_mev_cm    real not null,
            dedx_trap_mev_cm   real not null,
            dedx_birks_mev_cm  real not null,
            note               text not null,
            primary key (material, source)
        ) without rowid;

        drop table if exists scint_electron_light_yield;
        create table scint_electron_light_yield (
            material    text not null,
            energy_kev  real not null,
            yield_rel   real not null,
            primary key (material, energy_kev)
        ) without rowid;

        drop table if exists estar_collision_stopping;
        create table estar_collision_stopping (
            material_star_id     integer not null,
            energy_mev           real not null,
            collision_mev_cm2_g  real not null,
            radiative_mev_cm2_g  real not null,
            delta                real not null,
            primary key (material_star_id, energy_mev)
        ) without rowid;
    """)

    for mat, source, default, temp, eta, trap, birks, note in PARAMS:
        cur.execute(
            "insert into scint_npsm_params values (?,?,?,?,?,?,?,?,?)",
            (mat, source, default, temp, eta, DEDX_ONS, trap, birks, note))

    stored_star = set()
    for mat, (fname, star_id) in sorted(CURVES.items()):
        za, density, ionization, rows = parse_estar(
            os.path.join(HERE, "data", "estar", fname))
        if star_id not in stored_star:
            stored_star.add(star_id)
            cur.executemany(
                "insert into estar_collision_stopping values (?,?,?,?,?)",
                [(star_id, r[0], r[1], r[2], r[4]) for r in rows])
            # плотность и I должны сойтись с уже лежащими в star_materials
            db_row = cur.execute(
                "select density_g_cm3, potential_ev from star_materials where id=?",
                (star_id,)).fetchone()
            if db_row and (abs(db_row[0] - density) > 1e-6
                           or abs(db_row[1] - ionization) > 0.05):
                raise SystemExit("ESTAR разошёлся со star_materials: id %d" % star_id)

        eta, trap, birks = next(
            (p[4], p[5], p[6]) for p in PARAMS if p[0] == mat and p[2] == 1)
        curve = build_curve(rows, density, eta, trap, birks)
        cur.executemany(
            "insert into scint_electron_light_yield values (?,?,?)",
            [(mat, e, y) for e, y in curve])

        peak = max(curve, key=lambda p: p[1])
        at = lambda e: interp_loglog([p[0] for p in curve],
                                     [p[1] for p in curve], e)
        print("%-8s горб %.3f на %.0f кэВ; 3 кэВ %.3f, 10 кэВ %.3f, "
              "60 кэВ %.3f, 100 кэВ %.3f, 3 МэВ %.3f"
              % (mat, peak[1], peak[0], at(3), at(10), at(60), at(100), at(3000)))
        if not (1.0 < peak[1] < 1.6 and 3.0 <= peak[0] <= 300.0):
            raise SystemExit("кривая %s не похожа на щёлочно-галоидную" % mat)

    db.commit()
    for row in cur.execute(
            "select material, count(*), min(energy_kev), max(energy_kev) "
            "from scint_electron_light_yield group by material"):
        print("в базе:", row)
    db.close()


if __name__ == "__main__":
    main()
