# -*- coding: utf-8 -*-
u"""`D38`: соединение по `ensdf_datasets.parent_nucid` молча сводит изомера с
основным состоянием. Здесь считается, СКОЛЬКО строк и КАКИХ получило бы метку
изомера, если бы её ставили.

⛔ ЗАПИСЬ ПО ПРЯМОМУ РАЗРЕШЕНИЮ AMBER 10.09.2026, И ТОЛЬКО НА НЕЁ. Приказ Amber
09.08.2026: агент не пишет в `nucdb.sqlite` / `schemedb.sqlite` / `matdb.sqlite`
без разрешения на САМУ ЗАПИСЬ; согласованный МЕТОД разрешением не является.
Решение Amber 25.08.2026 по `D35`—`D38` то же: инструмент готовится с `--apply`
ВЫКЛЮЧЕННЫМ и приносит числа. Числа принесены, и 10.09.2026 вопросником дано
разрешение, дословно: **«Разрешаю: писать 662 строки»** — то есть `alter table
ensdf_datasets add column parent_l_seqno integer` и пометка 662 строк из 3450
(464 различных `parent_nucid`). Остальные остаются ПУСТЫМИ: 1976 — основное
состояние, 751 не сошлось по периоду, 20 не развести, 41 прочее.

⛔ `--apply` ВЫКЛЮЧЕН ПО УМОЛЧАНИЮ и остаётся таким — то же решение 25.08.2026
по всей серии. Без ключа обе базы открываются `mode=ro`.

⚠ ТАБЛИЦА ЖИВЁТ В `schemedb.sqlite`, а не в `nucdb.sqlite`: `ensdf_datasets`
там, `nuclides` — в `nucdb`. Правило читает обе, пишет ТОЛЬКО в `schemedb`.

ПРИЁМКА — ЧИСЛОМ. `--verify` пересчитывает правило и сверяет с колонкой; код 1
при любом расхождении. Собственный контроль правила — пример `110AG`
(`--control`, входит и в обычный прогон): оба набора «(249.76 D)» обязаны дать
`l_seqno` 2 (`110AGm1`), оба «(24.6 S)» — основное состояние, то есть пусто.

ЧТО ИЗМЕРЕНО (10.09.2026, поставка в дереве)

Метку изомера не несёт НИ ОДНО из 3450 непустых `parent_nucid`: разбор
«<A><символ><хвост>» со списком настоящих символов из `nuclides` даёт пустой
хвост у 2160 из 2173 РАЗЛИЧНЫХ значений, а остальные 13 — сверхтяжёлые,
записанные цифрами (`27110`, `28916`), тоже без метки. Отдельной колонки под
изомера нет вовсе (`parent_nucid`, `parent_hl_sec` — и всё), в отличие от
`decay_radiations.parent_l_seqno` и `gamma_coincidence_parent.isomer`.

⚠ ЛОВУШКА СЧЁТА, из-за которой находку однажды объявили снятой: признак
«метка изомера» нельзя искать выражением `[Mm]\\d?$` — оно ловит ХВОСТ ИМЕНИ
ЭЛЕМЕНТА. Перемерено: наивное выражение даёт **226 СТРОК** и **123 РАЗЛИЧНЫХ
значения**, и это ровно элементы на «m» (Tm 57 + Cm 51 + Pm 41 + Sm 34 + Am 24
+ Fm 19 = 226). Оба числа верны, но отвечают на РАЗНЫЕ вопросы, и путать их
нельзя. Метку состояния от символа отделяет РЕГИСТР, а не буква.

ПРАВИЛО ВЫВОДА МЕТКИ. Не из имени (её там нет), а из периода родителя:
`ensdf_datasets.parent_hl_sec` сводится с уровнями того же нуклида в
`nuclides` (`half_life_sec` по `l_seqno`). Сошлось в пределах допуска и второй
кандидат лежит ДАЛЕКО — уровень назван; `l_seqno = 0` значит «метка не нужна»,
иначе метка нужна и равна `nucid` этого уровня.

⛔ «ВТОРОЙ КАНДИДАТ ДАЛЕКО» — НЕ УКРАШЕНИЕ. Без него правило судило бы по
одному числу: два близких периода означают, что развести уровни периодом
нельзя, а вовсе не что подходит первый. То же условие стоит в приложении
(`CascadeAtomicData.ByParentHalfLife`, `S155`).

    python tools/nucdb/mark_isomer_datasets.py [--nucdb ...] [--schemedb ...]
                                               [--tol 0.01] [--show 15]
    python tools/nucdb/mark_isomer_datasets.py --verify   # сверка, код 0/1
    python tools/nucdb/mark_isomer_datasets.py --apply    # ЗАПИСЬ, в транзакции
"""
import argparse
import collections
import io
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite")
DEFAULT_SCHEMEDB = os.path.join(ROOT, "BecquerelMonitor", "schemedb.sqlite")

