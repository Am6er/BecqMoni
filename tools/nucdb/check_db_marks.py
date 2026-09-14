# -*- coding: utf-8 -*-
u"""Сторож НАШИХ колонок-признаков в базах: они обязаны сходиться с правилом.

Судит две колонки, заведённые 10.09.2026 по прямому разрешению Amber:

* `nucdb.nuclides.half_life_is_limit` (`D36`) — «в `half_life_sec` лежит
  ПРЕДЕЛ «>300 нс», а не измерение»; 81 строка;
* `schemedb.ensdf_datasets.parent_l_seqno` (`D38`) — номер уровня родителя
  набора ENSDF, там где имя изомера не помечает; 662 строки.

⛔ ЗАЧЕМ СТОРОЖ, А НЕ «ОДИН РАЗ ЗАПИСАЛИ И ХВАТИТ». Признак, лежащий в базе, —
ДАННЫЕ, и они расходятся с правилом молча: переписали поставку, пометили руками,
прогнали инструмент наполовину, пересобрали базу из импортёра — колонка при этом
не исчезает, она остаётся СТАРОЙ. Повторяющаяся беда этого дерева — «признак
заведён, потребитель не написан»: сторож и есть потребитель. ⚠ Отдельно важно
для `D36`: числа хвоста сверки (`compare_copies.py`, пара 4) подлога НЕ ловят —
измерено 10.09.2026, подложенные `105TE` и `122RH` не попали в сравниваемую
выборку и все числа пары 4а остались прежними до знака. Ловит только прямая
сверка колонки с правилом, то есть это.

Правило в обоих случаях считается заново, инструментом, который колонку и
ставил, — второй копии правила нет.

    python tools/nucdb/check_db_marks.py [--nucdb ...] [--schemedb ...]

Коды возврата: 0 — обе колонки сошлись с правилом; 1 — расхождение или колонки
нет вовсе (колонка была заведена решением Amber, её пропажа — тоже отказ).
"""
import argparse
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import mark_half_life_limits as d36                       # noqa: E402
import mark_isomer_datasets as d38                        # noqa: E402

ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite")
DEFAULT_SCHEMEDB = os.path.join(ROOT, "BecquerelMonitor", "schemedb.sqlite")

#: Числа, на которых колонки заведены (разрешение Amber 10.09.2026). Сторож
#: судит не только «сошлось с правилом», но и «столько же, сколько разрешено»:
#: правило, изменившееся вместе с колонкой, сошлось бы само с собой.
EXPECT_D36 = 81
EXPECT_D38 = 662

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    a = p.parse_args()

    w(u"# Колонки-признаки в базах против правила (`D36`, `D38`)")
    failures = []

    # ── D36 ──────────────────────────────────────────────────────────────
    nuc = d36.open_db(a.nucdb, False)
    rule36 = len(d36.round_limit_rows(nuc))
    # ⛔ У `mark_half_life_limits` СВОЙ буфер на том же дескрипторе: без этих
    # двух сбросов его печать вылезает в конец отчёта, и приговор оказывается
    # оторван от того, что его вызвало.
    OUT.flush()
    code36 = d36.verify(nuc)
    d36.OUT.flush()
    w(u"")
    w(u"1. `nuclides.half_life_is_limit` (`D36`): по правилу %d, разрешено %d",
      rule36, EXPECT_D36)
    if code36:
        failures.append(u"`half_life_is_limit` разошлась с правилом")
    if rule36 != EXPECT_D36:
        w(u"   ⛔ ПРАВИЛО ДАЁТ ДРУГОЕ ЧИСЛО: %d вместо разрешённых %d —"
          u" поставка изменилась, решение Amber нужно перезадать",
          rule36, EXPECT_D36)
        failures.append(u"правило `D36` даёт %d вместо %d" % (rule36, EXPECT_D36))

    # ── D38 ──────────────────────────────────────────────────────────────
    nuc38 = d38.open_ro(a.nucdb)
    sch = d38.open_ro(a.schemedb)
    rows = d38.load_rows(sch)
    _, would = d38.decide(d38.load_levels(nuc38), rows, 0.01)
    code38 = max(d38.control(OUT, rows, would), d38.verify_column(OUT, sch, would))
    w(u"")
    w(u"2. `ensdf_datasets.parent_l_seqno` (`D38`): по правилу %d, разрешено %d",
      len(would), EXPECT_D38)
    if code38:
        failures.append(u"`parent_l_seqno` разошлась с правилом или контролем")
    if len(would) != EXPECT_D38:
        w(u"   ⛔ ПРАВИЛО ДАЁТ ДРУГОЕ ЧИСЛО: %d вместо разрешённых %d —"
          u" поставка изменилась, решение Amber нужно перезадать",
          len(would), EXPECT_D38)
        failures.append(u"правило `D38` даёт %d вместо %d"
                        % (len(would), EXPECT_D38))

    w(u"")
    if failures:
        w(u"ОТКАЗ: " + u"; ".join(failures))
        OUT.flush()
        return 1
    w(u"ОБЕ КОЛОНКИ СОШЛИСЬ с правилом и с разрешёнными числами (%d и %d).",
      EXPECT_D36, EXPECT_D38)
    OUT.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())
