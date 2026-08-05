# -*- coding: utf-8 -*-
"""
Втягивает в nucdb.sqlite данные о ВЕЩЕСТВЕ из двух поставок NIST:

  XCOM 3.1 (Berger, Hubbell, Seltzer, 1999) — сечения взаимодействия фотона
    по каналам для Z = 1..100, файлы MDATX3.xxx;
  ESTAR/PSTAR/ASTAR (Berger, 1999) — составы веществ, файл FCOMP.

Зачем в базу. Те же числа уже лежат в исходнике программы
(`EfficiencyMaker/AttenuationData.cs`, `PartialCrossSections.cs`), но выборкой:
92 элемента полного ослабления и 9 элементов парциальных сечений, снятых
руками через веб-форму. Из-за этого четыре кристалла библиотеки (CeBr3, CdTe,
CZT, GSO) считаются грубым приближением «фотоэффект = всё, что не комптон»,
которое завышает канал поглощения в полтора раза.

Формат MDATX3 взят не на глаз, а из `XCOM.FOR` (строки 144-171):

    I6,F12.6        Z, атомный вес
    12I6            число краёв поглощения, число энергий
    12I6            индексы краёв в сетке
    14(1X,A2)       метки оболочек
    8F9.1           энергии краёв
                    (все три записи в одном порядке — по убыванию энергии)
    1P6E13.5        сетка энергий, эВ
    1P8E10.3        когерентное      \
    1P8E10.3        некогерентное     |
    1P8E10.3        фотоэффект        > барн/атом
    1P8E10.3        пары в поле ядра  |
    1P8E10.3        пары в поле эл.  /
    далее (если есть края) пооболочечный фотоэффект

Формат FCOMP — из `CONVERT.f` того же комплекта:

    A72             имя вещества
    *               число элементов, <Z/A>, средняя энергия возбуждения, эВ,
                    плотность, г/см3
    6(I3,F9.6)      пары (Z, массовая доля)

Ещё две таблицы читаются из ИСХОДНИКА ESTAR, а не из его файлов данных:
средние энергии возбуждения элементов лежат DATA-блоками POTH/POTGAS/POTCON в
`ESTAR.f`. Без них I считается только для 279 готовых составов FCOMP, а для
произвольной формулы (CeBr3, SrI2, CZT, GSO — их в FCOMP нет) взять неоткуда.

Коэффициенты внутренней конверсии — из поставки ЛСРМ (`TCCFCALC/LIB/ICC`),
таблицы Rösel и др.: Z, оболочка, энергия перехода, коэффициент для E1..E4 и
M1..M4.

Всё, что лежит рядом с FCOMP, ищется само: `FEDAT`, `MATS`, `material.txt`,
`ESTAR.f`. Каталоги ASTAR и PSTAR — на уровень выше рядом с каталогом ESTAR;
из них берутся FALPH и FPROT (тормозная способность, пробег и извилистость для
альфа-частиц и протонов, 74 вещества).

    python import_xcom_star.py <nucdb.sqlite> <каталог XCOM> <FCOMP> [каталог ICC]
"""
import io
import os
import sqlite3
import sys


