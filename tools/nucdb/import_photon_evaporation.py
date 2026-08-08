# -*- coding: utf-8 -*-
"""
Втягивает в nucdb.sqlite схемы уровней Geant4 PhotonEvaporation (проверено
на 6.1.2, 3364 файла `zZ.aA`).

ЗАЧЕМ. Три дыры §9а базы закрываются ОДНИМ источником (database/scheme.md):

  D-2  ADOPTED LEVELS: полной схемы уровней нуклида у нас нет вовсе — в
       поставке ЛСРМ `TCCFCALC\\LIB\\ENSDF2` только схемы РАСПАДА. Здесь
       уровни есть все, включая основное состояние, с энергией, периодом
       и спином-чётностью.
  D-3  Привязка гамма к КОНЕЧНОМУ уровню: у 17 276 наших гамма её нет.
       Здесь каждый переход задан парой «номер уровня → номер уровня», то
       есть привязка не выводится, а лежит готовой.
  D-5  Пооболочечные коэффициенты конверсии: у нас `icc` только Z ≥ 14 и не
       выше M5. Здесь на КАЖДЫЙ переход — полный α и разбиение по K, L1-3,
       M1-5 и внешним оболочкам.

И сверх того — то, без чего не посчитать угловые γ-γ корреляции (TODO N5):
**мультипольность и коэффициент смешивания** каждого перехода плюс спин и
чётность каждого уровня. В наших `ensdf_gammas` мультипольность заполнена у
33 % строк, коэффициент смешивания — у 6.7 %; здесь они идут вместе со
схемой и в единой кодировке.

ФОРМАТ (README-LevelGammaData поставки, дословно проверен по файлам).
Файл — последовательность блоков, блок на уровень. Строка уровня:

    seq  floating  E_кэВ  T½_с  JPi  n_gammas

`floating` — «-» либо +X/+Y/… (уровень с неопределённым положением);
T½ = −1 у стабильного основного состояния; JPi = 99 означает «в ENSDF нет»;
знак JPi — чётность. Далее n_gammas строк перехода:

    to_seq  E_кэВ  I_отн  мультипольность  δ  α  [10 долей оболочек]

Мультипольность закодирована: 1..7 = E0,E1,M1,E2,M2,E3,M3, а смесь —
100·Nx+Ny (304 = M1+E2). Ноль — неизвестна. Доли оболочек печатаются ТОЛЬКО
при α ≠ 0 и идут в порядке K, L1, L2, L3, M1, M2, M3, M4, M5, внешние.

ЧТО НЕ БЕРЁТСЯ. Интенсивности здесь ОТНОСИТЕЛЬНЫЕ (на 100 у сильнейшего
перехода с уровня), а не на распад: заселённость уровней задаёт β-ветвь, и
она лежит в RadioactiveDecay, не здесь. Поэтому таблица `decay_radiations`
этим импортом НЕ трогается — данные ложатся рядом и связываются по нуклиду.

РАЗМЕР И ЧТО ИЗ ОБОЛОЧЕК БЕРЁТСЯ. Доли конверсии — десять чисел на переход.
Положить их как есть — это **1 396 803 строки** (измерено при пороге 1e-4),
десятки мегабайт в базе, которая ходит в поставке, и ни одного читателя:
подоболочечная конверсия сейчас в расчёте не участвует нигде, а L-рентген
модель не разыгрывает вовсе (`EfficiencySimulator`, шапка). Заводить
таблицу-миллионник «на будущее» — ровно та ошибка, которой в базе уже
двадцать восемь таблиц без читателя (§9а F-2).

Поэтому десять оболочек сворачиваются в ЧЕТЫРЕ доли — K, L (L1-3),
M (M1-5), внешние — и лежат колонками в самой строке перехода, а не
отдельной таблицей. Заполняются только при α ≥ `ICC_FLOOR`; ниже — NULL,
который в SQLite стоит байт. Физика такого свёртывания: рентген после
конверсии рождает практически только K-дырка, а L и M различать незачем,
пока L-флуоресценция не моделируется. Понадобится разбиение тоньше —
поднимать из поставки заново, она лежит локально (строка в TODO).

ЧЕМ ДОБИРАЕТСЯ НЕДОСТАЮЩЕЕ (указание Amber 08.08.2026: «если для 3 чего-то
нет, возьми это из ЛСРМ»). У Geant4 спин-чётность стоит не у всех уровней
(73.3 %), мультипольность — не у всех переходов (68.5 %), а коэффициент
смешивания печатается только там, где он в ENSDF есть (4.9 %). Библиотека
ЛСРМ `TCCFCALC\\LIB\\ENSDF2` **уже втянута** в таблицы `ensdf_*`, и её
покрытие ДРУГОЕ: спин у 79 % уровней, коэффициент смешивания у 6.7 %.
Поэтому вторым проходом пустые места Geant4 заполняются из неё — по нуклиду
и энергии, с допуском `MATCH_KEV`, и только при ОДНОЗНАЧНОМ совпадении.
Откуда взято значение, видно в колонке `filled_from`: NULL — Geant4,
`ensdf` — добрано.

Что при этом НЕ добирается и почему:

  * ICC ЛСРМ (`Lib\\ICC`) — там те же 93 значения Z (нет 4, 5, 7…13) и те же
    оболочки K…M5, что уже лежат в `icc_coefficients`. Новая поставка
    2.10.1844 в этом месте не отличается от старой ничем; пробел D-5
    заполняется не ЛСРМ, а полным α из Geant4;
  * неоднозначные записи ENSDF — `M1,E2` (либо то, либо это), `D`, `Q`,
    `1,2+`: подставить одно из двух значило бы выдумать данные. Записи в
    скобках и квадратных скобках (`(E2)`, `[M1]`) берутся: это оценка
    составителя, а не неоднозначность;
  * мультипольности выше E3/M3: кодировка Geant4 их не описывает
    (README поставки перечисляет ровно E0…M3).

    python tools/nucdb/import_photon_evaporation.py [--db X.sqlite]
           [--source <каталог PhotonEvaporation>] [--dry] [--no-fill]

`--dry` разбирает и печатает сводку, ничего не записывая.
"""

