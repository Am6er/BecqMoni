# -*- coding: utf-8 -*-
"""
Втягивает в schemedb.sqlite схемы распада из поставки ЛСРМ
`C:\\LSRM\\NuclideMaster\\TCCFCALC\\LIB\\ENSDF2` — 272 файла по массовым
цепочкам, издание 2015 года.

Зачем. В базе уже есть ВЫХОДЫ линий (`decay_radiations`, 40216 гамма), но нет
СХЕМЫ: какой уровень какой заселяет, какая гамма из какого уровня в какой идёт.
Без этого нельзя посчитать каскадное суммирование — две гаммы одного каскада
попадают в детектор вместе и уходят из своих пиков в сумму. Именно этим занята
`TCCFCALC.dll` из той же поставки.

Формат — ENSDF (NNDC), фиксированные колонки, с двумя правками ЛСРМ:

  * в колонке 9 записи-заголовка набора стоит `*` (в оригинале пробел);
  * в колонках 1-5 записей уровня, гаммы и питания вместо NUCID стоит НОМЕР
    уровня в наборе, а в колонке 2 — метка `M` у метастабильного. NUCID
    сохранён только у записей заголовка, P (родитель) и Q.

Колонки самих записей стандартные (ENSDF Analysis and Utility Programs, гл. 2):

  1-5 NUCID/номер, 6 признак продолжения, 7 тип комментария, 8 тип записи,
  10-19 энергия, 20-21 её погрешность, дальше по типу записи:

  L  22-39 J^pi, 40-49 период, 50-55 погрешность периода
  G  22-29 отн. выход, 30-31 погр., 32-41 мультипольность, 42-49 дельта
     смешивания, 56-62 полный коэффициент конверсии, 65-74 полная
     интенсивность перехода
  B/E/A  22-29 интенсивность питания уровня (для E — 22-29 IB, 32-39 IE)

Записи продолжения (непробел в колонке 6), комментарии (C/D/T в колонке 7) и
записи S (структурные ссылки) не разбираются: там свободный текст.

НОМЕР УРОВНЯ В КОЛОНКАХ 1-5 — не только цифры. Перед номером стоит
однобуквенная пометка (`?` у неуверенного уровня, латинские буквы у полос), и
поле выглядит как `'?   4'` или `'  A 1'`. Разбор снимал только `M` и на всём
остальном отдавал пусто — **4558 уровней из 35415 (13 %) не писались вовсе**, а
печаталось при этом число РАЗОБРАННЫХ (W17). Номер берётся хвостовыми цифрами
поля, пометка отбрасывается. Что это правильно, проверено машинно: нумерация
внутри набора выходит сплошной 1..N во ВСЕХ 3323 наборах с уровнями, и
восстановленные номера не сталкиваются ни с уже читавшимися, ни между собой —
ни одного случая. То есть счётчик у формата один, а буква — только пометка.

Уровни, помеченные ОДНОЙ БУКВОЙ БЕЗ НОМЕРА (`'    A'`, `'    B'`, … — таких
195), номера не получают и не пишутся: ключ таблицы целочисленный. Они
считаются отдельно и печатаются, а не пропадают молча.

Привязка гаммы к уровням. Гамма принадлежит ПОСЛЕДНЕМУ встреченному уровню —
это и есть начальный уровень. Конечный ищется по энергии: E(нач) - E(гамма) с
допуском. Допуск НЕ фиксированный: берём максимум из 1.5 кэВ и суммы заявленных
погрешностей. Если совпадения нет — гамма остаётся с `to_level_seq = null`, и
это видно счётчиком. Придумывать привязку там, где её нет, нельзя: на ней
держится весь каскад.

    python import_ensdf.py <schemedb.sqlite> <каталог ENSDF2>
"""
import io
import os
import re
import sqlite3
import sys