def read_mdatx3(path):
    """Разобрать один файл MDATX3.xxx. Возвращает словарь с полями."""
    with io.open(path, encoding="latin-1") as f:
        lines = f.read().split("\n")

    z = int(lines[0][:6])
    atomic_weight = float(lines[0][6:18])
    max_edge = int(lines[1][:6])
    max_e = int(lines[1][6:12])

    at = 2
    edges = []

    def take(count):
        """Снять count полей, сколько бы строк они ни занимали.

        Формат фиксированный (12I6, 14(1X,A2), 8F9.1), но у тяжёлых элементов
        краёв больше, чем влезает в строку, и запись переносится. Считать по
        колонкам одной строки — ровно та ошибка, на которой первый заход и
        встал: у урана 24 края, а в строке их помещается 12.
        """
        out = []
        nonlocal_at = at
        while len(out) < count:
            out += lines[nonlocal_at].split("\x1a")[0].split()
            nonlocal_at += 1
        return out[:count], nonlocal_at

    if max_edge > 0:
        raw, at = take(max_edge)
        idx = [int(x) for x in raw]
        raw, at = take(max_edge)
        labels = raw
        raw, at = take(max_edge)
        energies = [float(x) for x in raw]
        # Все три записи идут В ОДНОМ порядке — по убыванию энергии, K первым:
        # у йода ` K  L1 L2 L3 M1` против `33169.4 5188.1 4852.1 4557.1 1072.1`
        # и индексов сетки `22 14 11 9 3`. Первый заход разворачивал индексы и
        # энергии, а метки оставлял как есть — от этого K уезжал на самый
        # нижний край, а название последней оболочки на K-край. Энергия с
        # индексом при этом оставались согласованы, и по числам ошибка не
        # видна: она видна только по имени оболочки.
        edges = list(zip(labels, energies, idx))

    # дальше всё читается потоком чисел: разбиение по строкам фиксированное,
    # но значения разделены пробелами, и поток надёжнее колонок
    stream = []
    for line in lines[at:]:
        # файлы 1988 года кончаются символом конца текста DOS (0x1A)
        line = line.split("\x1a")[0]
        stream += [float(x) for x in line.split()]

    grid = stream[:max_e]
    rest = stream[max_e:]
    channels = {}
    names = ["coherent", "incoherent", "photoelectric", "pair_nuclear", "pair_electron"]
    for i, name in enumerate(names):
        channels[name] = rest[i * max_e:(i + 1) * max_e]
        if len(channels[name]) != max_e:
            raise ValueError("%s: канал %s оборван (%d из %d)"
                             % (path, name, len(channels[name]), max_e))

    return {
        "z": z,
        "atomic_weight": atomic_weight,
        "grid": grid,
        "channels": channels,
        "edges": edges,
    }


def read_fedat(path):
    """Разобрать FEDAT: оболочки и тормозная способность для Z = 1..100.

    Формат — из `EDCONV.f` того же комплекта:

        *   NMAX (оболочек), LKMAX (энергий, всегда 113)
        *   NC(1..NMAX)    заселённости оболочек
        *   BD(1..NMAX)    энергии связи, эВ
        *   RLOS(1..LKMAX) радиационная тормозная способность, МэВ·см2/г

    Сетка энергий в файле не лежит — она вшита в саму программу
    (`ESTAR.f`, DATA ER, 113 точек от 1 кэВ до 10 ГэВ) и повторена ниже.
    """
    lines = io.open(path, encoding="latin-1").read().split("\n")
    out = {}
    i = 0
    for z in range(1, 101):
        n_shell, n_energy = [int(x) for x in lines[i].split()]
        i += 1

        def take(count, cast):
            got = []
            nonlocal i
            while len(got) < count:
                got += [cast(float(x)) for x in lines[i].split()]
                i += 1
            return got[:count]

        occupation = take(n_shell, int)
        binding = take(n_shell, float)
        stopping = take(n_energy, float)
        out[z] = (occupation, binding, stopping)

    return out


# Сетка ESTAR, МэВ: `ESTAR.f`, DATA ER — 113 точек, 1 кэВ … 10 ГэВ.
ESTAR_GRID = []
for _decade in range(-3, 4):
    for _m in (1.00, 1.25, 1.50, 1.75, 2.00, 2.50, 3.00, 3.50, 4.00,
               4.50, 5.00, 5.50, 6.00, 7.00, 8.00, 9.00):
        ESTAR_GRID.append(float("%.6g" % (_m * 10.0 ** _decade)))
ESTAR_GRID.append(1.0e4)


def read_fcomp(path):
    """Разобрать FCOMP: список веществ с составом."""
    with io.open(path, encoding="latin-1") as f:
        lines = [l.rstrip("\r") for l in f.read().split("\n")]

    materials = []
    i = 0
    while i + 1 < len(lines):
        name = lines[i].strip()
        if not name:
            break
        head = lines[i + 1].split()
        if len(head) < 4:
            break
        n_el = int(head[0])
        z_over_a = float(head[1])
        potential = float(head[2])
        density = float(head[3])
        i += 2

        # пары (Z, доля) по шесть в строке, формат 6(I3,F9.6)
        comp = []
        while len(comp) < n_el:
            line = lines[i]
            i += 1
            pos = 0
            while pos + 12 <= len(line) and len(comp) < n_el:
                zz = line[pos:pos + 3].strip()
                wt = line[pos + 3:pos + 12].strip()
                if zz:
                    comp.append((int(zz), float(wt)))
                pos += 12

        materials.append({
            "name": name,
            "z_over_a": z_over_a,
            "potential_ev": potential,
            "density": density,
            "composition": comp,
        })

    return materials


