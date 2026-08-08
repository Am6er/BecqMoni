# -*- coding: utf-8 -*-
"""
Втягивает в matdb.sqlite ИЗМЕРЕННЫЕ выходы флуоресценции (TODO N15, N17).

Зачем, если ω уже есть. То, что лежит в `xray_fluorescence.omega_k`, — сумма
`eadl_radiative` по вакансии K, то есть РАСЧЁТ (Скофилд / Чен–Крайземанн), и
сверять его было не с чем. Измерения показывают у него систематику: на
Z = 20…35 он занижен на 4–9 % (Fe 0.948, Cu 0.956, Zn 0.959 от измеренного),
а выше Z = 50 сходится на 0.3–0.5 % (I 1.004, Cs 1.005). Подробности и числа —
`database/omega-vs-measurement-2026-08-09.md`.

Две поставки заносятся ОБЕ и порознь, потому что они не копии друг друга
(медиана отношения 1.0009, но разброс 0.949…1.839):

  xraylib  `fluor_yield.dat` — Krause, Nestor, Sparks, Ricci, ORNL-5399 (1978),
           часть значений заменена измерениями Campbell-2009 (L1 переиздан),
           Ayri-2021 (W, Re), Kaur-2021 (Sn, Sb). Z = 1…98, K без пропусков.
  xraydb   `xraydb.sqlite`, `xray_levels.fluorescence_yield` — свод
           Elam, Ravel, Sieber (2002) поверх Krause-1979. Public domain.

Подгонок здесь нет и быть не должно: сетка целочисленная и полная,
интерполировать нечего (урок §5г того же журнала).

Таблица общая по оболочкам (K, L1…L3, M1…M5, …) — потребитель сегодня есть
только у K, остальное лежит для N17: заводя L-флуоресценцию, ω_L1 надо брать
отсюда, а НЕ из EADL, где он занижен вдвое на тяжёлых (W 0.533, Pb 0.767).

Запись в базу — ТОЛЬКО с `--apply` (приказ Amber 09.08.2026, `TODO.md`, шапка).
Без него импортёр всё разбирает, всё считает и всё печатает, но файла не
трогает: сначала числа, потом решение.

    python import_fluor_yield.py <matdb.sqlite> <fluor_yield.dat> <xraydb.sqlite>
                                 [--apply]
"""

import io
import os
import sqlite3
import sys


SCHEMA = """
drop table if exists fluorescence_yield;
drop table if exists fluorescence_k;

-- Полная запись про K-флуоресценцию элемента — то же, что `xray_fluorescence`,
-- но НЕ обрывающееся на Z = 30. Старая таблица считала энергии линий по
-- разности краёв XCOM, а пары L2/L3 ниже Z = 30 там нет, и железа, меди,
-- кальция в ней поэтому не было ВОВСЕ: измеренный выход, положенный поверх,
-- до них просто не доходил (найдено пробой `OmegaProbe`, 09.08.2026).
--
-- Источники названы в записи порознь, потому что запись СМЕШАННАЯ, как и у
-- `xray_fluorescence` (правило «каждому своё» и его оговорённое исключение,
-- database/scheme.md §0а): выход — из лучше обновляемой поставки xraylib,
-- энергии линий, край и скачок — из xraydb, где они лежат одной таблицей.
create table fluorescence_k (
    z            integer primary key,
    k_edge_ev    real not null,
    k_fraction   real not null,   -- доля фотопоглощения на K, из скачка (r−1)/r
    omega_k      real not null,
    ka1_ev       real not null,   -- K-L3
    ka1_weight   real not null,
    ka2_ev       real not null,   -- K-L2
    ka2_weight   real not null,
    kb_ev        real not null,   -- K-M*, взвешенное среднее
    kb_weight    real not null,
    omega_source text not null,
    line_source  text not null
);

-- Выход флуоресценции: вероятность того, что атом ответит на дырку в оболочке
-- квантом, а не оже-электроном. Источник назван в каждой строке ЯВНО, потому
-- что поставки расходятся между собой, и молча смешивать их нельзя (правило
-- «каждому своё», database/scheme.md §0а).
create table fluorescence_yield (
    z      integer not null,
    shell  text    not null,   -- 'K', 'L1'..'L3', 'M1'..'M5', ...
    omega  real    not null,   -- 0..1
    source text    not null,   -- 'xraylib' | 'xraydb'
    primary key (z, shell, source)
);
"""


