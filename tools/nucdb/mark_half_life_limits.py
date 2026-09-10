# -*- coding: utf-8 -*-
u"""`D36`: пометить строки `nuclides`, у которых `half_life_sec` несёт ПРЕДЕЛ.

⛔ ЧТО ЗДЕСЬ ПРОИСХОДИТ. У части поставки в `half_life_sec` лежит не измеренный
период, а закодированная граница «>300 нс», и потребитель читает её как
настоящее число. Знака «>» в дереве не осталось нигде (перемерено 10.09.2026,
полоса П7: 0 из 4429 строк `nuclides`, 0 из 35 220 `ensdf_levels`), и вернуть
его нечем — восстановить признак можно только правилом по числам. Правило
живёт В ОДНОМ МЕСТЕ, `compare_copies.round_limit_rows`, и отсюда оно берётся
импортом, а не переписывается: вторая копия разошлась бы с первой молча.

⛔ ЗАПИСЬ ПО ПРЯМОМУ РАЗРЕШЕНИЮ AMBER 10.09.2026, И ТОЛЬКО НА НЕЁ. Общий приказ
09.08.2026 — агент не пишет в `nucdb.sqlite` без разрешения на САМУ ЗАПИСЬ;
согласованный МЕТОД разрешением не является. Разрешение дано вопросником,
дословно: **«Отдельная колонка-признак»** — то есть `alter table nuclides add
column half_life_is_limit integer` и пометка 81 строки. Числа были показаны при
вопросе: под правило подпадают 84 строки на четырёх круглых значениях, правилу
отвечает 81, три отсеиваются по непустой `half_life_unc` (`105TE` 0.62 us ± 7,
`32ALm`, `61TIm1` — это измерения, случайно севшие на круглое число).

⛔ `--apply` ВЫКЛЮЧЕН ПО УМОЛЧАНИЮ и остаётся таким — общее решение Amber
25.08.2026 по всей серии `D35`—`D38`. Без ключа база открывается `mode=ro`,
и написать в неё нечего физически.

ПРИЁМКА — ЧИСЛОМ. `--verify` сверяет колонку с правилом и возвращает 1 при
любом расхождении в любую сторону. Это же сравнение печатает `compare_copies`
при каждой паре 4, так что признак имеет читателя, а не лежит молча.

    python tools/nucdb/mark_half_life_limits.py            # числа, без записи
    python tools/nucdb/mark_half_life_limits.py --verify   # сверка, код 0/1
    python tools/nucdb/mark_half_life_limits.py --apply    # ЗАПИСЬ, в транзакции
"""
import argparse
import collections
import io
import os
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from compare_copies import (LIMIT_COLUMN, has_limit_column,  # noqa: E402
                            round_limit_rows)

ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite")

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")


def open_db(path, write):
    if not os.path.isfile(path):
        sys.exit(u"нет базы: %s" % path)
    if not write:
        return sqlite3.connect("file:%s?mode=ro" % path.replace(chr(92), "/"),
                               uri=True)
    return sqlite3.connect(path)


def numbers(db):
    u"""Что лежит в базе СЕЙЧАС: круглые значения, строки под правилом, отсев."""
    counts = collections.Counter()
    for (hl,) in db.execute("select half_life_sec from nuclides"
                            " where half_life_sec is not null"
                            " and half_life_sec < 1e-6"):
        counts[hl] += 1
    shared = sorted(((v, n) for v, n in counts.items() if n >= 8),
                    key=lambda x: -x[1])
    rows = round_limit_rows(db)
    on_shared = [r for r in db.execute(
        "select pk, nucid, half_life_sec, half_life_unc from nuclides"
        " where half_life_sec is not null and half_life_sec < 1e-6")
        if r[2] in set(v for v, _ in shared)]
    keep = set(r[0] for r in rows)
    dropped = [r for r in on_shared if r[0] not in keep]
    return shared, rows, dropped


def report_state(db, rows):
    u"""Сколько уже помечено и сколько останется пустыми."""
    if not has_limit_column(db):
        w(u"колонки `%s` в таблице НЕТ — помечено 0", LIMIT_COLUMN)
        return 0, 0
    marked = db.execute("select count(*) from nuclides where %s = 1"
                        % LIMIT_COLUMN).fetchone()[0]
    filled = db.execute("select count(*) from nuclides where %s is not null"
                        % LIMIT_COLUMN).fetchone()[0]
    total = db.execute("select count(*) from nuclides").fetchone()[0]
    w(u"колонка `%s` есть: помечено единицей %d, непустых всего %d,"
      u" останутся пустыми %d из %d",
      LIMIT_COLUMN, marked, filled, total - filled, total)
    return marked, filled