def read_fortran_data(path, name):
    """Снять список чисел из DATA-блока фортрановского исходника.

    Форма записи фиксированная: колонки 7-72, перенос помечен знаком в
    колонке 6, список кончается косой чертой. Читать построчно целиком нельзя —
    в колонках 1-5 стоят метки операторов, и они попадут в числа.
    """
    with io.open(path, encoding="latin-1") as f:
        lines = f.read().split("\n")

    head = "DATA " + name + "/"
    for i, line in enumerate(lines):
        body = line[6:72]
        if head not in body:
            continue
        text = body.split(head, 1)[1]
        j = i + 1
        while "/" not in text:
            text += lines[j][6:72]
            j += 1
        return [float(x) for x in text.split("/")[0].replace(" ", "").split(",") if x]
    raise ValueError("%s: не нашёлся DATA %s" % (path, name))


def read_estar_potentials(path):
    """Снять DATA-блоки POTH / POTGAS / POTCON из исходника ESTAR.f.

    Средняя энергия возбуждения элемента, эВ. POTH — конденсированная фаза (ею
    ESTAR пользуется по умолчанию), POTGAS и POTCON — газовая и проводящая
    формы для первых девяти элементов, ESTAR подставляет их, когда элемент
    входит в вещество в таком виде (`ESTAR.f`, выбор по знаку заселённости
    последней оболочки).
    """
    poth = read_fortran_data(path, "POTH")
    potgas = read_fortran_data(path, "POTGAS")
    potcon = read_fortran_data(path, "POTCON")
    out = {}
    for i, value in enumerate(poth, 1):
        gas = potgas[i - 1] if i <= len(potgas) else None
        con = potcon[i - 1] if i <= len(potcon) else None
        out[i] = (value, gas, con)
    return out


def read_star_ids(mats_path, material_txt_path, fcomp_names):
    """Сопоставить веществам FCOMP их НОМЕР в поставке STAR (001..278 и 906).

    Номер — это то, чем вещество зовут и `material.txt`, и списки IDNO в
    ASTAR.f/PSTAR.f, и им же его выбирают в самих программах. Порядковый номер
    строки FCOMP им НЕ равен: `MATS` и `material.txt` идут строка в строку, а
    FCOMP — тот же список, но графит вынесен из шестой позиции в конец. Отсюда
    сдвиг на единицу у всех веществ после углерода, из-за которого имя вещества
    разъезжается с номером ровно на одну строку.
    """
    with io.open(mats_path, encoding="latin-1") as f:
        mats = [line.rstrip() for line in f if line.strip()]
    ids = []
    with io.open(material_txt_path, encoding="latin-1") as f:
        for line in f:
            if len(line) > 4 and line[:3].isdigit() and line[3] == ":":
                ids.append(int(line[:3]))
    if not (len(mats) == len(ids) == len(fcomp_names)):
        raise ValueError("MATS %d, material.txt %d, FCOMP %d — списки разной длины"
                         % (len(mats), len(ids), len(fcomp_names)))

    graphite = 6                       # позиция графита в MATS, 0-based
    out = []
    for k in range(len(fcomp_names)):
        if k < graphite:
            m = k
        elif k == len(fcomp_names) - 1:
            m = graphite               # графит, вынесенный в конец FCOMP
        else:
            m = k + 1
        out.append((ids[m], mats[m]))
    return out


