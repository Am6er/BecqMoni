# -*- coding: utf-8 -*-
u"""Гигиена ядерной части базы: D15, D16, D17, D20, D22.

Продолжение `nucdb_maintenance.py` (там индексы совпадений и пустые колонки).
Разнесено на два файла потому, что тот шаг уже применён к базе, а этот трогает
СОДЕРЖИМОЕ и должен читаться отдельно.

Каждый шаг ИДЕМПОТЕНТЕН (повторный запуск ничего не делает) и сам себя
проверяет; ни один не выполняется, если сверка до него не сошлась.

**D17.** Тестовая заглушка ЛСРМ `290XX / 'Fake ENSDF2 B- DECAY'` в двух
экземплярах (`290.ENX`, `291.ENX`) и всё, что за ней. Нуклида 290XX не
существует; в базе он выглядит как данные.

**D22.** Строки `ensdf_gammas`, совпадающие ПОБИТОВО (все колонки, кроме `id`).
Разные записи на одной энергии не трогаются — там надо решать, складывать или
выбирать, а это правка потребителя, не данных.

**D15.** То же для `decay_radiations` (все колонки, кроме `dr_pk`).

**D16.** Две пары строк `nuclides` с одинаковыми (`nucid`, `l_seqno` = 0):
`161PM` и `35NA`. В каждой паре одна строка без спина, вторая со спином —
остаётся вторая (решение Amber 08.08.2026).

**D20.** Сироты: 48 значений `decay_radiations.parent_nucid`, которых нет в
`nuclides`. Строки достраиваются ИЗ НАШИХ ЖЕ баз и без единой догадки:
номер уровня берётся у самой `decay_radiations` (`parent_l_seqno` — супплай сам
его назвал), а энергия, период и спин этого уровня — из `g4_level` по (Z, A,
seq). Проверено на известных: 99TCm → уровень 2, 142.68 кэВ, 21625.9 с
(6.007 ч), 85KRm → 304.87 кэВ, 4.48 ч, 91NBm1 → 104.6 кэВ, 60.9 сут.

    Правило «m = первый долгоживущий уровень» ОТВЕРГНУТО измерением: на 1193
    метастабильных, которые в `nuclides` уже есть, оно воспроизводит номер
    уровня в лучшем случае у 72 % (порог 100 нс) — четверть строк вышла бы
    неверной. Поэтому номер не угадывается, а читается.

    python db_hygiene.py [--nucdb=...] [--schemedb=...] [--dry]
"""
import argparse
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
DEFAULT_NUCDB = os.path.join(REPO, "BecquerelMonitor", "nucdb.sqlite")
DEFAULT_SCHEMEDB = os.path.join(REPO, "BecquerelMonitor", "schemedb.sqlite")

FAKE_NUCID = "290XX"
D16_PAIRS = ("161PM", "35NA")

NUCID_RE = re.compile(r"^(\d+)([A-Za-z]{1,2}?)(m\d*)?$")


def rows(con, sql, args=()):
    return list(con.execute(sql, args))


def one(con, sql, args=()):
    return con.execute(sql, args).fetchone()[0]


def columns(con, table):
    return [r[1] for r in con.execute('pragma table_info("%s")' % table)]


# ------------------------------------------------------------------ D17

def step_fake_dataset(scheme, dry):
    ids = [r[0] for r in scheme.execute(
        "select id from ensdf_datasets where nucid = ?", (FAKE_NUCID,))]
    if not ids:
        print(u"D17: заглушки нет — уже снята")
        return
    holder = ",".join(str(i) for i in ids)
    counts = {t: one(scheme, "select count(*) from %s where dataset_id in (%s)" % (t, holder))
              for t in ("ensdf_levels", "ensdf_gammas", "ensdf_feedings")}
    print(u"D17: наборов %d, за ними уровней %d, гамма %d, питаний %d"
          % (len(ids), counts["ensdf_levels"], counts["ensdf_gammas"],
             counts["ensdf_feedings"]))
    if dry:
        return
    for t in ("ensdf_levels", "ensdf_gammas", "ensdf_feedings"):
        scheme.execute("delete from %s where dataset_id in (%s)" % (t, holder))
    scheme.execute("delete from ensdf_datasets where id in (%s)" % holder)
    left = one(scheme, "select count(*) from ensdf_datasets where nucid = ?", (FAKE_NUCID,))
    if left:
        sys.exit(u"D17: заглушка не удалилась")
    for t in ("ensdf_levels", "ensdf_gammas", "ensdf_feedings"):
        if one(scheme, "select count(*) from %s where dataset_id in (%s)" % (t, holder)):
            sys.exit(u"D17: за заглушкой остались строки в %s" % t)
    print(u"     снята")


# ------------------------------------------------------- D15 и D22 (дубли)