def read_xraylib(path):
    """`fluor_yield.dat`: три поля в строке — Z, оболочка, ω.

    ⚠ Файл состоит из ДВУХ блоков, и это не опечатка поставки. Сначала идёт
    исходная таблица Krause ORNL-5399 (1978) по всем оболочкам, а со строки
    1458 — блок ЗАМЕН: K для Z = 3…98, L1 для тридцати элементов, L2/L3 для
    четырёх. Это и есть те самые новые измерения, о которых пишет
    `doc/xraydoc.txt` — Campbell-2009 (L1 переиздан), Ayri-2021 (W, Re),
    Kaur-2021 (Sn, Sb). Расходятся блоки заметно: у лития 9.0e-5 против
    2.928e-4, втрое.

    Поэтому берётся ПОСЛЕДНЕЕ вхождение ключа, и берётся НАРОЧНО, а не как
    придётся: `insert or replace` дал бы тот же ответ молча, и первый же
    читатель, посчитавший строки, увидел бы 192 элемента в K вместо 96.
    """
    last = {}
    order = []
    overridden = 0
    for line in io.open(path, encoding="utf-8"):
        parts = line.split()
        if len(parts) != 3:
            continue
        z, shell, omega = int(parts[0]), parts[1], float(parts[2])
        if not (0 < omega <= 1.0):
            continue
        key = (z, shell)
        if key in last:
            overridden += 1
        else:
            order.append(key)
        last[key] = omega

    print("xraylib: ключей %d, перекрыто блоком замен %d" % (len(order), overridden))
    return [(z, shell, last[(z, shell)], "xraylib") for z, shell in order]


def read_xraydb(path):
    """`xray_levels`: символ элемента переводится в Z таблицей `elements`."""
    con = sqlite3.connect("file:%s?mode=ro" % path.replace("\\", "/"), uri=True)
    zof = dict(con.execute("select element, atomic_number from elements"))
    rows = []
    for el, shell, omega in con.execute(
            "select element, iupac_symbol, fluorescence_yield from xray_levels"):
        if el in zof and omega is not None and 0 < omega <= 1.0:
            rows.append((zof[el], shell, float(omega), "xraydb"))
    con.close()
    return rows


def build_k_records(xraydb_path, omega_xraylib):
    """Полная K-запись на элемент: край и скачок, три линии, выход.

    Линии сворачиваются в ту же тройку, что понимает расчёт: Kα1 = K-L3,
    Kα2 = K-L2, Kβ = все K-M* одним номером (их разнести детектор всё равно
    не может — у железа Kb1 и Kb3 стоят на одной энергии 7059.3 эВ).
    Веса нормируются на сумму этой тройки: расчёт разыгрывает выбор ЛИНИИ уже
    после того, как решил, что квант вылетел, — доля K-серии в ней не участвует.
    """
    con = sqlite3.connect("file:%s?mode=ro" % xraydb_path.replace("\\", "/"), uri=True)
    zof = dict(con.execute("select element, atomic_number from elements"))

    edges = {}
    for el, edge, jump in con.execute(
            "select element, absorption_edge, jump_ratio from xray_levels"
            " where iupac_symbol = 'K'"):
        if el in zof and edge and jump and jump > 1.0:
            edges[zof[el]] = (float(edge), (float(jump) - 1.0) / float(jump))

    lines = {}
    for el, iupac, energy, intensity in con.execute(
            "select element, iupac_symbol, emission_energy, intensity"
            " from xray_transitions where iupac_symbol like 'K-%'"):
        if el not in zof or not energy or not intensity:
            continue
        z = zof[el]
        a1, a2, kb_e, kb_w = lines.get(z, (None, None, 0.0, 0.0))
        if iupac == "K-L3":
            a1 = (float(energy), float(intensity))
        elif iupac == "K-L2":
            a2 = (float(energy), float(intensity))
        elif iupac.startswith("K-M"):
            # взвешенное среднее по мере накопления
            kb_e = (kb_e * kb_w + float(energy) * float(intensity)) / (kb_w + float(intensity))
            kb_w += float(intensity)
        lines[z] = (a1, a2, kb_e, kb_w)
    con.close()

    rows = []
    for z in sorted(set(edges) & set(lines) & set(omega_xraylib)):
        a1, a2, kb_e, kb_w = lines[z]
        if a1 is None or a2 is None or not (kb_w > 0.0):
            continue        # неполная серия — запись не заводим
        total = a1[1] + a2[1] + kb_w
        edge_ev, k_fraction = edges[z]
        rows.append((z, edge_ev, k_fraction, omega_xraylib[z],
                     a1[0], a1[1] / total, a2[0], a2[1] / total, kb_e, kb_w / total,
                     "xraylib", "xraydb"))
    return rows