def read_star_stopping(data_path, source_path, n_energies, n_materials=74):
    """Разобрать FALPH (ASTAR) или FPROT (PSTAR).

    Формат — из `ACONV.f`/`PCONV.f`: на вещество четыре массива подряд по
    n_energies чисел — электронная тормозная, ядерная тормозная, пробег CSDA и
    коэффициент извилистости (отношение проекции пробега к самому пробегу).
    Единицы поставки: МэВ·см²/г, г/см², безразмерный.

    Сетка энергий и номера веществ в исходнике программы, DATA E и DATA IDNO;
    в самом файле данных нет ни того, ни другого, и порядок записей — это
    единственное, чем вещество опознаётся.
    """
    energies = read_fortran_data(source_path, "E")
    ids = [int(x) for x in read_fortran_data(source_path, "IDNO")]
    if len(energies) != n_energies:
        raise ValueError("%s: сетка на %d точек, ждали %d"
                         % (source_path, len(energies), n_energies))
    if len(ids) != n_materials:
        raise ValueError("%s: номеров веществ %d, ждали %d"
                         % (source_path, len(ids), n_materials))

    with io.open(data_path, encoding="latin-1") as f:
        stream = [float(x) for x in f.read().split()]
    need = n_materials * 4 * n_energies
    if len(stream) != need:
        raise ValueError("%s: чисел %d, а надо %d" % (data_path, len(stream), need))

    rows = []
    at = 0
    for j in range(n_materials):
        block = [stream[at + i * n_energies:at + (i + 1) * n_energies] for i in range(4)]
        at += 4 * n_energies
        for k in range(n_energies):
            rows.append((ids[j], energies[k],
                         block[0][k], block[1][k], block[2][k], block[3][k]))
    return rows


def build_fluorescence(db):
    """Собрать таблицу K-флуоресценции: чем отвечает атом на дырку в K-оболочке.

    Нужна, чтобы считать вылет характеристического рентгена: выше K-края квант
    выбивает электрон именно оттуда, атом излучает Kα или Kβ, и этот квант
    может уйти из кристалла — событие покидает пик полного поглощения. Эффект
    большой: на 40 кэВ в CsI это четверть событий.

    Всё, кроме выхода флуоресценции, считается ИЗ БАЗЫ, ничего не набирается:

    * энергии линий — разности краёв поглощения (`xcom_edges`): Kα1 = K − L3,
      Kα2 = K − L2. Проверка сошлась со справочником до третьего знака:
      у иода 28.612 кэВ, у цезия 30.973, у германия 9.886, у висмута 77.107;
    * доля поглощений на K-оболочке — из скачка сечения фотоэффекта на крае
      (в сетке XCOM на энергии края стоят ДВЕ точки, ниже и выше):
      f_K = 1 − 1/скачок. У иода скачок 6.04, f_K = 0.834;
    * соотношение линий и энергия Kβ — из `decay_radiations`, по нуклидам.
      Считать надо ДОЛЮ внутри набора одного родителя: у разных распадов
      абсолютные выходы рентгена разные, и медиана по абсолютным числам
      смешала бы несмешиваемое. Отбор элемента — по совпадению энергии Kα1 с
      разностью краёв.

    Выход флуоресценции ω_K — единственное, чего нет ни в одной поставке.
    Берётся аппроксимация Bambynek и др. (Rev. Mod. Phys. 44, 716 (1972)):
    (ω/(1−ω))^(1/4) = 0.015 + 0.0327·Z − 0.64·10⁻⁶·Z³. Сверено со
    справочными значениями: I 0.882 против 0.884, Cs 0.895 против 0.894,
    Gd 0.934 против 0.933, Bi 0.969 против 0.968, Ge 0.540 против 0.535.
    У лёгких элементов формула врёт заметнее (Na 0.019 против 0.023), но там
    и сам выход ничтожен, и рентген в 1 кэВ поглощается на месте.
    """
    edges = {}
    for z, shell, energy in db.execute("select z, shell, energy_ev from xcom_edges"):
        edges.setdefault(z, {})[shell] = energy

    ka1_of = {}
    for z, shells in edges.items():
        if "K" in shells and "L3" in shells:
            ka1_of[z] = (shells["K"] - shells["L3"]) / 1000.0

    by_parent = {}
    for pid, kind, energy, intensity in db.execute(
            "select parent_nucid, type_c, energy_num, intensity_num"
            " from decay_radiations where type_a='X'"
            " and type_c in ('KA1','KA2','KB')"
            " and energy_num>0 and intensity_num>0"):
        by_parent.setdefault(pid, {})[kind] = (energy, intensity)

    samples = {}
    for lines in by_parent.values():
        if not set(["KA1", "KA2", "KB"]).issubset(lines):
            continue
        measured = lines["KA1"][0]
        hit = [z for z, value in ka1_of.items() if abs(measured - value) < 0.004 * value]
        if len(hit) != 1:
            continue                      # линия попала между двумя элементами
        i1, i2, ib = lines["KA1"][1], lines["KA2"][1], lines["KB"][1]
        total = i1 + i2 + ib
        samples.setdefault(hit[0], []).append(
            (i1 / total, i2 / total, ib / total, lines["KB"][0]))

    def median(values):
        values = sorted(values)
        n = len(values)
        return values[n // 2] if n % 2 else 0.5 * (values[n // 2 - 1] + values[n // 2])

    ratios = {}
    for z, rows in samples.items():
        ratios[z] = (median([r[0] for r in rows]), median([r[1] for r in rows]),
                     median([r[2] for r in rows]), median([r[3] for r in rows]),
                     len(rows))

    def interpolated(z):
        """Соотношения меняются с Z плавно — недостающие берём линейно по соседям."""
        lo = max([k for k in ratios if k < z] or [0])
        hi = min([k for k in ratios if k > z] or [0])
        if not lo or not hi:
            near = lo or hi
            if not near:
                return None
            r = ratios[near]
            return r[0], r[1], r[2], None, 0
        a, b = ratios[lo], ratios[hi]
        t = (z - lo) / float(hi - lo)
        return (a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]),
                a[2] + t * (b[2] - a[2]), None, 0)

    rows = []
    borrowed = []
    for z in sorted(edges):
        shells = edges[z]
        if "K" not in shells or "L2" not in shells or "L3" not in shells:
            continue
        k_edge = shells["K"]
        pair = db.execute("select photoelectric_b from xcom_cross_sections"
                          " where z=? and energy_ev between ? and ?"
                          " order by energy_ev", (z, k_edge - 1.0, k_edge + 1.0)).fetchall()
        if len(pair) < 2 or not pair[0][0] > 0.0:
            continue
        jump = pair[-1][0] / pair[0][0]

        value = 0.015 + 0.0327 * z - 0.64e-06 * z ** 3
        omega = value ** 4 / (1.0 + value ** 4)

        ka1 = k_edge - shells["L3"]
        ka2 = k_edge - shells["L2"]
        got = ratios.get(z)
        if got is None:
            got = interpolated(z)
            if got is None:
                continue
            borrowed.append(z)
        w1, w2, wb, kb_kev, count = got
        # энергия Kβ у элементов без измерений: K − M3, оболочка, с которой
        # идёт основная часть Kβ1
        kb = kb_kev * 1000.0 if kb_kev else (k_edge - shells.get("M3", shells["L2"]))
        rows.append((z, k_edge, jump, 1.0 - 1.0 / jump, omega,
                     ka1, w1, ka2, w2, kb, wb, count))

    db.executemany("insert into xray_fluorescence"
                   " values (?,?,?,?,?,?,?,?,?,?,?,?)", rows)
    return len(rows), borrowed