def step_exact_duplicates(con, table, key, label, dry):
    u"""Снести строки, совпадающие по всем колонкам, КРОМЕ ключа и полей
    неопределённости (`*_unc`).

    Строгое сравнение по всем колонкам нашло бы ноль дублей в
    `decay_radiations` и половину в `ensdf_gammas`: у настоящих дублей расходится
    ровно неопределённость — `energy_unc` = 2 против 7, `intensity_unc` = 6
    против 'LT'. Это одна и та же линия, пришедшая из двух оценок, и потребитель
    (`NucBaseFramework` собирает линии без группировки) отдаёт её выход ДВАЖДЫ.
    Неопределённостью в расчёте не пользуется никто.

    Из группы остаётся строка с ЧИСЛОВОЙ неопределённостью выхода: 'LT' — это
    верхний предел, и он беднее числа. При равенстве — наименьший ключ, чтобы
    выбор был воспроизводим."""
    unc = [c for c in columns(con, table) if c.endswith("_unc")]
    cols = [c for c in columns(con, table) if c != key and c not in unc]
    group = ", ".join('"%s"' % c for c in cols)
    # Порядок предпочтения внутри группы: числовая неопределённость выхода
    # раньше словесной и пустой.
    rank = ("case when %s then 0 else 1 end" %
            " and ".join('"%s" glob "*[0-9]*"' % c for c in unc)) if unc else "0"
    total_before = one(con, 'select count(*) from "%s"' % table)
    extra = one(con,
                'select ifnull(sum(c - 1), 0) from (select count(*) c from "%s"'
                ' group by %s having c > 1)' % (table, group))
    print(u"%s: %s — строк %d, дублей (без учёта %s) %d"
          % (label, table, total_before, "/".join(unc) or u"—", extra))
    if not extra or dry:
        return
    con.execute('delete from "%s" where "%s" not in'
                ' (select "%s" from (select "%s", row_number() over'
                '   (partition by %s order by %s, "%s") rn from "%s") where rn = 1)'
                % (table, key, key, key, group, rank, key, table))
    total_after = one(con, 'select count(*) from "%s"' % table)
    left = one(con,
               'select ifnull(sum(c - 1), 0) from (select count(*) c from "%s"'
               ' group by %s having c > 1)' % (table, group))
    if left or total_after != total_before - extra:
        sys.exit(u"%s: сверка не сошлась (стало %d, ждали %d, дублей осталось %d)"
                 % (label, total_after, total_before - extra, left))
    print(u"     снято %d, осталось %d" % (extra, total_after))


# ------------------------------------------------------------------ D16

def step_duplicate_nuclides(nuc, dry):
    for nucid in D16_PAIRS:
        got = rows(nuc, "select pk, jp, half_life, half_life_unit from nuclides"
                        " where nucid = ? and l_seqno = 0", (nucid,))
        if len(got) < 2:
            print(u"D16: %s — уже одна строка" % nucid)
            continue
        with_jp = [g for g in got if g[1]]
        if len(with_jp) != 1:
            sys.exit(u"D16: у %s строк со спином %d — правило не применимо"
                     % (nucid, len(with_jp)))
        keep = with_jp[0]
        drop = [g for g in got if g[0] != keep[0]]
        print(u"D16: %s — остаётся pk=%s (%s, %s %s), сносится %s"
              % (nucid, keep[0], keep[1], keep[2], keep[3],
                 [(g[0], g[2], g[3]) for g in drop]))
        if dry:
            continue
        nuc.executemany("delete from nuclides where pk = ?", [(g[0],) for g in drop])
        if one(nuc, "select count(*) from nuclides where nucid = ? and l_seqno = 0",
               (nucid,)) != 1:
            sys.exit(u"D16: %s — осталось не одна строка" % nucid)


# ------------------------------------------------------------------ D20