#: «<масса><буквы><хвост>»: 110AG, 234PAm1, 27110.
HEAD = re.compile(r"^(\d+)([A-Za-z]+)(.*)$")

#: Наивный признак изомера, который ЛОВИТ НЕ ТО. Держится в коде нарочно —
#: чтобы число ложных совпадений печаталось рядом с верным и путаницу нельзя
#: было повторить молча.
NAIVE = re.compile(r"[Mm]\d?$")


#: Колонка под уровень родителя. Имя взято у соседки `decay_radiations`, где
#: изомер различается ровно так же (`D38`, решение Amber 10.09.2026).
SEQ_COLUMN = "parent_l_seqno"


def open_ro(path):
    u"""Только чтение — приказ Amber 09.08.2026."""
    return sqlite3.connect("file:" + path.replace("\\", "/") + "?mode=ro", uri=True)


def open_rw(path):
    u"""На запись — ТОЛЬКО при `--apply`, по разрешению Amber 10.09.2026."""
    return sqlite3.connect(path)


def has_seq_column(sch):
    return SEQ_COLUMN in [r[1] for r in sch.execute(
        "pragma table_info(ensdf_datasets)")]


def ground_of(nucid):
    u"""«110AGm» -> «110AG». Метку отделяет РЕГИСТР: символ заглавными."""
    m = HEAD.match(nucid.strip())
    if not m:
        return None
    letters = m.group(2)
    up = 0
    while up < len(letters) and letters[up].isupper():
        up += 1
    return (m.group(1) + letters[:up]).upper()


def load_levels(nuc):
    u"""Основное состояние + изомеры каждого нуклида: ground -> [(seq, nucid, с)]."""
    levels = collections.defaultdict(list)
    for nucid, seq, sec in nuc.execute(
            "select nucid, l_seqno, half_life_sec from nuclides"
            " where half_life_sec is not null"):
        ground = ground_of(nucid)
        if ground:
            levels[ground].append((seq, nucid, sec))
    return levels


def load_rows(sch):
    return sch.execute(
        "select id, nucid, dsid, parent_nucid, parent_hl_sec from ensdf_datasets"
        " where parent_nucid is not null and trim(parent_nucid) <> ''").fetchall()