import argparse
import os
import re
import sqlite3
import sys

DEFAULT_SOURCE = r"C:\Users\moroz\source\repos\GEANT4\PhotonEvaporation6.1.2"
DEFAULT_DB = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..",
    "BecquerelMonitor", "nucdb.sqlite")

# Ниже этого коэффициента конверсии разбиение по оболочкам в расчёте не
# значит ничего: сам канал конверсии даёт меньше промилле переходов.
ICC_FLOOR = 1.0e-3

FILE_RE = re.compile(r"^z(\d+)\.a(\d+)$")

# Порядок долей в строке файла: K, L1-3, M1-5, внешние (README поставки).
# Свёртка в четыре группы — по числу долей в каждой.
SHELL_GROUPS = ((1, "k"), (3, "l"), (5, "m"), (1, "outer"))

SCHEMA = """
-- Укладка целочисленная там, где это ничего не стоит по точности: энергия
-- в ЭЛЕКТРОНВОЛЬТАХ целым (сетка поставки — три знака после запятой в кэВ),
-- доли — в миллионных. У SQLite целый нуль занимает НОЛЬ байт в строке, а
-- вещественный — восемь; в этих таблицах нули составляют треть значений.
create table if not exists g4_level (
    z          integer not null,
    a          integer not null,
    seq        integer not null,   -- 0 = основное состояние
    floating   text,               -- null = обычный уровень; иначе +X/+Y/...
    energy_ev  integer not null,
    half_life_sec real,            -- null = стабильно (в файле -1)
    jpi        real,               -- знак = чётность; null = в ENSDF нет (99)
    jpi_from   text,               -- NULL — из Geant4, 'ensdf' — добрано
    primary key (z, a, seq)
) without rowid;

create table if not exists g4_gamma (
    z          integer not null,
    a          integer not null,
    from_seq   integer not null,
    -- Порядковый номер перехода ВНУТРИ уровня. Стоит в ключе не для красоты:
    -- в поставке есть уровни с двумя строками на одну и ту же пару
    -- (from, to, энергия) — 456 штук на все 3364 файла. Без этого поля
    -- `insert or replace` их молча съедал, и таблица выходила короче файла
    -- на полтысячи переходов, о чём никто бы не узнал.
    idx        integer not null,
    to_seq     integer not null,
    energy_ev  integer not null,
    intensity_ppm integer not null,-- относительная ×1e4: 100 % = 1 000 000
    multipolarity integer not null,-- кодировка Geant4; 0 = неизвестна
    mixing_ratio  real    not null,-- дельта; 0 = чистый переход либо нет данных
    icc_total     real    not null,-- alpha = Ic/Ig
    icc_k_ppm  integer,            -- доли полной конверсии по группам оболочек,
    icc_l_ppm  integer,            -- миллионные; NULL — alpha ниже порога
    icc_m_ppm  integer,
    icc_outer_ppm integer,
    -- Откуда взяты мультипольность и коэффициент смешивания: NULL — из
    -- Geant4, 'ensdf' — добраны из библиотеки ЛСРМ вторым проходом.
    filled_from text,
    primary key (z, a, from_seq, idx)
) without rowid;

create index if not exists g4_gamma_energy on g4_gamma (z, a, energy_ev);
"""


