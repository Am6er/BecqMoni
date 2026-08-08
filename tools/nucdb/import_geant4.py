# -*- coding: utf-8 -*-
"""
Втягивает в matdb.sqlite данные из поставки Geant4 G4EMLOW (проверено на 8.8).

Что берём и зачем (нумерация дыр — database/scheme.md, §9а):

  fluor/binding.dat          → eadl_binding        энергии связи ВСЕХ подоболочек
                               H 13.6 эВ … Fm, включая края ниже 1 кэВ (A-3)
  fluor/fl-tr-pr-Z.dat       → eadl_radiative      радиационные переходы EADL:
                               вероятности и энергии линий; сумма по вакансии
                               K = ω_K, по L1..L3 = ω_L1..ω_L3 (A-4)
  auger/au-tr-pr-Z.dat       → eadl_auger          оже- и Костера–Кронига переходы:
                               выходы и энергии электронов (A-5)
  epics2017/phot/pe-ss-cs-Z  → epics_photo_subshell пооболочечный фотоэффект,
                               табличная часть у краёв (A-2)
  epics2017/phot/pe-high/low → epics_photo_fit     6-параметрические фиты фотоэффекта
                               выше табличной части; строки КУМУЛЯТИВНЫ (A-2)
  livermore/rayl/re-ff-Z.dat → epdl_form_factor    атомный форм-фактор F(x,Z) (A-1)
  comp/ce-sf-Z.dat           → epdl_scattering_function  функция инкогерентного
                               рассеяния S(x,Z) (A-1)
  doppler/*                  → compton_profile*    профили Комптона Биггса по
                               оболочкам — доплеровское размытие края (A-1)
  brem_SB/brZ                → seltzer_berger*     дифференциальные сечения
                               тормозного Зельцера–Бергера (B-3)

Форматы прочитаны не на глаз, а из читателей самого Geant4
(geant4-v11.4.2/source/processes/electromagnetic/lowenergy/src):

  * G4LivermorePhotoElectricModel.cc::ReadData / ComputeCrossSectionPerAtom /
    SampleSecondaries — главные три факта о фотоэффекте:
      1) в табличных файлах (pe-cs, pe-le-cs, pe-ss-cs) хранится НЕ сечение,
         а σ·E³ в барн·МэВ³ — код делит на E³ (`cs = x3 * Value(energy)`).
         Проверка: иод у K-края 0.2697/0.033176³ = 7385 б против 7393 б XCOM;
      2) строки pe-high/pe-low — кумулятивные суммы фитов по оболочкам:
         выбор оболочки идёт обходом «пока cs < rand·total», и полное сечение
         берётся из ПОСЛЕДНЕЙ строки (idx = 7·n−5). Сечение отдельной оболочки
         i — разность строк i и i−1;
      3) формула фита: σ(E) = (1/E)·(a1 + a2/E + a3/E² + a4/E³ + a5/E⁴ + a6/E⁵),
         E в МэВ, aᵢ в барн·МэВⁱ, σ в барнах. Фит «low» действует от
         low_from_ev (K-край) до high_from_ev, «high» — выше high_from_ev.
         Ниже K-края — табличные pe-le-cs/pe-ss-cs.
  * G4LivermorePolarizedRayleighModel.cc::GenerateCosTheta — аргумент
    форм-фактора x = (E/hc)·sin(θ/2) в СМ⁻¹ (`xxfact = cm/(h_Planck·c_light)`);
    сетка EPDL доходит до 1e17 см⁻¹ = 1e9 Å⁻¹. У S(x,Z) аргумент тот же.
  * doppler/README — профили Биггса: 31 значение J(p) на оболочку, импульсная
    сетка p-biggs.dat в атомных единицах, заселённости и потенциалы оболочек —
    shell-doppler.dat.
  * brem_SB: формат Зельцера–Бергера, как в G4SeltzerBergerModel: заголовок
    «? nk ne», сетка κ = k/T (nk значений), сетка ln(E/МэВ) (ne значений),
    затем ne строк по nk значений χ(Z,E,κ) = (β²/Z²)·k·dσ/dk в МИЛЛИбарнах.

Нумерация оболочек. В eadl_* — обозначения EADL: 1=K, 3=L1, 5=L2, 6=L3,
9=M1, 10=M2, 11=M3, 13=M4, 14=M5, дальше N (17…22) и O (28…). Составные
обозначения (2=L, 7=M, 8=M12, 12=M45, …) в файлах поставки не встречаются.
Проверка на иоде: Kα1 = переход 6→1 с вероятностью 0.472 и энергией
28.61 кэВ — сошлось со справочником.
В epics_photo_* — ПОРЯДКОВЫЙ номер 0..n−1 по убыванию энергии связи (0=K,
1=L1, …), как в самих файлах pe-ss-cs (четвёртое поле заголовка блока) и в
порядке строк pe-high/pe-low. Сопоставлять с EADL — по энергии края.

После импорта скрипт ЗАМЕНЯЕТ xray_fluorescence.omega_k (аппроксимация
Bambynek 1972 из import_xcom_star.py) на суммы радиационных вероятностей EADL.
Поэтому порядок пересборки базы: сначала import_xcom_star.py, потом этот
скрипт — иначе ω_K останется аппроксимацией.

    python import_geant4.py <matdb.sqlite> <каталог G4EMLOW>
"""
import math
import os
import sqlite3
import sys