def num(text):
    """Число из поля ENSDF или None. Поля бывают пустые, со знаком, в 1E-4."""
    text = text.strip()
    if not text:
        return None
    text = text.replace("E+", "E").rstrip("+")
    try:
        return float(text)
    except ValueError:
        return None


HALF_LIFE_UNITS = {
    "Y": 3.15576e7, "D": 86400.0, "H": 3600.0, "M": 60.0, "S": 1.0,
    "MS": 1e-3, "US": 1e-6, "NS": 1e-9, "PS": 1e-12, "FS": 1e-15, "AS": 1e-18,
    "EV": None, "KEV": None, "MEV": None,      # ширина уровня, не период
}


def half_life_seconds(text):
    """«30.04 Y» -> 9.4759e8 с. STABLE -> бесконечность, ширина уровня -> None."""
    text = text.strip().upper()
    if not text:
        return None
    if text.startswith("STABLE"):
        return float("inf")
    parts = text.split()
    if len(parts) < 2:
        return None
    value = num(parts[0])
    factor = HALF_LIFE_UNITS.get(parts[1])
    if value is None or factor is None:
        return None
    return value * factor


def parse_file(path):
    """Разобрать один файл массовой цепочки. Возвращает список наборов."""
    with io.open(path, encoding="latin-1") as f:
        lines = f.read().split("\n")

    datasets = []
    current = None
    level_seq = None
    for raw in lines:
        line = raw.rstrip("\r")
        if len(line) < 8:
            continue
        cont, kind = line[5], line[7]

        # заголовок набора: колонка 9 — звёздочка ЛСРМ
        if len(line) > 9 and line[8] == "*" and cont == " " and kind == " ":
            current = {
                "nucid": line[:5].strip(),
                "dsid": line[9:39].strip(),
                "ref": line[39:65].strip(),
                "date": line[65:].strip(),
                "levels": [],
                "gammas": [],
                "feedings": [],
            }
            datasets.append(current)
            level_seq = None
            continue

        if current is None or cont != " " or line[6] not in " ":
            continue

        # Хвостовые цифры поля 1-5: пометка перед номером (`?`, буква полосы,
        # `M` у метастабильного) к номеру не относится — см. шапку, W17.
        seq = re.search(r"(\d+)\s*$", line[:5])
        seq = int(seq.group(1)) if seq else None

        if kind == "L":
            level_seq = seq
            current["levels"].append({
                "seq": seq,
                "energy": num(line[9:19]),
                "energy_unc": line[19:21].strip(),
                "jpi": line[21:39].strip(),
                "half_life": line[39:49].strip(),
                "half_life_sec": half_life_seconds(line[39:49]),
                "metastable": 1 if len(line) > 1 and line[1] == "M" else 0,
            })
        elif kind == "G":
            current["gammas"].append({
                "seq": seq,
                "from_level": level_seq,
                "energy": num(line[9:19]),
                "energy_unc": line[19:21].strip(),
                "intensity": num(line[21:29]),
                "intensity_unc": line[29:31].strip(),
                "multipolarity": line[31:41].strip(),
                "mixing_ratio": num(line[41:49]),
                "conv_coef": num(line[55:62]),
                "total_intensity": num(line[64:74]),
            })
        elif kind in "BEA":
            # у E-записи в 22-29 стоит бета-плюс, а захват — в 32-39;
            # складывать их здесь нельзя, храним оба поля
            current["feedings"].append({
                "level_seq": level_seq,
                "kind": kind,
                "energy": num(line[9:19]),
                "intensity": num(line[21:29]),
                "intensity_ec": num(line[31:39]) if kind == "E" else None,
                "logft": line[41:49].strip() if kind in "BE" else "",
            })
        elif kind == "P":
            current["parent"] = {
                "nucid": line[:5].strip(),
                "energy": num(line[9:19]),
                "jpi": line[21:39].strip(),
                "half_life": line[39:49].strip(),
                "half_life_sec": half_life_seconds(line[39:49]),
                "q_value": num(line[64:74]),
            }
    return datasets