def read_icc(directory):
    """Разобрать таблицы коэффициентов внутренней конверсии ICCxxxH.TXT.

    Файл на элемент, шапка из двух строк, дальше строки вида

        Z Shell E(gamma)  E1 E2 E3 E4  M1 M2 M3 M4

    энергия перехода в кэВ. Разбирать НАДО ПО КОЛОНКАМ, а не по пробелам:
    поле оболочки шириной четыре знака, поле энергии — шесть, и на
    килоэлектронвольтных энергиях они слипаются («` 30  L11000.0`» — это L1 на
    1000 кэВ). Разбор по пробелам даёт у таких строк десять полей вместо
    одиннадцати и молча их теряет.

    В части файлов таблица идёт ДВАЖДЫ — два разных расчёта, расходятся в
    третьей значащей цифре. Оба остаются, второй помечается variant = 2:
    выбрасывать один из них, не зная, чем они отличаются, значит решать за
    того, кто эти данные собирал.

    Часть элементов в поставке отсутствует — берём то, что есть, и печатаем
    список пропущенных: молчаливая дыра в таблице хуже, чем известная.
    """
    rows = []
    have = set()
    dropped = 0
    for name in sorted(os.listdir(directory)):
        if not name.upper().startswith("ICC"):
            continue
        variant = {}
        with io.open(os.path.join(directory, name), encoding="latin-1") as f:
            for line in f:
                if not line[:3].strip().isdigit():
                    continue
                z = int(line[:3])
                shell = line[3:7].strip()
                try:
                    energy = float(line[7:13])
                    values = [float(line[13 + 9 * i:22 + 9 * i]) for i in range(8)]
                except ValueError:
                    dropped += 1
                    continue
                key = (z, shell, energy)
                variant[key] = variant.get(key, 0) + 1
                have.add(z)
                rows.append((z, shell, energy, variant[key]) + tuple(values))
    if dropped:
        raise ValueError("%s: %d строк не разобрались по колонкам" % (directory, dropped))
    return rows, sorted(have)