ZMAX = 100


# ---------------------------------------------------------------- утилиты

def tokens(path):
    """Все числовые поля файла одним потоком строк-токенов."""
    with open(path, encoding="latin-1") as f:
        return f.read().split()


def is_int(tok):
    try:
        return float(tok) == int(float(tok))
    except ValueError:
        return False


# ---------------------------------------------------------------- fluor/binding.dat

def import_binding(db, g4):
    """Блоки по Z в порядке возрастания, разделитель «-1 -1», конец «-2 -2».

    Поставка тянется до Z=104; берём 1..100, как во всей остальной базе
    (сетка XCOM и данные STAR кончаются на Z=100)."""
    rows = []
    z = 1
    toks = tokens(os.path.join(g4, "fluor", "binding.dat"))
    for i in range(0, len(toks), 2):
        a, b = toks[i], toks[i + 1]
        if a == "-2" or z > ZMAX:
            break
        if a == "-1":
            z += 1
            continue
        rows.append((z, int(float(a)), float(b) * 1e6))
    db.executemany("insert into eadl_binding values (?,?,?)", rows)
    nz = db.execute("select count(distinct z) from eadl_binding").fetchone()[0]
    print("eadl_binding: %d строк, %d элементов" % (len(rows), nz))


# ---------------------------------------------------------------- fluor, auger

def import_transitions(db, g4, sub, prefix, table, ncols):
    """fl-tr-pr-Z.dat (3 колонки) и au-tr-pr-Z.dat (4 колонки).

    Блок открывает строка из ncols ОДИНАКОВЫХ целых (номер оболочки с
    вакансией), закрывает строка из «-1». Дальше в строке:
      fluor: конечная оболочка, вероятность, энергия кванта (МэВ)
      auger: откуда электрон перехода, откуда вылетевший, вероятность,
             энергия электрона (МэВ)
    """
    rows = []
    for z in range(1, ZMAX + 1):
        path = os.path.join(g4, sub, "%s-%d.dat" % (prefix, z))
        if not os.path.exists(path):
            continue
        toks = tokens(path)
        if len(toks) % ncols:
            raise ValueError("%s: %d полей не делятся на %d" % (path, len(toks), ncols))
        vac = None
        for i in range(0, len(toks), ncols):
            f = toks[i:i + ncols]
            if f[0] == "-2":
                break
            if f[0] == "-1":
                vac = None
                continue
            if vac is None:
                if not all(is_int(x) and float(x) == float(f[0]) for x in f):
                    raise ValueError("%s: ожидался заголовок блока, найдено %r" % (path, f))
                vac = int(float(f[0]))
                continue
            if ncols == 3:
                rows.append((z, vac, int(float(f[0])), float(f[1]), float(f[2]) * 1e6))
            else:
                rows.append((z, vac, int(float(f[0])), int(float(f[1])),
                             float(f[2]), float(f[3]) * 1e6))
    db.executemany("insert into %s values (%s)" % (table, ",".join("?" * (ncols + 2))), rows)
    print("%s: %d строк" % (table, len(rows)))


# ---------------------------------------------------------------- pe-ss-cs