def verify(db):
    u"""Колонка против правила. Возвращает код возврата: 0 — сошлось."""
    mech = dict((r[0], r[1]) for r in round_limit_rows(db))
    if not has_limit_column(db):
        w(u"⛔ `D36`: колонки `%s` нет — сверять нечего (правилу отвечает %d)",
          LIMIT_COLUMN, len(mech))
        return 1
    col = dict((pk, nucid) for pk, nucid in db.execute(
        "select pk, nucid from nuclides where %s = 1" % LIMIT_COLUMN))
    only_col = sorted(col.items(), key=lambda x: x[0])
    only_col = [x for x in only_col if x[0] not in mech]
    only_mech = sorted([x for x in mech.items() if x[0] not in col],
                       key=lambda x: x[0])
    w(u"")
    w(u"## Сверка: колонка против механического правила")
    w(u"   в колонке %d, по правилу %d", len(col), len(mech))
    if not only_col and not only_mech:
        w(u"   СОШЛОСЬ до единицы")
        return 0
    if only_col:
        w(u"   ⛔ помечено, а правилу НЕ отвечает: %d — %s", len(only_col),
          u", ".join(u"%s (pk %s)" % (n, p) for p, n in only_col[:12]))
    if only_mech:
        w(u"   ⛔ правилу отвечает, а НЕ помечено: %d — %s", len(only_mech),
          u", ".join(u"%s (pk %s)" % (n, p) for p, n in only_mech[:12]))
    w(u"   РАСХОЖДЕНИЕ: признак в базе и правило говорят разное")
    return 1


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--apply", action="store_true",
                   help=u"ЗАПИСАТЬ (по умолчанию выключено, решение Amber"
                        u" 25.08.2026 по серии `D35`—`D38`)")
    p.add_argument("--verify", action="store_true",
                   help=u"только сверка колонки с правилом; код 1 при"
                        u" расхождении")
    p.add_argument("--show", type=int, default=10)
    a = p.parse_args()

    db = open_db(a.nucdb, a.apply)
    w(u"# `D36`: признак «в `half_life_sec` лежит ПРЕДЕЛ» — %s",
      u"ЗАПИСЬ" if a.apply else u"числа, без записи")
    w(u"")
    w(u"база: %s", a.nucdb)

    if a.verify:
        code = verify(db)
        OUT.flush()
        return code

    shared, rows, dropped = numbers(db)
    w(u"")
    w(u"## Числа")
    w(u"")
    w(u"круглых значений короче микросекунды, разделённых >= 8 нуклидами: %d",
      len(shared))
    total = 0
    for v, n in shared:
        w(u"   %-10g  %d строк", v, n)
        total += n
    w(u"   всего строк на этих значениях: %d", total)
    w(u"ПОД ПРАВИЛО (у самой строки нет `half_life_unc`): %d", len(rows))
    w(u"ОТСЕЯНО (неопределённость есть — значит это ИЗМЕРЕНИЕ): %d",
      len(dropped))
    for pk, nucid, hl, unc in dropped:
        w(u"   отсев: %-8s pk %-6s %-10g  half_life_unc = %s",
          nucid, pk, hl, unc)
    w(u"")
    marked, _ = report_state(db, rows)

    w(u"")
    w(u"## Что изменилось бы")
    w(u"")
    if has_limit_column(db):
        col = set(pk for (pk,) in db.execute(
            "select pk from nuclides where %s = 1" % LIMIT_COLUMN))
    else:
        col = set()
    add = [r for r in rows if r[0] not in col]
    w(u"`alter table nuclides add column %s integer`: %s",
      LIMIT_COLUMN, u"НУЖЕН" if not has_limit_column(db) else u"не нужен, есть")
    w(u"строк получит признак: **%d** (было помечено %d)", len(add), marked)
    w(u"")
    w(u"### Первые %d строк под пометку", a.show)
    w(u"")
    for pk, nucid, hl, unc in add[:a.show]:
        w(u"   pk %-6s %-8s half_life_sec = %-10g", pk, nucid, hl)

    if not a.apply:
        w(u"")
        w(u"⛔ ЗАПИСИ НЕ БЫЛО: база открыта `mode=ro`, ключ `--apply` не назван.")
        OUT.flush()
        return 0

    # ── ЗАПИСЬ ───────────────────────────────────────────────────────────
    # Одна транзакция: колонка и все пометки въезжают вместе или никак.
    w(u"")
    w(u"## ЗАПИСЬ (разрешение Amber 10.09.2026: «Отдельная колонка-признак»)")
    w(u"")
    try:
        db.execute("begin")
        if not has_limit_column(db):
            db.execute("alter table nuclides add column %s integer"
                       % LIMIT_COLUMN)
            w(u"   `alter table nuclides add column %s integer` — сделано",
              LIMIT_COLUMN)
        db.executemany("update nuclides set %s = 1 where pk = ?" % LIMIT_COLUMN,
                       [(r[0],) for r in add])
        changed = db.total_changes
        db.commit()
    except Exception:
        db.rollback()
        raise
    w(u"   помечено строк: %d (по `pk`, не по имени: `144TBm` в таблице трижды)",
      len(add))
    w(u"   `total_changes` соединения: %d", changed)

    code = verify(db)
    OUT.flush()
    return code


if __name__ == "__main__":
    sys.exit(main())