def main():
    args = [a for a in sys.argv[1:] if a != "--apply"]
    apply = "--apply" in sys.argv
    if len(args) != 3:
        sys.exit("usage: import_fluor_yield.py <matdb.sqlite>"
                 " <fluor_yield.dat> <xraydb.sqlite> [--apply]")

    db_path, fluor_path, xraydb_path = args
    for path in (db_path, fluor_path, xraydb_path):
        if not os.path.exists(path):
            sys.exit("нет файла: %s" % path)

    rows = read_xraylib(fluor_path) + read_xraydb(xraydb_path)
    if not rows:
        sys.exit("обе поставки дали ноль строк — проверьте пути")

    omega_k = dict((z, omega) for z, shell, omega, src in rows
                   if shell == "K" and src == "xraylib")
    krows = build_k_records(xraydb_path, omega_k)

    # Без --apply база открывается на ЧТЕНИЕ: посчитать и напечатать можно
    # всегда, записать — только по решению. Числа ниже от этого не зависят,
    # они собраны из поставок, а не из базы.
    if apply:
        db = sqlite3.connect(db_path)
        db.executescript(SCHEMA)
        db.executemany(
            "insert or replace into fluorescence_yield (z, shell, omega, source)"
            " values (?, ?, ?, ?)", rows)
        db.executemany(
            "insert or replace into fluorescence_k values (?,?,?,?,?,?,?,?,?,?,?,?)", krows)
        db.commit()
    else:
        db = sqlite3.connect("file:%s?mode=ro" % db_path.replace("\\", "/"), uri=True)

    old = dict(db.execute("select z, ka1_ev from xray_fluorescence"))
    print("K-записей: %d (Z %d…%d); в старой `xray_fluorescence` — %d (от Z=%d)"
          % (len(krows), min(r[0] for r in krows), max(r[0] for r in krows),
             len(old), min(old) if old else 0))
    print("  новых элементов, которых у расчёта не было: %d"
          % len([r for r in krows if r[0] not in old]))
    for z, name in ((26, "Fe"), (29, "Cu"), (30, "Zn")):
        r = [x for x in krows if x[0] == z]
        if r:
            r = r[0]
            print("  %-3s край %8.1f эВ, доля K %.4f, Kα1 %8.1f (%.3f), Kβ %8.1f (%.3f)%s"
                  % (name, r[1], r[2], r[4], r[5], r[8], r[9],
                     "" if z in old else "   <- НЕ БЫЛО"))
    # сверка энергий там, где старая таблица есть: расходиться они не должны
    both = [(r[0], r[4], old[r[0]]) for r in krows if r[0] in old]
    if both:
        worst = max(both, key=lambda t: abs(t[1] / t[2] - 1.0))
        print("  Kα1 новая против старой на %d общих: худшее Z=%d, %.1f против %.1f эВ (%.2f %%)"
              % (len(both), worst[0], worst[1], worst[2],
                 100.0 * (worst[1] / worst[2] - 1.0)))
    print()

    # --- отчёт по СОБРАННОМУ, а не по записанному: без --apply в базе этого
    #     ещё нет, а числа должны печататься одни и те же в обоих режимах ---
    print("%s строк: %d" % ("занесено" if apply else "СОБРАНО (не записано)", len(rows)))
    sources = sorted({r[3] for r in rows})
    for source in sources:
        zs = sorted(r[0] for r in rows if r[1] == "K" and r[3] == source)
        print("  %-8s K: %d элементов, Z %d…%d" % (source, len(zs), min(zs), max(zs)))

    print("\nполнота сетки K (пропуски внутри диапазона):")
    for source in sources:
        zs = sorted(r[0] for r in rows if r[1] == "K" and r[3] == source)
        gaps = sorted(set(range(min(zs), max(zs) + 1)) - set(zs))
        print("  %-8s %s" % (source, "нет" if not gaps else gaps))

    print("\nнаша EADL-сумма против %s (K):" % ("занесённого" if apply else "собранного"))
    ours = dict(db.execute(
        "select z, sum(probability) from eadl_radiative"
        " where vacancy_shell=1 group by z"))
    for z, name in ((26, "Fe"), (29, "Cu"), (30, "Zn"), (53, "I"), (55, "Cs")):
        got = dict((r[3], r[2]) for r in rows if r[0] == z and r[1] == "K")
        if z in ours and "xraylib" in got:
            print("  %-3s Z=%-3d EADL %.5f  xraylib %.5f  xraydb %s  EADL/изм %.4f"
                  % (name, z, ours[z], got["xraylib"],
                     ("%.5f" % got["xraydb"]) if "xraydb" in got else "-",
                     ours[z] / got["xraylib"]))
    db.close()


if __name__ == "__main__":
    main()