def decide(levels, rows, tol):
    u"""ПРАВИЛО, И ОНО ЗДЕСЬ ОДНО. Возвращает `(stat, would)`.

    `would` — строки под метку: `(id, parent, hl, уровень, l_seqno, дочь, dsid)`.
    Печать, сверка и запись зовут ЭТУ функцию — второй копии правила нет, иначе
    записанное и проверяемое разошлись бы молча.
    """
    stat = collections.Counter()
    would = []
    for did, dnuc, dsid, par, hl in rows:
        ground = ground_of(par)
        if ground is None:
            stat[u"`parent_nucid` записан цифрами — нуклид не опознать"] += 1
            continue
        cand = levels.get(ground)
        if not cand:
            stat[u"нуклида нет в `nuclides`"] += 1
            continue
        if hl is None:
            stat[u"у набора нет `parent_hl_sec`"] += 1
            continue
        scale = max(hl, 1e-300)
        best = min(cand, key=lambda c: abs(c[2] - hl) / scale)
        if abs(best[2] - hl) / scale > tol:
            stat[u"ни один уровень не сошёлся по периоду"] += 1
            continue
        rest = [c for c in cand if c[0] != best[0]]
        if rest:
            second = min(rest, key=lambda c: abs(c[2] - hl) / scale)
            if abs(second[2] - hl) / scale <= tol:
                stat[u"два уровня сошлись одинаково — периодом не развести"] += 1
                continue
        if best[0] == 0:
            stat[u"сошлось на ОСНОВНОМ состоянии — метка не нужна"] += 1
        else:
            stat[u"сошлось на ИЗОМЕРЕ — МЕТКА НУЖНА"] += 1
            would.append((did, par.strip(), hl, best[1], best[0], dnuc, dsid))
    return stat, would


#: Собственный контроль правила — пример из самой строки `D38`. Ожидание
#: записано ЧИСЛОМ и проверяется, а не пересказывается: наборы `110AG` с
#: «(249.76 D)» обязаны сесть на `110AGm1` (`l_seqno` 2), с «(24.6 S)» — на
#: основное состояние, то есть метки не получить вовсе.
CONTROL = {1104: 2, 1106: 2, 1103: None, 1105: None}


def control(out, rows, would):
    u"""Проверка на образце `110AG`. Возвращает 0, если правило дало ожидаемое."""
    got = dict((x[0], x[4]) for x in would)
    by_id = dict((r[0], r) for r in rows)
    out.write(u"\n## Контроль правила: четыре набора `110AG`\n\n")
    bad = 0
    for did in sorted(CONTROL):
        want = CONTROL[did]
        have = got.get(did)
        row = by_id.get(did)
        ok = (have == want)
        bad += 0 if ok else 1
        out.write(u"  %5d  %-28s ждём %-6s вышло %-6s  %s\n"
                  % (did, (row[2] if row else u"НЕТ СТРОКИ")[:28],
                     u"пусто" if want is None else str(want),
                     u"пусто" if have is None else str(have),
                     u"СОШЛОСЬ" if ok else u"⛔ РАЗОШЛОСЬ"))
    if bad:
        out.write(u"  ⛔ КОНТРОЛЬ НЕ ПРОЙДЕН: расхождений %d\n" % bad)
    return 1 if bad else 0