def import_photo_subshell(db, g4):
    """Блоки: «emin emax n shell», затем n пар (E МэВ, σ·E³ барн·МэВ³).

    В базу кладётся σ в барнах (= значение/E³). Повторную точку края
    разводим на 1e-4 эВ, как сделано для xcom_cross_sections, — иначе
    первичный ключ схлопнет две физически разные точки.
    """
    rows = []
    for z in range(1, ZMAX + 1):
        toks = tokens(os.path.join(g4, "epics2017", "phot", "pe-ss-cs-%d.dat" % z))
        i = 0
        seen = set()
        while i < len(toks):
            n, shell = int(float(toks[i + 2])), int(float(toks[i + 3]))
            i += 4
            for j in range(n):
                e_mev = float(toks[i])
                v = float(toks[i + 1])
                i += 2
                e_ev = e_mev * 1e6
                while (shell, e_ev) in seen:
                    e_ev += 1e-4
                seen.add((shell, e_ev))
                rows.append((z, shell, e_ev, v / e_mev ** 3))
    db.executemany("insert into epics_photo_subshell values (?,?,?,?)", rows)
    print("epics_photo_subshell: %d строк" % len(rows))


def import_photo_fits(db, g4):
    """pe-high-Z.dat и pe-low-Z.dat: «n1 n2 порог», затем n1 строк по 7 чисел
    (край МэВ, a1..a6). Строки кумулятивны (см. шапку)."""
    fit_rows = []
    meta = {}
    for kind in ("high", "low"):
        for z in range(1, ZMAX + 1):
            toks = tokens(os.path.join(g4, "epics2017", "phot", "pe-%s-%d.dat" % (kind, z)))
            n1, n2, thr = int(toks[0]), int(toks[1]), float(toks[2]) * 1e6
            if n1 != n2:
                raise ValueError("pe-%s-%d: n1=%d != n2=%d" % (kind, z, n1, n2))
            if len(toks) != 3 + 7 * n1:
                raise ValueError("pe-%s-%d: %d полей вместо %d" % (kind, z, len(toks), 3 + 7 * n1))
            meta.setdefault(z, [n1, None, None])[1 if kind == "high" else 2] = thr
            if meta[z][0] != n1:
                raise ValueError("pe-high/low-%d: разное число оболочек" % z)
            for s in range(n1):
                f = [float(x) for x in toks[3 + 7 * s:10 + 7 * s]]
                fit_rows.append((z, kind, s, f[0] * 1e6) + tuple(f[1:]))
    db.executemany("insert into epics_photo_fit values (?,?,?,?,?,?,?,?,?,?)", fit_rows)
    db.executemany("insert into epics_photo_meta values (?,?,?,?)",
                   [(z, m[0], m[1], m[2]) for z, m in sorted(meta.items())])
    print("epics_photo_fit: %d строк, epics_photo_meta: %d элементов" % (len(fit_rows), len(meta)))


# ---------------------------------------------------------------- FF и SF

def import_form_factor(db, g4):
    """re-ff-Z.dat: «xmin xmax n», затем n, затем n пар (x см⁻¹, F)."""
    rows = []
    for z in range(1, ZMAX + 1):
        toks = tokens(os.path.join(g4, "livermore", "rayl", "re-ff-%d.dat" % z))
        n = int(float(toks[2]))
        if int(float(toks[3])) != n or len(toks) != 4 + 2 * n:
            raise ValueError("re-ff-%d: сломан заголовок" % z)
        pairs = [(float(toks[4 + 2 * j]), float(toks[5 + 2 * j])) for j in range(n)]
        if abs(pairs[0][1] - z) > 1e-6:
            raise ValueError("re-ff-%d: F(0)=%r, а не Z" % (z, pairs[0][1]))
        rows += [(z, x, ff) for x, ff in pairs]
    db.executemany("insert into epdl_form_factor values (?,?,?)", rows)
    print("epdl_form_factor: %d строк" % len(rows))


