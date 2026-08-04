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
    12I6            индексы краёв в сетке (в обратном порядке)
    14(1X,A2)       метки оболочек
    8F9.1           энергии краёв (в обратном порядке)
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

    python import_xcom_star.py <nucdb.sqlite> <каталог XCOM> <FCOMP>
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
        # индексы и энергии записаны в обратном порядке, метки — в прямом
        idx = list(reversed(idx))
        energies = list(reversed(energies))
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


def main():
    db_path, xcom_dir, fcomp_path = sys.argv[1], sys.argv[2], sys.argv[3]
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

        create table star_materials (
            id                integer primary key,
            name              text not null,
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
    for i, m in enumerate(materials, 1):
        db.execute("insert into star_materials values (?,?,?,?,?)",
                   (i, m["name"], m["z_over_a"], m["potential_ev"], m["density"]))
        db.executemany("insert or replace into star_material_composition values (?,?,?)",
                       [(i, z, w) for z, w in m["composition"]])

    db.commit()
    print("XCOM: элементов %d, точек сечений %d, краёв поглощения %d" % (n_el, n_pt, n_edge))
    print("STAR: веществ %d, строк состава %d, оболочек %d, точек тормозной %d"
          % (len(materials), sum(len(m["composition"]) for m in materials), n_shell, n_stop))
    db.close()


if __name__ == "__main__":
    main()