def verify_column(out, sch, would):
    u"""Колонка против правила. Код 0 — сошлось до единицы."""
    out.write(u"\n## Сверка: колонка `%s` против правила\n\n" % SEQ_COLUMN)
    if not has_seq_column(sch):
        out.write(u"  ⛔ колонки нет — сверять нечего (правилу отвечает %d строк)\n"
                  % len(would))
        return 1
    incol = dict((i, s) for i, s in sch.execute(
        "select id, %s from ensdf_datasets where %s is not null"
        % (SEQ_COLUMN, SEQ_COLUMN)))
    want = dict((x[0], x[4]) for x in would)
    extra = sorted(set(incol) - set(want))
    missing = sorted(set(want) - set(incol))
    wrong = sorted(i for i in set(incol) & set(want) if incol[i] != want[i])
    out.write(u"  в колонке непустых %d, по правилу %d\n" % (len(incol), len(want)))
    if not extra and not missing and not wrong:
        out.write(u"  СОШЛОСЬ до единицы\n")
        return 0
    if extra:
        out.write(u"  ⛔ помечено, а правилу НЕ отвечает: %d — %s\n"
                  % (len(extra), u", ".join(str(i) for i in extra[:12])))
    if missing:
        out.write(u"  ⛔ правилу отвечает, а НЕ помечено: %d — %s\n"
                  % (len(missing), u", ".join(str(i) for i in missing[:12])))
    if wrong:
        out.write(u"  ⛔ помечено ДРУГИМ уровнем: %d — %s\n"
                  % (len(wrong), u", ".join(u"%d (%s вместо %s)"
                                            % (i, incol[i], want[i])
                                            for i in wrong[:12])))
    out.write(u"  РАСХОЖДЕНИЕ: признак в базе и правило говорят разное\n")
    return 1


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except AttributeError:
        pass
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    p.add_argument("--tol", type=float, default=0.01,
                   help=u"допуск схождения периодов, доля (0.01 = 1 %%,"
                        u" как NearHalfLife в приложении)")
    p.add_argument("--show", type=int, default=15)
    p.add_argument("--apply", action="store_true",
                   help=u"ЗАПИСАТЬ (по умолчанию выключено, решение Amber"
                        u" 25.08.2026 по серии `D35`—`D38`)")
    p.add_argument("--verify", action="store_true",
                   help=u"только сверка колонки с правилом; код 1 при"
                        u" расхождении")
    a = p.parse_args()
    out = io.open(1, "w", encoding="utf-8", closefd=False)

    nuc = open_ro(a.nucdb)
    sch = open_rw(a.schemedb) if a.apply else open_ro(a.schemedb)

    levels = load_levels(nuc)
    rows = load_rows(sch)

    if a.verify:
        _, would = decide(levels, rows, a.tol)
        code = max(control(out, rows, would), verify_column(out, sch, would))
        out.flush()
        return code

    total = sch.execute("select count(*) from ensdf_datasets").fetchone()[0]
    distinct = len(set(r[3].strip().upper() for r in rows))

    out.write(u"# `D38`: метка изомера у родителя набора ENSDF — %s\n\n"
              % (u"ЗАПИСЬ" if a.apply else u"ЧТО БЫ ИЗМЕНИЛОСЬ"))
    if not a.apply:
        out.write(u"⛔ ЗАПИСИ НЕТ: ключ `--apply` не назван.\n\n")
    out.write(u"строк `ensdf_datasets`: %d; из них с непустым `parent_nucid`: %d;"
              u" РАЗЛИЧНЫХ значений: %d\n" % (total, len(rows), distinct))

    # --- метки в самом имени: сколько их (ответ — ноль) --------------------
    marked_name = 0
    digits = 0
    for value in set(r[3].strip() for r in rows):
        ground = ground_of(value)
        if ground is None:
            digits += 1
            continue
        if value.strip().upper() != ground:
            marked_name += 1
    naive_rows = sum(1 for r in rows if NAIVE.search(r[3].strip()))
    naive_vals = len(set(r[3].strip() for r in rows if NAIVE.search(r[3].strip())))
    out.write(u"метка изомера В САМОМ ИМЕНИ: %d из %d различных значений"
              u" (записанных цифрами: %d)\n" % (marked_name, distinct, digits))
    out.write(u"⚠ наивное `[Mm]\\d?$` даёт %d СТРОК и %d РАЗЛИЧНЫХ значений —"
              u" это хвосты имён элементов, а не метки\n" % (naive_rows, naive_vals))

    # --- неоднозначность: одно имя, разные периоды -------------------------
    by_name = collections.defaultdict(set)
    for _, _, _, par, hl in rows:
        by_name[par.strip().upper()].add(hl)
    # ⛔ ДВА ЧЕСТНЫХ ОТВЕТА, И ОНИ РАЗНЫЕ. `count(distinct parent_hl_sec)` в SQL
    # НЕ СЧИТАЕТ NULL, а множество в питоне считает его значением. Имя, у
    # которого один набор без периода и один с периодом, SQL зовёт однозначным,
    # питон — двузначным. Разница ровно в этом, и печатаются оба: строка `D38`
    # несёт числа обоих счётов (583 и «даёт 579»), не называя, какое чьё, — и
    # без этой пометки они выглядят опечаткой друг друга.
    amb_null = [k for k, v in by_name.items() if len(v) > 1]
    amb_sql = [k for k, v in by_name.items()
               if len(set(x for x in v if x is not None)) > 1]
    rows_null = sum(1 for r in rows if r[3].strip().upper() in set(amb_null))
    rows_sql = sum(1 for r in rows if r[3].strip().upper() in set(amb_sql))
    out.write(u"значений `parent_nucid` с ДВУМЯ И БОЛЕЕ разными `parent_hl_sec`:\n")
    out.write(u"    %d, если пустой период считать значением (наборов %d)\n"
              % (len(amb_null), rows_null))
    out.write(u"    %d, если считать как SQL — `count(distinct)` пропускает NULL"
              u" (наборов %d)\n" % (len(amb_sql), rows_sql))

    # --- что получило бы метку ---------------------------------------------
    stat, would = decide(levels, rows, a.tol)

    out.write(u"\n## Что бы изменилось (допуск %.3g %%)\n\n" % (100.0 * a.tol))
    for key in sorted(stat, key=lambda x: -stat[x]):
        out.write(u"  %-52s %5d\n" % (key, stat[key]))
    out.write(u"\nИТОГО строк под метку: **%d** из %d; различных `parent_nucid`"
              u" среди них: %d\n"
              % (len(would), len(rows), len(set(x[1].upper() for x in would))))
    if has_seq_column(sch):
        filled = sch.execute("select count(*) from ensdf_datasets"
                             " where %s is not null" % SEQ_COLUMN).fetchone()[0]
        out.write(u"КУДА писать — колонка `%s` ЕСТЬ, непустых в ней %d,"
                  u" останутся пустыми %d из %d\n"
                  % (SEQ_COLUMN, filled, total - filled, total))
    else:
        out.write(u"КУДА писать — колонки нет: понадобился бы"
                  u" `alter table ensdf_datasets add column %s integer`\n"
                  % SEQ_COLUMN)

    out.write(u"\n## Первые %d строк: id -> уровень `nuclides`\n\n" % a.show)
    for x in would[:a.show]:
        out.write(u"  %5d  %-8s hl=%-14.6g -> %-10s l_seqno=%d   %s\n"
                  % (x[0], x[1], x[2], x[3], x[4], x[6]))

    code = control(out, rows, would)

    if not a.apply:
        out.write(u"\n⛔ ЗАПИСИ НЕ БЫЛО: обе базы открыты `mode=ro`,"
                  u" ключ `--apply` не назван.\n")
        out.flush()
        return code

    # ── ЗАПИСЬ ───────────────────────────────────────────────────────────
    # ⛔ Разрешение Amber 10.09.2026, дословно: «Разрешаю: писать 662 строки».
    # Одна транзакция: колонка и все метки въезжают вместе или никак.
    # ⛔ Контроль правила идёт ПЕРЕД записью: не сошёлся образец `110AG` —
    # писать нельзя, потому что правило считает не то.
    if code:
        out.write(u"\n⛔ ЗАПИСИ НЕ БУДЕТ: контроль правила не пройден.\n")
        out.flush()
        return code

    out.write(u"\n## ЗАПИСЬ (разрешение Amber 10.09.2026:"
              u" «Разрешаю: писать 662 строки»)\n\n")
    try:
        sch.execute("begin")
        if not has_seq_column(sch):
            sch.execute("alter table ensdf_datasets add column %s integer"
                        % SEQ_COLUMN)
            out.write(u"  `alter table ensdf_datasets add column %s integer`"
                      u" — сделано\n" % SEQ_COLUMN)
        sch.executemany("update ensdf_datasets set %s = ? where id = ?"
                        % SEQ_COLUMN, [(x[4], x[0]) for x in would])
        changed = sch.total_changes
        sch.commit()
    except Exception:
        sch.rollback()
        raise
    out.write(u"  помечено строк: %d (различных `parent_nucid` %d)\n"
              % (len(would), len(set(x[1].upper() for x in would))))
    out.write(u"  `total_changes` соединения: %d\n" % changed)

    code = verify_column(out, sch, would)
    out.flush()
    return code


if __name__ == "__main__":
    sys.exit(main())