def import_scattering_function(db, g4):
    """ce-sf-Z.dat: голые пары (x см⁻¹, S), конец — «-1 -1» и «-2 -2».
    S(x→∞) → Z."""
    rows = []
    for z in range(1, ZMAX + 1):
        toks = tokens(os.path.join(g4, "comp", "ce-sf-%d.dat" % z))
        pairs = [(float(toks[2 * j]), float(toks[2 * j + 1])) for j in range(len(toks) // 2)]
        pairs = [p for p in pairs if p[0] >= 0]
        if abs(pairs[-1][1] - z) > 0.05 * z:
            raise ValueError("ce-sf-%d: S(xmax)=%r далеко от Z" % (z, pairs[-1][1]))
        rows += [(z, x, sf) for x, sf in pairs]
    db.executemany("insert into epdl_scattering_function values (?,?,?)", rows)
    print("epdl_scattering_function: %d строк" % len(rows))


# ---------------------------------------------------------------- профили Комптона

def import_compton_profiles(db, g4):
    """profile-Z.dat: по 31 значению J(p) на оболочку (а.е.);
    p-biggs.dat: общая импульсная сетка из 31 точки (а.е.);
    shell-doppler.dat: строки «заселённость потенциал(МэВ)» по оболочкам,
    блоки по Z разделены «-1 -1», конец «-2 -2»; Z задан порядком блока.
    README поставки описывает формат со строкой «Z n» — в файле её НЕТ."""
    grid = [float(x) for x in tokens(os.path.join(g4, "doppler", "p-biggs.dat"))]
    if len(grid) != 31:
        raise ValueError("p-biggs.dat: %d точек вместо 31" % len(grid))
    db.executemany("insert into compton_profile_momentum values (?,?)",
                   list(enumerate(grid)))

    toks = tokens(os.path.join(g4, "doppler", "shell-doppler.dat"))
    shells = {}
    z = 1
    for i in range(0, len(toks), 2):
        if toks[i] == "-2" or z > ZMAX:
            break
        if toks[i] == "-1":
            z += 1
            continue
        shells.setdefault(z, []).append((float(toks[i]), float(toks[i + 1]) * 1e6))
    shell_rows = [(z, s, occ, pot) for z, lst in sorted(shells.items())
                  for s, (occ, pot) in enumerate(lst)]
    db.executemany("insert into compton_profile_shell values (?,?,?,?)", shell_rows)

    prof_rows = []
    for z in range(1, ZMAX + 1):
        vals = [float(x) for x in tokens(os.path.join(g4, "doppler", "profile-%d.dat" % z))]
        if len(vals) % 31:
            raise ValueError("profile-%d: %d значений не кратны 31" % (z, len(vals)))
        nsh = len(vals) // 31
        if z in shells and nsh != len(shells[z]):
            raise ValueError("profile-%d: %d оболочек против %d в shell-doppler"
                             % (z, nsh, len(shells[z])))
        for s in range(nsh):
            for p in range(31):
                prof_rows.append((z, s, p, vals[31 * s + p]))
    db.executemany("insert into compton_profile values (?,?,?,?)", prof_rows)
    print("compton_profile: %d строк, оболочек %d" % (len(prof_rows), len(shell_rows)))


# ---------------------------------------------------------------- Зельцер–Бергер

def import_seltzer_berger(db, g4):
    """brZ: «? nk ne», сетка κ (nk), сетка ln(E/МэВ) (ne), ne×nk значений χ, мб.

    Сетки у всех элементов одинаковы — проверяется и хранится один раз.
    Энергия в сетке хранится в эВ (exp(lnE)·1e6).

    Берём Z=1..92: у br93–br99 заголовок и сетки от грубого формата 14×31,
    а тело — 57 строк по 32 значения (штатный читатель G4Physics2DVector
    прочёл бы мусор), br100 целиком в грубой сетке. Элементы тяжелее урана
    в наших веществах не встречаются."""
    kappa = energy = None
    rows = []
    for z in range(1, 93):
        toks = tokens(os.path.join(g4, "brem_SB", "br%d" % z))
        nk, ne = int(toks[1]), int(toks[2])
        if len(toks) != 3 + nk + ne + nk * ne:
            raise ValueError("br%d: %d полей вместо %d" % (z, len(toks), 3 + nk + ne + nk * ne))
        k = [float(x) for x in toks[3:3 + nk]]
        e = [float(x) for x in toks[3 + nk:3 + nk + ne]]
        if kappa is None:
            kappa, energy = k, e
        elif k != kappa or e != energy:
            raise ValueError("br%d: сетка отличается от br1" % z)
        base = 3 + nk + ne
        for ei in range(ne):
            for ki in range(nk):
                rows.append((z, ei, ki, float(toks[base + ei * nk + ki])))
    db.executemany("insert into seltzer_berger_grid values (?,?,?)",
                   [("kappa", i, v) for i, v in enumerate(kappa)] +
                   [("energy", i, math.exp(v) * 1e6) for i, v in enumerate(energy)])
    db.executemany("insert into seltzer_berger values (?,?,?,?)", rows)
    print("seltzer_berger: %d строк (%d энергий × %d κ × 92 элемента)"
          % (len(rows), len(energy), len(kappa)))


# ---------------------------------------------------------------- ω_K → EADL

def replace_omega_k(db):
    """Меняет аппроксимацию Bambynek на сумму радиационных вероятностей EADL.

    Сумма вероятностей fl-tr-pr по вакансии K — это и есть выход
    флуоресценции ω_K (проверка: иод 0.886 при справочных 0.884)."""
    diffs = []
    for z, old in db.execute("select z, omega_k from xray_fluorescence"):
        new = db.execute("select sum(probability) from eadl_radiative"
                         " where z=? and vacancy_shell=1", (z,)).fetchone()[0]
        if new is None:
            raise ValueError("нет EADL-переходов для Z=%d" % z)
        diffs.append((z, old, new))
        db.execute("update xray_fluorescence set omega_k=? where z=?", (new, z))
    worst = max(diffs, key=lambda r: abs(r[1] - r[2]))
    print("xray_fluorescence.omega_k: заменено %d значений;"
          " худшее расхождение с Bambynek: Z=%d, %.4f → %.4f"
          % (len(diffs), worst[0], worst[1], worst[2]))


# ---------------------------------------------------------------- проверки

def verify(db):
    print("\n--- проверки ---")

    # 1. Энергии связи K против краёв XCOM (два независимых источника).
    r = db.execute("""
        select count(*), max(abs(b.binding_ev - e.energy_ev) / e.energy_ev)
        from eadl_binding b join xcom_edges e on e.z = b.z and e.shell = 'K'
        where b.shell_id = 1""").fetchone()
    print("K-связь EADL против K-краёв XCOM: %d элементов, худшее отн. расхождение %.3f%%"
          % (r[0], 100 * r[1]))

    # 2. Энергии Kα1 (переход 6 = L3→K) против xray_fluorescence.
    print("Kα1: EADL против xray_fluorescence (разности краёв XCOM):")
    for z, name in ((32, "Ge"), (53, "I"), (55, "Cs"), (64, "Gd"), (83, "Bi")):
        r = db.execute("""
            select t.energy_ev, x.ka1_ev from eadl_radiative t
            join xray_fluorescence x on x.z = t.z
            where t.z=? and t.vacancy_shell=1 and t.from_shell=6""", (z,)).fetchone()
        print("  %-2s: %8.1f эВ против %8.1f (%+.2f%%)"
              % (name, r[0], r[1], 100 * (r[0] - r[1]) / r[1]))

    # 3. Полнота распада вакансии: ω + оже = 1.
    r = db.execute("""
        with f as (select z, vacancy_shell v, sum(probability) p
                   from eadl_radiative group by z, vacancy_shell),
             a as (select z, vacancy_shell v, sum(probability) p
                   from eadl_auger group by z, vacancy_shell)
        select max(abs(1 - coalesce(f.p,0) - coalesce(a.p,0)))
        from a left join f on f.z = a.z and f.v = a.v""").fetchone()
    print("ω + оже = 1: худшее отклонение %.2e" % r[0])

    # 4. Кумулятивный фит полного сечения против XCOM-фотоэффекта на 1 МэВ.
    #    Фит действует только выше high_from_ev/low_from_ev (у Pb — от 507 кэВ);
    #    ниже G4 берёт табличные векторы, и подставлять туда фит нельзя.
    print("полное σ фотоэффекта из последней строки фита против XCOM, 1 МэВ:")
    for z, name in ((26, "Fe"), (53, "I"), (82, "Pb")):
        n, high_from = db.execute(
            "select n_shells, high_from_ev from epics_photo_meta where z=?", (z,)).fetchone()
        if high_from > 1e6:
            raise ValueError("Z=%d: high_from=%g выше 1 МэВ, проверка не судит" % (z, high_from))
        a = db.execute("select a1_b,a2_b,a3_b,a4_b,a5_b,a6_b from epics_photo_fit"
                       " where z=? and kind='high' and shell_seq=?", (z, n - 1)).fetchone()
        sigma = sum(a[i] / 1.0 ** (i + 1) for i in range(6))  # E = 1 МэВ
        xcom = db.execute("select photoelectric_b from xcom_cross_sections"
                          " where z=? and energy_ev=1e6", (z,)).fetchone()[0]
        print("  %-2s: %9.4f б против %9.4f (%+.2f%%)"
              % (name, sigma, xcom, 100 * (sigma - xcom) / xcom))

    # 5. Доля K-оболочки у K-края: первая точка таблицы pe-ss против
    #    надкраевого XCOM (из двух точек края верхняя — с большим σ).
    print("σ_K/σ_tot сразу над K-краем против k_fraction (скачок XCOM):")
    for z, name in ((53, "I"), (82, "Pb")):
        cs_k = db.execute("""
            select cs_b from epics_photo_subshell
            where z=? and shell_seq=0 order by energy_ev limit 1""", (z,)).fetchone()[0]
        tot = db.execute("""
            select max(c.photoelectric_b) from xcom_cross_sections c
            join xcom_edges e on e.z = c.z and e.shell = 'K'
            where c.z=? and abs(c.energy_ev - e.energy_ev) < 1.0""", (z,)).fetchone()[0]
        frac = db.execute("select k_fraction from xray_fluorescence where z=?", (z,)).fetchone()[0]
        print("  %-2s: %.4f против %.4f" % (name, cs_k / tot, frac))

    # 6. Заселённости Комптона: сумма по оболочкам = Z.
    r = db.execute("""select max(abs(z - s)) from
        (select z, sum(occupancy) s from compton_profile_shell group by z)""").fetchone()
    print("Σ заселённостей оболочек = Z: худшее отклонение %.2e" % r[0])

    # 7. Зельцер–Бергер против радиационной тормозной ESTAR на 1 МэВ:
    #    S_rad/ρ = (N_A/A)·(Z²/β²)·T·∫χ dκ · 1e-27  [МэВ·см²/г]
    print("∫χdκ Зельцера–Бергера против estar_radiative_stopping, 1 МэВ:")
    kappa = [r[0] for r in db.execute(
        "select value from seltzer_berger_grid where kind='kappa' order by idx")]
    e_idx = db.execute("select idx from seltzer_berger_grid where kind='energy'"
                       " and abs(value - 1e6) < 1" ).fetchone()[0]
    t = 1.0  # МэВ
    gamma = 1 + t / 0.510998950
    beta2 = 1 - 1 / gamma ** 2
    for z, name in ((13, "Al"), (29, "Cu"), (82, "Pb")):
        chi = [r[0] for r in db.execute(
            "select chi_mb from seltzer_berger where z=? and e_idx=? order by kappa_idx",
            (z, e_idx))]
        integral = sum((chi[i] + chi[i + 1]) / 2 * (kappa[i + 1] - kappa[i])
                       for i in range(len(kappa) - 1))
        aw = db.execute("select atomic_weight from xcom_elements where z=?", (z,)).fetchone()[0]
        srad = 0.6022140857 / aw * z * z / beta2 * t * integral * 1e-3
        est = db.execute("""select stopping_mev_cm2_g from estar_radiative_stopping
            where z=? and abs(energy_mev - 1.0) < 1e-6""", (z,)).fetchone()
        est = est[0] if est else None
        print("  %-2s: %7.4f МэВ·см²/г против %s (ESTAR)"
              % (name, srad, "%.4f (%+.1f%%)" % (est, 100 * (srad - est) / est) if est else "—"))


# ---------------------------------------------------------------- main

DDL = """
create table eadl_binding (
    z         integer not null,
    shell_id  integer not null,   -- обозначение EADL: 1=K, 3=L1, 5=L2, 6=L3, 9=M1…
    binding_ev real   not null,
    primary key (z, shell_id)
) without rowid;

create table eadl_radiative (
    z             integer not null,
    vacancy_shell integer not null,  -- где дырка (обозначение EADL)
    from_shell    integer not null,  -- откуда пришёл электрон
    probability   real    not null,  -- на одну вакансию; Σ по вакансии = ω
    energy_ev     real    not null,  -- энергия кванта
    primary key (z, vacancy_shell, from_shell)
) without rowid;

create table eadl_auger (
    z             integer not null,
    vacancy_shell integer not null,
    from_shell    integer not null,  -- электрон, закрывший дырку
    ejected_shell integer not null,  -- вылетевший электрон
    probability   real    not null,
    energy_ev     real    not null,  -- энергия вылетевшего электрона
    primary key (z, vacancy_shell, from_shell, ejected_shell)
) without rowid;

create table epics_photo_subshell (
    z         integer not null,
    shell_seq integer not null,  -- порядковый: 0=K, 1=L1, … по убыванию связи
    energy_ev real    not null,
    cs_b      real    not null,  -- σ оболочки, барн (в файле σ·E³ — уже поделено)
    primary key (z, shell_seq, energy_ev)
) without rowid;

create table epics_photo_fit (
    z         integer not null,
    kind      text    not null,  -- 'high' | 'low'
    shell_seq integer not null,
    edge_ev   real    not null,
    a1_b      real    not null,  -- σ(E) = Σ aᵢ/Eⁱ, E в МэВ, σ в барнах;
    a2_b      real    not null,  -- строки КУМУЛЯТИВНЫ: строка i = оболочки 0..i,
    a3_b      real    not null,  -- последняя = полное сечение; σ оболочки i =
    a4_b      real    not null,  -- строка i минус строка i−1
    a5_b      real    not null,
    a6_b      real    not null,
    primary key (z, kind, shell_seq)
) without rowid;

create table epics_photo_meta (
    z            integer primary key,
    n_shells     integer not null,
    high_from_ev real    not null,  -- фит high действует от этой энергии
    low_from_ev  real    not null   -- фит low — отсюда до high_from_ev (K-край)
) without rowid;

create table epdl_form_factor (
    z        integer not null,
    x_percm  real    not null,  -- x = (E/hc)·sin(θ/2), см⁻¹
    ff       real    not null,  -- F(x,Z), электронов; F(0)=Z
    primary key (z, x_percm)
) without rowid;

create table epdl_scattering_function (
    z        integer not null,
    x_percm  real    not null,
    sf       real    not null,  -- S(x,Z), электронов; S(∞)=Z
    primary key (z, x_percm)
) without rowid;

create table compton_profile_momentum (
    p_idx integer primary key,
    p_au  real not null          -- импульс, атомные единицы
) without rowid;

create table compton_profile_shell (
    z            integer not null,
    shell_seq    integer not null,  -- порядковый, как в profile-Z.dat
    occupancy    real    not null,
    potential_ev real    not null,
    primary key (z, shell_seq)
) without rowid;

create table compton_profile (
    z         integer not null,
    shell_seq integer not null,
    p_idx     integer not null,
    j_au      real    not null,  -- профиль J(p), атомные единицы
    primary key (z, shell_seq, p_idx)
) without rowid;

create table seltzer_berger_grid (
    kind  text    not null,      -- 'kappa' (k/T, доля) | 'energy' (эВ)
    idx   integer not null,
    value real    not null,
    primary key (kind, idx)
) without rowid;

create table seltzer_berger (
    z         integer not null,
    e_idx     integer not null,  -- индекс в сетке 'energy'
    kappa_idx integer not null,  -- индекс в сетке 'kappa'
    chi_mb    real    not null,  -- χ = (β²/Z²)·k·dσ/dk, миллибарн
    primary key (z, e_idx, kappa_idx)
) without rowid;
"""

TABLES = ["eadl_binding", "eadl_radiative", "eadl_auger",
          "epics_photo_subshell", "epics_photo_fit", "epics_photo_meta",
          "epdl_form_factor", "epdl_scattering_function",
          "compton_profile_momentum", "compton_profile_shell", "compton_profile",
          "seltzer_berger_grid", "seltzer_berger"]


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    dbpath, g4 = sys.argv[1], sys.argv[2]
    db = sqlite3.connect(dbpath)
    for t in TABLES:
        db.execute("drop table if exists " + t)
    db.executescript(DDL)

    import_binding(db, g4)
    import_transitions(db, g4, "fluor", "fl-tr-pr", "eadl_radiative", 3)
    import_transitions(db, g4, "auger", "au-tr-pr", "eadl_auger", 4)
    import_photo_subshell(db, g4)
    import_photo_fits(db, g4)
    import_form_factor(db, g4)
    import_scattering_function(db, g4)
    import_compton_profiles(db, g4)
    import_seltzer_berger(db, g4)
    replace_omega_k(db)
    verify(db)

    db.commit()
    db.execute("vacuum")
    db.close()
    size = os.path.getsize(dbpath) / 1024.0 / 1024.0
    print("\nГотово. Размер базы: %.1f МБ" % size)


if __name__ == "__main__":
    main()