def shell_groups(parts):
    """
    Десять долей строки → четыре: K, L, M, внешние. Пустой ответ, если долей
    в строке нет (файл их печатает только при ненулевом α).
    """
    values = parts[6:16]
    if len(values) < 10:
        return None

    out, i = [], 0
    for width, _ in SHELL_GROUPS:
        out.append(int(round(1e6 * sum(float(v) for v in values[i:i + width]))))
        i += width

    return out


def zero_as_int(value):
    """
    Ноль отдаём ЦЕЛЫМ: у SQLite целый нуль занимает в строке ноль байтов, а
    вещественный — восемь, и нулей здесь треть значений.
    """
    return 0 if value == 0.0 else value


def parse_file(path, z, a):
    """Уровни и переходы одного нуклида. Возвращает (levels, gammas)."""
    levels, gammas = [], []
    current = None
    expect = 0
    index = 0
    with open(path, "r", encoding="latin-1") as handle:
        for lineno, raw in enumerate(handle, 1):
            parts = raw.split()
            if not parts:
                continue

            if expect > 0:
                # строка перехода
                if len(parts) < 6:
                    raise ValueError("%s:%d: строка перехода короче шести полей"
                                     % (path, lineno))
                to_seq = int(parts[0])
                energy = float(parts[1])
                intensity = float(parts[2])
                mult = int(float(parts[3]))
                mixing = float(parts[4])
                icc = float(parts[5])
                groups = shell_groups(parts) if icc >= ICC_FLOOR else None
                if groups is None:
                    groups = [None, None, None, None]

                gammas.append((z, a, current, index, to_seq,
                               int(round(energy * 1000.0)),
                               int(round(intensity * 1e4)),
                               mult, zero_as_int(mixing), zero_as_int(icc),
                               groups[0], groups[1], groups[2], groups[3],
                               None))
                expect -= 1
                index += 1
                continue

            # строка уровня
            if len(parts) < 6:
                raise ValueError("%s:%d: строка уровня короче шести полей"
                                 % (path, lineno))
            seq = int(parts[0])
            floating = parts[1]
            energy = float(parts[2])
            half_life = float(parts[3])
            jpi = float(parts[4])
            n_gammas = int(parts[5])
            levels.append((z, a, seq,
                           None if floating == "-" else floating,
                           int(round(energy * 1000.0)),
                           None if half_life < 0 else zero_as_int(half_life),
                           None if abs(abs(jpi) - 99.0) < 1e-9 else zero_as_int(jpi),
                           None))
            current = seq
            expect = n_gammas
            index = 0

    if expect != 0:
        raise ValueError("%s: файл кончился, а переходов не хватает: %d"
                         % (path, expect))

    return levels, gammas


# ----------------------------------------------------------------------
# Второй проход: добор недостающего из библиотеки ЛСРМ (таблицы ensdf_*)
# ----------------------------------------------------------------------

# Допуск сопоставления уровня и перехода по энергии, кэВ. Источник у обеих
# библиотек в конечном счёте один (ENSDF), расходятся они округлением.
MATCH_KEV = 0.3

# Кодировка мультипольности Geant4 (README поставки): 1..7 = E0,E1,M1,E2,M2,E3,M3.
MULT_CODE = {"E0": 1, "E1": 2, "M1": 3, "E2": 4, "M2": 5, "E3": 6, "M3": 7}

JPI_RE = re.compile(r"^(\d+)(?:/2)?([+-])$")


def parse_jpi(text):
    """
    Спин-чётность ENSDF («2+», «3/2-», «(5/2+)») → число со знаком чётности,
    как хранит Geant4. None — запись неоднозначна и подставлять нечего.
    """
    if not text:
        return None

    s = text.strip().replace("[", "").replace("]", "")
    s = s.replace("(", "").replace(")", "").replace(" ", "")
    if not s or any(ch in s for ch in ",&:"):
        return None                       # «1,2+», «2+ TO 4+» — не одно значение

    half = "/2" in s
    m = JPI_RE.match(s)
    if not m:
        return None

    value = float(m.group(1)) / (2.0 if half else 1.0)
    return value if m.group(2) == "+" else -value