def jpi_to_text(value):
    u"""Спин-чётность из числа Geant4 в запись базы: -5.5 -> '11/2-'."""
    if value is None:
        return None
    sign = "-" if value < 0 else "+"
    j = abs(value)
    if abs(j * 2 - round(j * 2)) > 1e-6:
        return None
    twice = int(round(j * 2))
    return ("%d/2" % twice if twice % 2 else "%d" % (twice // 2)) + sign


UNITS = [("Y", 31556952.0), ("d", 86400.0), ("h", 3600.0),
         ("m", 60.0), ("s", 1.0), ("ms", 1e-3), ("us", 1e-6), ("ns", 1e-9)]


def seconds_to_text(sec):
    for unit, scale in UNITS:
        if sec >= scale:
            return ("%.4g" % (sec / scale)), unit
    return ("%.4g" % (sec / 1e-9)), "ns"


def step_orphans(nuc, scheme, dry):
    symbols = {}
    for z, sym in scheme.execute("select 0, 0 where 0"):    # placeholder
        pass
    for z, sym in nuc.execute("select distinct z, upper(symbol) from nuclides"
                              " where symbol is not null"):
        symbols[sym] = z
    proper = {}
    for z, sym in nuc.execute("select distinct z, symbol from nuclides"
                              " where symbol is not null"):
        proper[z] = sym

    levels = {}
    for z, a, seq, ev, hl, jpi in scheme.execute(
            "select z, a, seq, energy_ev, half_life_sec, jpi from g4_level"):
        levels[(z, a, seq)] = (ev, hl, jpi)

    # Сироты обеих связей сразу. У родителей `decay_radiations` номер уровня
    # назван самим супплаем; у дочек `decay_chain` его нет — они всегда
    # основное состояние (дочка цепочки не изомер), поэтому уровень 0.
    orphans = [(r[0], None) for r in nuc.execute(
        "select distinct parent_nucid from decay_radiations d where not exists"
        " (select 1 from nuclides x where x.nucid = d.parent_nucid) order by 1")]
    orphans += [(r[0], 0) for r in nuc.execute(
        "select distinct daughter_nucid from decay_chain d"
        " where daughter_nucid is not null and not exists"
        " (select 1 from nuclides x where x.nucid = d.daughter_nucid) order by 1")]
    if not orphans:
        print(u"D20: сирот нет — уже залатано")
        return

    next_pk = one(nuc, "select max(cast(pk as integer)) from nuclides") + 1
    made, skipped = [], []
    for nucid, fixed_seq in orphans:
        m = NUCID_RE.match(nucid)
        z = symbols.get(m.group(2).upper()) if m else None
        if z is None:
            skipped.append((nucid, u"не разобрано имя"))
            continue
        a = int(m.group(1))
        seqs = ([(fixed_seq,)] if fixed_seq is not None
                else nuc.execute("select distinct parent_l_seqno from decay_radiations"
                                 " where parent_nucid = ? order by 1", (nucid,)))
        for (seq,) in seqs:
            key = (z, a, seq)
            if key not in levels:
                skipped.append((nucid, u"уровня %s нет в g4_level" % seq))
                continue
            ev, hl, jpi = levels[key]
            text, unit = seconds_to_text(hl) if hl and hl > 0 else (None, None)
            made.append((nucid, z, a - z, proper.get(z), seq, jpi_to_text(jpi),
                         text, unit, hl if hl and hl > 0 else None,
                         str(next_pk), ev / 1000.0))
            next_pk += 1

    print(u"D20: сирот %d, строк к заведению %d, пропущено %d"
          % (len(orphans), len(made), len(skipped)))
    for r in made:
        print(u"     %-9s l_seqno=%-3d %8.2f кэВ  T=%s %s  J=%s"
              % (r[0], r[4], r[10], r[6], r[7] or "", r[5]))
    for nucid, why in skipped:
        print(u"     ПРОПУЩЕН %-9s %s" % (nucid, why))
    if dry or not made:
        return

    nuc.executemany(
        "insert into nuclides (nucid, z, n, symbol, l_seqno, jp, half_life,"
        " half_life_unit, half_life_sec, pk) values (?,?,?,?,?,?,?,?,?,?)",
        [r[:10] for r in made])
    left = one(nuc, "select count(distinct parent_nucid) from decay_radiations d"
                    " where not exists (select 1 from nuclides x"
                    "                   where x.nucid = d.parent_nucid)")
    left += one(nuc, "select count(distinct daughter_nucid) from decay_chain d"
                     " where daughter_nucid is not null and not exists"
                     " (select 1 from nuclides x where x.nucid = d.daughter_nucid)")
    print(u"     заведено %d, сирот осталось %d" % (len(made), left))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    p.add_argument("--dry", action="store_true")
    a = p.parse_args()

    for path in (a.nucdb, a.schemedb):
        if not os.path.isfile(path):
            sys.exit(u"нет базы: %s" % path)

    nuc = sqlite3.connect(a.nucdb)
    scheme = sqlite3.connect(a.schemedb)

    print(u"# Гигиена ядерной части (D15, D16, D17, D20, D22)")
    print(u"")
    print(u"nucdb    %.2f МБ" % (os.path.getsize(a.nucdb) / 1048576.0))
    print(u"schemedb %.2f МБ" % (os.path.getsize(a.schemedb) / 1048576.0))
    print(u"")

    step_fake_dataset(scheme, a.dry)
    step_exact_duplicates(scheme, "ensdf_gammas", "id", u"D22", a.dry)
    step_exact_duplicates(nuc, "decay_radiations", "dr_pk", u"D15", a.dry)
    step_duplicate_nuclides(nuc, a.dry)
    step_orphans(nuc, scheme, a.dry)

    if a.dry:
        print(u"")
        print(u"(--dry: ничего не изменено)")
        return

    nuc.commit()
    scheme.commit()
    nuc.execute("vacuum")
    scheme.execute("vacuum")
    nuc.close()
    scheme.close()
    print(u"")
    print(u"nucdb    %.2f МБ" % (os.path.getsize(a.nucdb) / 1048576.0))
    print(u"schemedb %.2f МБ" % (os.path.getsize(a.schemedb) / 1048576.0))


if __name__ == "__main__":
    main()