def main():
    db_path, xcom_dir, fcomp_path = sys.argv[1], sys.argv[2], sys.argv[3]
    icc_dir = sys.argv[4] if len(sys.argv) > 4 else None

    estar_dir = os.path.dirname(fcomp_path)
    star_root = os.path.dirname(estar_dir)
    estar_f_path = os.path.join(estar_dir, "ESTAR.f")
    mats_path = os.path.join(estar_dir, "MATS")
    material_txt_path = os.path.join(estar_dir, "material.txt")
    db = sqlite3.connect(db_path)
    db.executescript("""
        drop table if exists xcom_elements;
        drop table if exists xcom_cross_sections;
        drop table if exists xcom_edges;
        drop table if exists star_materials;
        drop table if exists star_material_composition;

        create table xcom_elements (
            z              integer primary key,
            atomic_weight  real not null,
            n_energies     integer not null,
            n_edges        integer not null
        );

        -- Сечения в барнах на атом, как в поставке. Переводить в см2/г здесь
        -- нельзя: перевод зависит от атомного веса, а он у нас теперь берётся
        -- из самой базы, и хранить результат деления значило бы вморозить
        -- сегодняшнее значение веса в таблицу сечений.
        create table xcom_cross_sections (
            z                integer not null,
            energy_ev        real not null,
            coherent_b       real not null,
            incoherent_b     real not null,
            photoelectric_b  real not null,
            pair_nuclear_b   real not null,
            pair_electron_b  real not null,
            primary key (z, energy_ev)
        ) without rowid;

        -- Край поглощения: на одной энергии сетка содержит ДВЕ точки, ниже и
        -- выше края. Индекс говорит, какая из них верхняя.
        create table xcom_edges (
            z           integer not null,
            shell       text not null,
            energy_ev   real not null,
            grid_index  integer not null,
            primary key (z, shell)
        ) without rowid;

        drop table if exists estar_shells;
        drop table if exists estar_radiative_stopping;

        -- Заселённости и энергии связи оболочек: из них ESTAR считает
        -- ионизационные потери по Бете. Данные поэлементные, поэтому лежат
        -- здесь, а не рядом с веществами.
        --
        -- Знак заселённости ЗНАЧАЩИЙ, и убирать его нельзя: отрицательная
        -- метит внешнюю (проводящую) оболочку, на её знаке ветвится сама
        -- программа (`ESTAR.f:184`, `IF(NC(NMAX))`). Проверено на всех ста
        -- элементах: Σ|заселённость| = Z, и все 86 отрицательных значений
        -- стоят последними в своём элементе.
        create table estar_shells (
            z            integer not null,
            shell_index  integer not null,
            occupation   integer not null,
            binding_ev   real not null,
            primary key (z, shell_index)
        ) without rowid;

        -- Радиационная тормозная способность, МэВ·см2/г, на сетке ESTAR.
        -- Складывается по правилу Брэгга: S(вещество) = Σ w_i · S(элемент i).
        create table estar_radiative_stopping (
            z                    integer not null,
            energy_mev           real not null,
            stopping_mev_cm2_g   real not null,
            primary key (z, energy_mev)
        ) without rowid;

        -- id — порядок записи в FCOMP, star_id — НОМЕР вещества в поставке
        -- (001..278 и 906 у графита). Это разные вещи: см. read_star_ids.
        -- Ссылаться снаружи надо на star_id, он же стоит в списках ASTAR/PSTAR.
        create table star_materials (
            id                integer primary key,
            star_id           integer not null,
            name              text not null,
            star_name         text not null,
            z_over_a          real not null,
            potential_ev      real not null,
            density_g_cm3     real not null
        );

        create table star_material_composition (
            material_id      integer not null,
            z                integer not null,
            weight_fraction  real not null,
            primary key (material_id, z)
        ) without rowid;

        drop table if exists estar_element_potential;
        drop table if exists icc_coefficients;
        drop table if exists star_stopping_powers;
        drop table if exists xray_fluorescence;

        -- Ответ атома на дырку в K-оболочке. `k_fraction` — доля поглощений,
        -- пришедшихся на K (из скачка сечения на крае), `omega_k` — вероятность
        -- ответить квантом, а не оже-электроном. Веса линий нормированы на
        -- единицу в сумме. Всё, кроме omega_k, посчитано из этой же базы;
        -- omega_k — аппроксимация, см. build_fluorescence.
        create table xray_fluorescence (
            z            integer primary key,
            k_edge_ev    real not null,
            jump_ratio   real not null,
            k_fraction   real not null,
            omega_k      real not null,
            ka1_ev       real not null,
            ka1_weight   real not null,
            ka2_ev       real not null,
            ka2_weight   real not null,
            kb_ev        real not null,
            kb_weight    real not null,
            n_nuclides   integer not null
        );

        -- Тормозная способность, пробег CSDA и коэффициент извилистости для
        -- тяжёлых заряженных частиц: 'alpha' — из ASTAR (122 энергии),
        -- 'proton' — из PSTAR (133). Для электрона такой таблицы в поставке
        -- НЕТ: ESTAR считает пробег и выход тормозного на месте, из FEDAT.
        create table star_stopping_powers (
            particle                text not null,
            material_star_id        integer not null,
            energy_mev              real not null,
            electronic_mev_cm2_g    real not null,
            nuclear_mev_cm2_g       real not null,
            csda_range_g_cm2        real not null,
            detour_factor           real not null,
            primary key (particle, material_star_id, energy_mev)
        ) without rowid;

        -- Средняя энергия возбуждения элемента, эВ. Нужна, чтобы посчитать I
        -- ПРОИЗВОЛЬНОГО состава по правилу Брэгга: ln I = Σ w_i (Z_i/A_i) ln I_i
        -- / Σ w_i (Z_i/A_i). Для 279 готовых веществ I лежит в star_materials,
        -- а для формулы, которой в FCOMP нет, взять её больше неоткуда.
        create table estar_element_potential (
            z                  integer primary key,
            potential_ev       real not null,
            potential_gas_ev   real,
            potential_cond_ev  real
        );

        -- Коэффициент внутренней конверсии: доля переходов, снятых с ядра
        -- электроном оболочки вместо гамма-кванта. Энергия перехода в кэВ,
        -- дальше коэффициент для мультипольности E1..E4 и M1..M4.
        create table icc_coefficients (
            z           integer not null,
            shell       text not null,
            energy_kev  real not null,
            variant     integer not null,
            e1          real not null,
            e2          real not null,
            e3          real not null,
            e4          real not null,
            m1          real not null,
            m2          real not null,
            m3          real not null,
            m4          real not null,
            primary key (z, shell, energy_kev, variant)
        ) without rowid;
    """)

    n_el = n_pt = n_edge = 0
    for z in range(1, 101):
        path = os.path.join(xcom_dir, "MDATX3.%03d" % z)
        if not os.path.exists(path):
            continue
        data = read_mdatx3(path)
        db.execute("insert into xcom_elements values (?,?,?,?)",
                   (data["z"], data["atomic_weight"], len(data["grid"]), len(data["edges"])))
        n_el += 1
        c = data["channels"]
        rows = []
        seen = set()
        for i, e in enumerate(data["grid"]):
            # дубль энергии на краю поглощения: разводим на 1e-4 эВ, иначе
            # первичный ключ схлопнет две физически разные точки в одну
            key = e
            while key in seen:
                key += 1e-4
            seen.add(key)
            rows.append((data["z"], key, c["coherent"][i], c["incoherent"][i],
                         c["photoelectric"][i], c["pair_nuclear"][i], c["pair_electron"][i]))
        db.executemany("insert into xcom_cross_sections values (?,?,?,?,?,?,?)", rows)
        n_pt += len(rows)
        for shell, energy, index in data["edges"]:
            db.execute("insert or replace into xcom_edges values (?,?,?,?)",
                       (data["z"], shell, energy, index))
            n_edge += 1

    fedat_path = os.path.join(os.path.dirname(fcomp_path), "FEDAT")
    n_shell = n_stop = 0
    if os.path.exists(fedat_path):
        for z, (occupation, binding, stopping) in sorted(read_fedat(fedat_path).items()):
            db.executemany("insert into estar_shells values (?,?,?,?)",
                           [(z, i + 1, occupation[i], binding[i]) for i in range(len(occupation))])
            n_shell += len(occupation)
            if len(stopping) != len(ESTAR_GRID):
                raise ValueError("Z=%d: точек тормозной %d, а сетка на %d"
                                 % (z, len(stopping), len(ESTAR_GRID)))
            db.executemany("insert into estar_radiative_stopping values (?,?,?)",
                           [(z, ESTAR_GRID[i], stopping[i]) for i in range(len(stopping))])
            n_stop += len(stopping)

    materials = read_fcomp(fcomp_path)
    star_ids = read_star_ids(mats_path, material_txt_path, [m["name"] for m in materials])
    for i, m in enumerate(materials, 1):
        star_id, star_name = star_ids[i - 1]
        db.execute("insert into star_materials values (?,?,?,?,?,?,?)",
                   (i, star_id, m["name"], star_name,
                    m["z_over_a"], m["potential_ev"], m["density"]))
        db.executemany("insert or replace into star_material_composition values (?,?,?)",
                       [(i, z, w) for z, w in m["composition"]])

    n_stop_hi = 0
    for particle, folder, data_name, source_name, n_energy in (
            ("alpha", "Astarzip", "FALPH", "ASTAR.f", 122),
            ("proton", "Pstarzip", "FPROT", "PSTAR.f", 133)):
        data_path = os.path.join(star_root, folder, data_name)
        source_path = os.path.join(star_root, folder, source_name)
        if not (os.path.exists(data_path) and os.path.exists(source_path)):
            continue
        rows = read_star_stopping(data_path, source_path, n_energy)
        db.executemany("insert into star_stopping_powers"
                       " values ('%s',?,?,?,?,?,?)" % particle, rows)
        n_stop_hi += len(rows)

    n_fluo, borrowed = build_fluorescence(db)
    print("Флуоресценция: элементов %d, из них соотношения линий взяты у соседей"
          " для %d %s" % (n_fluo, len(borrowed), borrowed if borrowed else ""))

    n_pot = 0
    if estar_f_path and os.path.exists(estar_f_path):
        potentials = read_estar_potentials(estar_f_path)
        db.executemany("insert into estar_element_potential values (?,?,?,?)",
                       [(z,) + potentials[z] for z in sorted(potentials)])
        n_pot = len(potentials)

    n_icc = 0
    if icc_dir and os.path.isdir(icc_dir):
        icc_rows, icc_z = read_icc(icc_dir)
        db.executemany("insert into icc_coefficients"
                       " values (?,?,?,?,?,?,?,?,?,?,?,?)", icc_rows)
        n_icc = len(icc_rows)
        n_second = sum(1 for r in icc_rows if r[3] > 1)
        missing = [z for z in range(3, max(icc_z) + 1) if z not in icc_z]
        print("ICC: строк %d (из них повторный расчёт %d), элементов %d (Z %d..%d),"
              " нет данных для Z %s"
              % (n_icc, n_second, len(icc_z), min(icc_z), max(icc_z), missing))

    db.commit()
    print("XCOM: элементов %d, точек сечений %d, краёв поглощения %d" % (n_el, n_pt, n_edge))
    print("STAR: веществ %d, строк состава %d, оболочек %d, точек тормозной %d"
          % (len(materials), sum(len(m["composition"]) for m in materials), n_shell, n_stop))
    print("ESTAR: энергий возбуждения элементов %d" % n_pot)
    print("ASTAR/PSTAR: точек тормозной для тяжёлых частиц %d" % n_stop_hi)
    db.close()


if __name__ == "__main__":
    main()