def parse_multipolarity(text):
    """
    Мультипольность ENSDF → код Geant4. «E2» → 4, «M1+E2» → 304,
    «M1(+E2)» → 304 (примесь в скобках — та же смесь). None — либо
    неоднозначно («M1,E2», «D», «Q»), либо выше E3/M3, чего кодировка
    Geant4 не описывает.
    """
    if not text:
        return None

    s = text.strip().upper().replace("[", "").replace("]", "")
    s = s.replace("(", "").replace(")", "").replace(" ", "")
    if not s or "," in s:
        return None

    parts = s.split("+")
    if not 1 <= len(parts) <= 2:
        return None

    codes = []
    for part in parts:
        if part not in MULT_CODE:
            return None                   # D, Q, E4, M4, мусор

        codes.append(MULT_CODE[part])

    return codes[0] if len(codes) == 1 else 100 * codes[0] + codes[1]


def nuclide_index(connection):
    """nucid библиотеки ЛСРМ («100Ag») → (z, a) по таблице `nuclides`."""
    symbols = {}
    for symbol, z in connection.execute(
            "select distinct symbol, z from nuclides where symbol is not null"):
        symbols[symbol.strip().upper()] = z

    index = {}
    for (nucid,) in connection.execute("select distinct nucid from ensdf_datasets"):
        if not nucid:
            continue

        m = re.match(r"^(\d+)([A-Za-z]+)$", nucid.strip())
        if not m:
            continue

        z = symbols.get(m.group(2).upper())
        if z is not None:
            index[nucid] = (z, int(m.group(1)))

    return index


def unique_near(table, energy_kev):
    """
    Однозначное значение таблицы `{энергия: значение}` в допуске; None, если
    записей нет или они РАСХОДЯТСЯ. Подставлять «первую подошедшую» из двух
    разных — именно тот способ, каким в базу попадают чужие числа.

    Согласные повторы при этом отбрасывать нельзя: наборы ENSDF дублируются
    по нуклиду (TODO W9), одна и та же линия перечислена в схеме распада
    каждого родителя, и требование «ровно одна запись» выкашивало 97 %
    доступного (измерено: 544 мультипольности вместо 15 тысяч).
    """
    hits = [v for e, v in table if abs(e - energy_kev) <= MATCH_KEV]
    if not hits:
        return None

    first = hits[0]
    for value in hits[1:]:
        if abs(value - first) > 1e-9 * max(1.0, abs(first)):
            return None                   # библиотека сама себе противоречит

    return first