def link_gammas(dataset):
    """Найти конечный уровень каждой гаммы. Возвращает число непривязанных."""
    by_seq = {l["seq"]: l for l in dataset["levels"] if l["seq"] is not None}
    energies = sorted((l["energy"], l["seq"]) for l in dataset["levels"]
                      if l["energy"] is not None and l["seq"] is not None)
    unlinked = 0
    for g in dataset["gammas"]:
        g["to_level"] = None
        start = by_seq.get(g["from_level"])
        if start is None or start["energy"] is None or g["energy"] is None:
            unlinked += 1
            continue
        want = start["energy"] - g["energy"]
        best, best_d = None, None
        for energy, seq in energies:
            d = abs(energy - want)
            if best_d is None or d < best_d:
                best, best_d = seq, d
        # допуск: заявленные погрешности редко больше килоэлектронвольта,
        # но у старых наборов энергия уровня округлена, и жёсткие 0.5 кэВ
        # отрезают половину привязок
        if best is None or best_d > max(1.5, 0.002 * max(1.0, start["energy"])):
            unlinked += 1
            continue
        g["to_level"] = best
    return unlinked


def main():
    db_path, ensdf_dir = sys.argv[1], sys.argv[2]
    db = sqlite3.connect(db_path)
    db.executescript("""
        drop table if exists ensdf_datasets;
        drop table if exists ensdf_levels;
        drop table if exists ensdf_gammas;
        drop table if exists ensdf_feedings;

        -- Набор данных ENSDF: одна схема распада (или схема принятых уровней).
        -- dsid вида '137CS B- DECAY' — то, чем набор зовут в самой базе.
        create table ensdf_datasets (
            id            integer primary key,
            nucid         text not null,      -- ДОЧЕРНИЙ нуклид (у кого схема)
            dsid          text not null,
            ref           text,
            date          text,
            source_file   text not null,
            parent_nucid  text,
            parent_hl_sec real,
            q_value_kev   real
        );

        create table ensdf_levels (
            dataset_id     integer not null,
            seq            integer not null,
            energy_kev     real,
            energy_unc     text,
            jpi            text,
            half_life      text,
            half_life_sec  real,
            metastable     integer not null,
            primary key (dataset_id, seq)
        ) without rowid;

        -- from_level_seq — уровень, ИЗ которого идёт переход (последняя запись L
        -- перед этой гаммой, так устроен формат). to_level_seq найден по
        -- энергии и может быть null — тогда переход в схему не уложился.
        create table ensdf_gammas (
            id              integer primary key,
            dataset_id      integer not null,
            from_level_seq  integer,
            to_level_seq    integer,
            energy_kev      real,
            energy_unc      text,
            intensity       real,
            intensity_unc   text,
            multipolarity   text,
            mixing_ratio    real,
            conv_coef       real,
            total_intensity real
        );

        -- Питание уровня: B (бета-минус), E (захват и бета-плюс), A (альфа).
        create table ensdf_feedings (
            id            integer primary key,
            dataset_id    integer not null,
            level_seq     integer,
            kind          text not null,
            energy_kev    real,
            intensity     real,
            intensity_ec  real,
            logft         text
        );

        create index if not exists ix_ensdf_gammas_ds on ensdf_gammas(dataset_id);
        create index if not exists ix_ensdf_feed_ds on ensdf_feedings(dataset_id);
        create index if not exists ix_ensdf_ds_nucid on ensdf_datasets(nucid);
    """)

    ds_id = 0
    # Счётчики РАЗОБРАННОГО и ЗАЛИТОГО ведутся порознь и печатаются оба.
    # Пока была одна цифра, она считала разобранное, а в таблицу ложилось на
    # 13 % меньше, и разошедшееся число уехало из печати в scheme.md (W17).
    n_lev = n_lev_written = n_lev_unnumbered = 0
    n_gam = n_feed = n_unlinked = n_fake = 0
    for name in sorted(os.listdir(ensdf_dir)):
        if not name.upper().endswith(".ENX"):
            continue
        for ds in parse_file(os.path.join(ensdf_dir, name)):
            # Тестовая заглушка поставки ЛСРМ: нуклида 290XX не существует, а в
            # базе он выглядел как данные — 2 набора, 22 уровня, 20 гамма и 20
            # питаний (D17, вычищено 08.08.2026). Лежит в 290.ENX и 291.ENX.
            if ds["nucid"].upper().startswith("290XX") or "FAKE" in ds["dsid"].upper():
                n_fake += 1
                continue
            ds_id += 1
            n_unlinked += link_gammas(ds)
            parent = ds.get("parent") or {}
            db.execute("insert into ensdf_datasets values (?,?,?,?,?,?,?,?,?)",
                       (ds_id, ds["nucid"], ds["dsid"], ds["ref"], ds["date"], name,
                        parent.get("nucid"), parent.get("half_life_sec"),
                        parent.get("q_value")))
            numbered = [l for l in ds["levels"] if l["seq"] is not None]
            db.executemany("insert or replace into ensdf_levels values (?,?,?,?,?,?,?,?)",
                           [(ds_id, l["seq"], l["energy"], l["energy_unc"], l["jpi"],
                             l["half_life"], l["half_life_sec"], l["metastable"])
                            for l in numbered])
            n_lev += len(ds["levels"])
            # Залито — по ЧИСЛУ РАЗЛИЧНЫХ номеров: ключ таблицы (набор, seq), и
            # `insert or replace` при столкновении затирает молча. Сегодня
            # столкновений нет ни одного, и разница между этими двумя числами
            # ровно это и покажет, если формат однажды подсунет повтор.
            n_lev_written += len(set(l["seq"] for l in numbered))
            n_lev_unnumbered += len(ds["levels"]) - len(numbered)
            db.executemany("insert into ensdf_gammas"
                           " values (null,?,?,?,?,?,?,?,?,?,?,?)",
                           [(ds_id, g["from_level"], g["to_level"], g["energy"],
                             g["energy_unc"], g["intensity"], g["intensity_unc"],
                             g["multipolarity"], g["mixing_ratio"], g["conv_coef"],
                             g["total_intensity"]) for g in ds["gammas"]])
            n_gam += len(ds["gammas"])
            db.executemany("insert into ensdf_feedings values (null,?,?,?,?,?,?,?)",
                           [(ds_id, f["level_seq"], f["kind"], f["energy"],
                             f["intensity"], f["intensity_ec"], f["logft"])
                            for f in ds["feedings"]])
            n_feed += len(ds["feedings"])

    db.commit()
    print("ENSDF: наборов %d, гамма %d (без привязки к конечному уровню %d, "
          "%.1f%%), питаний %d; отброшено тестовых заглушек %d"
          % (ds_id, n_gam, n_unlinked, 100.0 * n_unlinked / max(1, n_gam),
             n_feed, n_fake))
    # Уровни — ДВУМЯ цифрами. Число разобранного в таблицу не годится: это оно
    # однажды уехало в scheme.md как число строк (W17).
    print("ENSDF: уровней разобрано %d, ЗАЛИТО %d; без номера (пометка буквой "
          "без цифры) %d, затёрто столкновениями номеров %d"
          % (n_lev, n_lev_written, n_lev_unnumbered,
             n_lev - n_lev_written - n_lev_unnumbered))
    real = db.execute("select count(*) from ensdf_levels").fetchone()[0]
    print("ENSDF: в таблице ensdf_levels строк %d — %s"
          % (real, "сходится" if real == n_lev_written else "НЕ СХОДИТСЯ со счётчиком"))
    db.close()


if __name__ == "__main__":
    main()
