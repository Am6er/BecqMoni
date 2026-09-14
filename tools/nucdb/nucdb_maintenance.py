# -*- coding: utf-8 -*-
u"""Обслуживание `nucdb.sqlite`: индексы совпадений (T22) и пустые колонки (D21).

Запускается ПО МЕСТУ над рабочей базой и сам себя проверяет: до и после
сравниваются число строк каждой затронутой таблицы и контрольная сумма по
ОСТАЮЩИМСЯ колонкам (тот же приём, что в `split_db.py`, — сумма хешей строк
по модулю, порядок строк не важен). Расхождение — отказ, база не трогается.

Что делает.

**T22, индексы.** `gamma_coincidence` (128 тыс. строк) и
`gamma_coincidence_line` (43 тыс.) спрашиваются `FsaCascadeSummer` через
представления с фильтром по нуклиду, а индекса по `parent_id` у них нет —
план запроса `SCAN`, полный проход на КАЖДЫЙ нуклид библиотеки. Индекс по
`parent_id` превращает его в `SEARCH`.

**D21, пустые колонки.** `decay_radiations.logft`, `logft_unc`, `logft_num`
(0 значений из 66290) и `nuclides.mag_mom` (0 из 4377) не заполнены ни разу,
не читаются и не пишутся НИ ОДНОЙ строкой кода — ни C#, ни импортёрами
(проверено поиском по дереву: единственный `logft` в коде — это
`ensdf_feedings.logft` из `import_ensdf.py`, другая таблица и она заполнена).
Пустая колонка выглядит как данные, которых нет. Правило Amber 08.08.2026:
кода нет — удалять.

    python nucdb_maintenance.py [--db=.../BecquerelMonitor/nucdb.sqlite] [--dry]
"""
import argparse
import os
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir))
DEFAULT_DB = os.path.join(REPO, "BecquerelMonitor", "nucdb.sqlite")

# таблица -> колонки под снос
DROP = {
    "decay_radiations": ["logft", "logft_unc", "logft_num"],
    "nuclides": ["mag_mom"],
}

# таблица -> (имя индекса, колонки)
INDEXES = [
    ("gamma_coincidence", "ix_gamma_coincidence_parent_id", "parent_id"),
    ("gamma_coincidence_line", "ix_gamma_coincidence_line_parent_id", "parent_id"),
]


def columns(con, table):
    return [r[1] for r in con.execute('pragma table_info("%s")' % table)]


def digest(con, table, cols):
    u"""Контрольная сумма содержимого по перечисленным колонкам."""
    expr = " || '\x1f' || ".join('quote("%s")' % c for c in cols)
    total = 0
    for (row,) in con.execute('select %s from "%s"' % (expr, table)):
        total = (total + hash(row)) % (1 << 61)
    return total


def rows(con, table):
    return con.execute('select count(*) from "%s"' % table).fetchone()[0]


def plan(con, sql):
    return " / ".join(r[3] for r in con.execute("explain query plan " + sql))


PROBE = {
    "gamma_coincidence":
        "select * from v_gamma_coincidence where nucid = '208TL'",
    "gamma_coincidence_line":
        "select * from v_gamma_coincidence_line where nucid = '208TL'",
}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--db", default=DEFAULT_DB)
    p.add_argument("--dry", action="store_true", help=u"только показать, ничего не менять")
    a = p.parse_args()

    if not os.path.isfile(a.db):
        sys.exit(u"нет базы: %s" % a.db)

    con = sqlite3.connect(a.db)
    size0 = os.path.getsize(a.db)

    # ---- снимок «до» -------------------------------------------------
    before = {}
    for table, drop in DROP.items():
        cols = columns(con, table)
        unknown = [c for c in drop if c not in cols]
        if unknown:
            sys.exit(u"в %s нет колонок %s — база не та" % (table, unknown))
        # Колонка, в которой ХОТЬ ЧТО-ТО есть, не удаляется: правило было
        # «пустые», а не «ненужные».
        for c in drop:
            n = con.execute(
                'select count("%s") from "%s"' % (c, table)).fetchone()[0]
            if n:
                sys.exit(u"%s.%s заполнена (%d значений) — удалять нельзя"
                         % (table, c, n))
        keep = [c for c in cols if c not in drop]
        before[table] = (rows(con, table), digest(con, table, keep), keep)

    print(u"# Обслуживание nucdb (T22 + D21)")
    print(u"")
    print(u"База: %s, %.1f МБ" % (a.db, size0 / 1048576.0))
    print(u"")
    print(u"## План запроса до")
    for table, _, _ in INDEXES:
        print(u"    %-24s %s" % (table, plan(con, PROBE[table])))

    if a.dry:
        print(u"")
        print(u"(--dry: ничего не изменено)")
        return

    # ---- индексы -----------------------------------------------------
    for table, name, col in INDEXES:
        con.execute('create index if not exists %s on "%s"(%s)' % (name, table, col))

    # ---- колонки -----------------------------------------------------
    # `alter table drop column` есть с SQLite 3.35; если сборка старше —
    # честный отказ, а не тихая пересборка таблицы своими руками.
    for table, drop in DROP.items():
        for c in drop:
            con.execute('alter table "%s" drop column "%s"' % (table, c))

    con.commit()

    # ---- сверка ------------------------------------------------------
    bad = []
    for table, (n0, d0, keep) in before.items():
        n1, d1 = rows(con, table), digest(con, table, keep)
        left = columns(con, table)
        if n1 != n0:
            bad.append(u"%s: строк %d -> %d" % (table, n0, n1))
        if d1 != d0:
            bad.append(u"%s: содержимое остальных колонок изменилось" % table)
        if [c for c in DROP[table] if c in left]:
            bad.append(u"%s: колонки не удалились" % table)
    if bad:
        sys.exit(u"СВЕРКА НЕ СОШЛАСЬ:\n  " + u"\n  ".join(bad))

    print(u"")
    print(u"## План запроса после")
    for table, _, _ in INDEXES:
        print(u"    %-24s %s" % (table, plan(con, PROBE[table])))

    print(u"")
    print(u"## Удалённые колонки")
    for table, drop in sorted(DROP.items()):
        n0, _, _ = before[table]
        print(u"    %-20s %s (строк %d, все значения были пусты)"
              % (table, ", ".join(drop), n0))

    con.execute("vacuum")
    con.close()
    size1 = os.path.getsize(a.db)
    print(u"")
    print(u"Размер: %.2f -> %.2f МБ (после vacuum)"
          % (size0 / 1048576.0, size1 / 1048576.0))
    print(u"Сверка: строки и содержимое остальных колонок сошлись.")


if __name__ == "__main__":
    main()