def fill_from_ensdf(connection):
    """Добирает спин, мультипольность и коэффициент смешивания из `ensdf_*`."""
    index = nuclide_index(connection)
    if not index:
        print("  добор пропущен: таблиц ensdf_* в базе нет")
        return

    # Библиотека ЛСРМ по (z, a). Наборов на нуклид бывает несколько (W9):
    # сливаем их в один список, а неоднозначность снимает `unique_near`.
    levels = {}
    gammas = {}
    for nucid, dataset_id in connection.execute(
            "select nucid, id from ensdf_datasets"):
        key = index.get(nucid)
        if key is None:
            continue

        for energy, jpi in connection.execute(
                "select energy_kev, jpi from ensdf_levels where dataset_id=?"
                " and energy_kev is not null and jpi is not null", (dataset_id,)):
            value = parse_jpi(jpi)
            if value is not None:
                levels.setdefault(key, []).append((energy, value))

        for energy, mult, mixing in connection.execute(
                "select energy_kev, multipolarity, mixing_ratio from ensdf_gammas"
                " where dataset_id=? and energy_kev is not null", (dataset_id,)):
            gammas.setdefault(key, []).append(
                (energy, parse_multipolarity(mult), mixing))

    level_updates = []
    for z, a, seq, energy_ev in connection.execute(
            "select z, a, seq, energy_ev from g4_level where jpi is null"):
        table = levels.get((z, a))
        if not table:
            continue

        value = unique_near(table, energy_ev / 1000.0)
        if value is not None:
            level_updates.append((value, z, a, seq))

    mult_updates, mixing_updates = [], []
    for z, a, from_seq, idx, energy_ev, mult, mixing in connection.execute(
            "select z, a, from_seq, idx, energy_ev, multipolarity, mixing_ratio"
            " from g4_gamma where multipolarity = 0 or mixing_ratio = 0"):
        table = gammas.get((z, a))
        if not table:
            continue

        energy = energy_ev / 1000.0
        if mult == 0:
            value = unique_near([(e, m) for e, m, _ in table if m is not None], energy)
            if value is not None:
                mult_updates.append((value, z, a, from_seq, idx))

        if mixing == 0:
            value = unique_near(
                [(e, d) for e, _, d in table if d is not None and d != 0.0], energy)
            if value is not None:
                mixing_updates.append((value, z, a, from_seq, idx))

    connection.executemany(
        "update g4_level set jpi=?, jpi_from='ensdf' where z=? and a=? and seq=?",
        level_updates)
    connection.executemany(
        "update g4_gamma set multipolarity=?, filled_from='ensdf'"
        " where z=? and a=? and from_seq=? and idx=?", mult_updates)
    connection.executemany(
        "update g4_gamma set mixing_ratio=?, filled_from='ensdf'"
        " where z=? and a=? and from_seq=? and idx=?", mixing_updates)

    print("  добор из ЛСРМ: спин у %d уровней, мультипольность у %d переходов,"
          " коэффициент смешивания у %d"
          % (len(level_updates), len(mult_updates), len(mixing_updates)))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=DEFAULT_DB)
    parser.add_argument("--source", default=DEFAULT_SOURCE)
    parser.add_argument("--dry", action="store_true")
    parser.add_argument("--no-fill", action="store_true")
    args = parser.parse_args()

    if not os.path.isdir(args.source):
        sys.stderr.write("нет каталога поставки: %s\n" % args.source)
        return 2

    all_levels, all_gammas = [], []
    files = 0
    for name in sorted(os.listdir(args.source)):
        m = FILE_RE.match(name)
        if not m:
            continue

        z, a = int(m.group(1)), int(m.group(2))
        levels, gammas = parse_file(os.path.join(args.source, name), z, a)
        all_levels.extend(levels)
        all_gammas.extend(gammas)
        files += 1

    # ---- сводка ------------------------------------------------------
    jpi_known = sum(1 for row in all_levels if row[6] is not None)   # jpi
    mult_known = sum(1 for row in all_gammas if row[7] != 0)
    mixing_known = sum(1 for row in all_gammas if row[8] != 0)
    icc_known = sum(1 for row in all_gammas if row[9] > 0)
    shells_kept = sum(1 for row in all_gammas if row[10] is not None)

    print("файлов %d, уровней %d, переходов %d"
          % (files, len(all_levels), len(all_gammas)))
    print("  разбиение конверсии положено у %d переходов (alpha >= %.0e)"
          % (shells_kept, ICC_FLOOR))
    print("  спин-чётность известна у %d уровней (%.1f %%)"
          % (jpi_known, 100.0 * jpi_known / max(1, len(all_levels))))
    print("  мультипольность известна у %d переходов (%.1f %%)"
          % (mult_known, 100.0 * mult_known / max(1, len(all_gammas))))
    print("  коэффициент смешивания ненулевой у %d (%.1f %%)"
          % (mixing_known, 100.0 * mixing_known / max(1, len(all_gammas))))
    print("  конверсия ненулевая у %d (%.1f %%)"
          % (icc_known, 100.0 * icc_known / max(1, len(all_gammas))))

    if args.dry:
        print("--dry: в базу ничего не записано")
        return 0

    db = os.path.abspath(args.db)
    if not os.path.exists(db):
        sys.stderr.write("нет базы: %s\n" % db)
        return 2

    before = os.path.getsize(db)
    connection = sqlite3.connect(db)
    try:
        connection.executescript(SCHEMA)
        connection.execute("delete from g4_level")
        connection.execute("delete from g4_gamma")
        connection.executemany(
            "insert into g4_level values (?,?,?,?,?,?,?,?)", all_levels)
        connection.executemany(
            "insert into g4_gamma values (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
            all_gammas)
        if not args.no_fill:
            fill_from_ensdf(connection)

        connection.commit()
        # Без сжатия страниц прирост читается вдвое больше настоящего:
        # удалённые старые строки остаются в файле свободными страницами.
        connection.execute("vacuum")
    finally:
        connection.close()

    after = os.path.getsize(db)
    print("база: %.1f -> %.1f МБ (+%.1f)"
          % (before / 1048576.0, after / 1048576.0, (after - before) / 1048576.0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
