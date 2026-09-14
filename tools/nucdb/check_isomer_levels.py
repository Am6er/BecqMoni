# -*- coding: utf-8 -*-
u"""Сторож изомерной РАЗМЕТКИ `nucdb`: чьи строки лежат под чьим именем.

Судит одно: соответствие между ИМЕНЕМ изомера (`nucid`) и НОМЕРОМ УРОВНЯ
(`l_seqno` / `parent_l_seqno`) в трёх таблицах — `nuclides`, `decay_radiations`,
`l_decays`. Заведён 11.09.2026 полосой П5 по строкам `D40`, `D41`, `D47`.

⛔ ЗАЧЕМ СТОРОЖ, А НЕ ОДНА ЗАПИСЬ В ЖУРНАЛЕ. Все три беды этой темы
ПРОЯВЛЯЮТСЯ МОЛЧА — ни отказа, ни предупреждения, только другое число:

* `select … from nuclides where nucid = ?` возвращает ПРОИЗВОЛЬНУЮ из трёх
  строк `144TBm` (`D41`): 4.25 с, 2.8 мкс или 0.67 мкс. Уникальности имени в
  схеме нет, и запретить её нечем — три строки ПОДЛИННЫЕ (см. §1);
* `DecayParentRule.LevelClause` при отсутствии своего уровня уходит на
  `min(parent_l_seqno)` и отдаёт строки СОСЕДНЕГО изомера как свои (`D40`);
* у четырёх родителей строк на двух уровнях сразу, и сложение уровней ДВОИТ
  распад — «два распада по сто процентов у одного ядра» (`S89`, `D47`).

Каждое из этих мест сегодня в равновесии, и равновесие держится ЧИСЛАМИ, а не
правилом: стоит поставке измениться (переписали `nucdb`, пересобрали импортёром,
пометили руками) — и множества расходятся, не сказав ни слова. Сторож и есть
потребитель этих чисел.

⚠ **Сторож НЕ судит, что верно физически, и не лечит базу.** Запись в `nucdb`
идёт только через Amber; сторож фиксирует ФАКТИЧЕСКОЕ состояние поставки на
11.09.2026 и краснеет при любом отклонении — в том числе когда решение Amber
БУДЕТ выполнено. Это нарочно: выполненное решение обязано пройти через правку
ожиданий здесь, иначе «починили» и «испортили» выглядят одинаково.

Что проверяется (числа замерены 11.09.2026 на `BecquerelMonitor/*.sqlite`):

  1. дубли имени в `nuclides`: ровно одно имя — `144TBm`, уровни 4/6/7;
  2. имена, чей `nuclides.l_seqno` НЕ встречается в `decay_radiations`
     (запасная ветвь правила): ровно `123CSm2`;
  3. то же в `l_decays`: ровно `123CSm2` и `179AUm`;
  4. родители со строками более чем на одном уровне в `decay_radiations`:
     ровно `116AGm2`, `118INm2`, `190Wm2`, `70CUm2`, и у каждого СВОЙ уровень
     среди них есть (иначе правило уйдёт на запасную ветвь);
  5. то же в `l_decays`: те же четыре плюс `198BIm2`;
  6. набор `123CSm2` побитово совпадает со строками `123CSm1` — это довод, на
     котором стоит `D40`, и он обязан держаться;
  7. запасная ветвь в `DecayParentRule.LevelClause` жива (иначе п. 2 судит
     мёртвое правило);
  8. числа §0/§2 `database/scheme.md` — строк `nuclides` и различных имён —
     против базы.

    python tools/nucdb/check_isomer_levels.py [--nucdb ...] [--scheme ...]
                                              [--rule ...]

Коды возврата: 0 — всё сошлось; 1 — хоть одно расхождение; 2 — файла нет.
"""
import argparse
import io
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite")
DEFAULT_SCHEME = os.path.join(ROOT, "database", "scheme.md")
DEFAULT_RULE = os.path.join(ROOT, "BecquerelMonitor", "FullSpectrumAnalysis",
                            "DecayParentRule.cs")

OUT = io.open(1, "w", encoding="utf-8", closefd=False)


def w(fmt, *args):
    OUT.write((fmt % args if args else fmt) + u"\n")


# ── ОЖИДАНИЯ. Замерено 11.09.2026, полоса П5. ───────────────────────────────
#: `D41`: имена, стоящие в `nuclides` более чем одной строкой, и их уровни.
#: ⛔ Три строки `144TBm` — ПОДЛИННЫЕ, а не три редакции поставки: уровни 4 и 6
#: независимо подтверждает схема Geant4 (`schemedb.g4_level`, Z=65 A=144:
#: seq 4 = 396.9 кэВ / 4.25 с / 6−, seq 6 = 476.2 кэВ / 2.8 мкс / 8−) и набор
#: ENSDF `144TB IT DECAY (4.25 S)` (уровень родителя 4). Удаление «лишних»
#: уничтожило бы данные; беда только в ИМЕНИ — поставка пишет всем трём голое
#: `m` вместо `m1`/`m2`/`m3`.
EXPECT_DUP_NUCLIDES = {u"144TBm": (4, 6, 7)}

#: `D40`: имя -> (уровни в `decay_radiations`, уровни в `nuclides`). Свой
#: уровень имени в `decay_radiations` НЕ встречается, и правило уходит на
#: запасную ветвь `min(...)`, отдавая строки соседа как собственные.
EXPECT_ORPHAN_DR = {u"123CSm2": ((5,), (8,))}

#: `D40`, расширение замером 11.09.2026: в `l_decays` таких имён ДВА, а не
#: одно. `179AUm` в `decay_radiations` не входит вовсе (потому прежний разбор
#: его и не увидел — он смотрел только туда), а его единственная строка
#: `l_decays` стоит на уровне 2 при `nuclides.l_seqno` = 7. Уровень 7
#: подтверждает Geant4 (Z=79 A=179: seq 7 = 86.0 кэВ / 100 мкс, и это ровно
#: наш период), уровень 2 у Geant4 — уровень 0 кэВ, то есть смысла не несёт.
#: ⚠ Потребителя у `l_decays` в дереве НЕТ НИ ОДНОГО (замер 11.09.2026: ни
#: одного `.cs`, ни одной пробы; только `split_db.py` и `dec_type_to_int.py`),
#: поэтому это не дефект приложения, а порча ПОСТАВКИ.
EXPECT_ORPHAN_LD = {u"123CSm2": ((5,), (8,)), u"179AUm": ((2,), (7,))}

#: `D47`/`S89`: родители, чьи строки лежат более чем на одном уровне. Значение
#: — (уровни в таблице, свой уровень из `nuclides`). Свой уровень ОБЯЗАН быть
#: среди них: на нём стоит собственный набор изомера, на нижнем — набор соседа
#: `m1`, попавший в поставку под тем же именем.
EXPECT_MULTI_DR = {
    u"116AGm2": ((1, 4), 4),
    u"118INm2": ((1, 3), 3),
    u"190Wm2": ((6, 7), 7),
    u"70CUm2": ((1, 3), 3),
}

#: То же в `l_decays`. ⚠ Здесь имён ПЯТЬ: `198BIm2` несёт два уровня в
#: `l_decays` (1 и 3 при своём 3), а в `decay_radiations` — только свой.
EXPECT_MULTI_LD = {
    u"116AGm2": ((1, 4), 4),
    u"118INm2": ((1, 3), 3),
    u"190Wm2": ((6, 7), 7),
    u"70CUm2": ((1, 3), 3),
    u"198BIm2": ((1, 3), 3),
}

#: `D40`: сколько строк в подложенном наборе `123CSm2` и с кем они совпадают.
EXPECT_CS_ROWS = 9
EXPECT_CS_TWIN = u"123CSm1"

#: §8: числа `database/scheme.md`. Строк `nuclides` больше, чем имён, ровно на
#: два — это две лишние строки `144TBm` (п. 1), и различие СТРОК и ИМЁН обязано
#: быть в описании названо, иначе читатель считает `nucid` ключом.
EXPECT_NUCLIDES_ROWS = 4429
EXPECT_NUCLIDES_NAMES = 4427


def open_ro(path):
    return sqlite3.connect(u"file:" + path.replace(u"\\", u"/") + u"?mode=ro",
                           uri=True)


def as_tuple(csv):
    if csv is None:
        return ()
    return tuple(sorted(int(x) for x in csv.split(u",")))


def cmp_maps(title, got, want, fmt):
    u"""Сравнить два словаря имя -> значение и напечатать расхождения."""
    w(u"%s: в базе %d, ожидалось %d", title, len(got), len(want))
    bad = []
    for name in sorted(set(got) | set(want)):
        if name not in want:
            w(u"   ⛔ НОВОЕ ИМЯ %s: %s — такого в поставке 11.09.2026 не было",
              name, fmt(got[name]))
            bad.append(u"новое имя %s" % name)
        elif name not in got:
            w(u"   ⛔ ПРОПАЛО ИМЯ %s (ожидалось %s) — либо поставка исправлена,"
              u" либо испорчена; решение Amber требует обновить ожидание здесь",
              name, fmt(want[name]))
            bad.append(u"пропало имя %s" % name)
        elif got[name] != want[name]:
            w(u"   ⛔ %s: в базе %s, ожидалось %s", name, fmt(got[name]),
              fmt(want[name]))
            bad.append(u"%s изменился" % name)
        else:
            w(u"   ✔ %s: %s", name, fmt(got[name]))
    return bad


def fmt_levels(v):
    return u"уровни " + u"/".join(u"%d" % x for x in v)


def fmt_pair(v):
    return u"в таблице %s, в nuclides %s" % (
        u"/".join(u"%d" % x for x in v[0]), u"/".join(u"%d" % x for x in v[1]))


def fmt_own(v):
    return u"уровни %s, свой %d" % (u"/".join(u"%d" % x for x in v[0]), v[1])


def check_dups(db):
    got = {}
    for nucid, lv in db.execute(
            u"select nucid, group_concat(l_seqno) from nuclides"
            u" group by nucid having count(*) > 1"):
        got[nucid] = as_tuple(lv)
    return cmp_maps(u"1. `D41` дубли имени в `nuclides`", got,
                    dict((k, tuple(v)) for k, v in EXPECT_DUP_NUCLIDES.items()),
                    fmt_levels)


#: Имена, у которых `nuclides.l_seqno` не встречается в таблице-спутнике.
#: Запрос идёт по ИМЕНИ целиком (не по базовому!) — ошибка `D11`.
ORPHAN_SQL = u"""
select t.{name}, group_concat(distinct t.{lvl}),
       (select group_concat(y.l_seqno) from nuclides y where y.nucid = t.{name})
from {tab} t
group by t.{name}
having exists (select 1 from nuclides y where y.nucid = t.{name})
   and not exists (select 1 from nuclides y join {tab} z
                     on z.{name} = y.nucid and z.{lvl} = y.l_seqno
                   where y.nucid = t.{name})
"""


def check_orphans(db, tab, name, lvl, title, want):
    got = {}
    q = ORPHAN_SQL.format(tab=tab, name=name, lvl=lvl)
    for nucid, tlv, nlv in db.execute(q):
        got[nucid] = (as_tuple(tlv), as_tuple(nlv))
    return cmp_maps(title, got,
                    dict((k, (tuple(v[0]), tuple(v[1]))) for k, v in want.items()),
                    fmt_pair)


MULTI_SQL = u"""
select t.{name}, group_concat(distinct t.{lvl})
from {tab} t
group by t.{name} having count(distinct t.{lvl}) > 1
"""


def check_multi(db, tab, name, lvl, title, want):
    got = {}
    for nucid, tlv in db.execute(MULTI_SQL.format(tab=tab, name=name, lvl=lvl)):
        own = [r[0] for r in db.execute(
            u"select l_seqno from nuclides where nucid = ?", (nucid,))]
        levels = as_tuple(tlv)
        # Свой уровень — тот из `nuclides`, что среди уровней таблицы есть;
        # его отсутствие само по себе отказ, и печатается как -1.
        pick = [x for x in own if x in levels]
        got[nucid] = (levels, pick[0] if pick else -1)
    bad = cmp_maps(title, got,
                   dict((k, (tuple(v[0]), v[1])) for k, v in want.items()),
                   fmt_own)
    for nucid, (levels, pick) in sorted(got.items()):
        if pick < 0:
            w(u"   ⛔ %s: СВОЕГО уровня среди %s нет — правило уйдёт на"
              u" запасную ветвь и отдаст строки соседа", nucid, fmt_levels(levels))
            bad.append(u"%s без своего уровня" % nucid)
    return bad


def check_cs_twin(db):
    u"""`D40`: набор под именем `123CSm2` — побитовая копия строк `123CSm1`."""
    w(u"6. `D40` набор `123CSm2` против строк `%s`", EXPECT_CS_TWIN)
    cols = u"type_a, type_c, energy, energy_num, intensity, intensity_num"
    q = (u"select " + cols + u" from decay_radiations"
         u" where parent_nucid = ? order by " + cols)
    mine = list(db.execute(q, (u"123CSm2",)))
    twin = list(db.execute(q, (EXPECT_CS_TWIN,)))
    w(u"   строк под `123CSm2`: %d; строк под `%s`: %d; ожидалось по %d",
      len(mine), EXPECT_CS_TWIN, len(twin), EXPECT_CS_ROWS)
    bad = []
    if len(mine) != EXPECT_CS_ROWS or len(twin) != EXPECT_CS_ROWS:
        w(u"   ⛔ ЧИСЛО СТРОК ИЗМЕНИЛОСЬ")
        bad.append(u"число строк `123CSm2`/`%s` не %d"
                   % (EXPECT_CS_TWIN, EXPECT_CS_ROWS))
    if mine != twin:
        w(u"   ⛔ НАБОРЫ РАЗОШЛИСЬ — довод `D40` («под именем `m2` лежат строки"
          u" `m1`») больше не держится; разбор надо переделать")
        only_mine = [r for r in mine if r not in twin]
        only_twin = [r for r in twin if r not in mine]
        for r in only_mine[:5]:
            w(u"     только у `123CSm2`: %s", r)
        for r in only_twin[:5]:
            w(u"     только у `%s`: %s", EXPECT_CS_TWIN, r)
        bad.append(u"наборы `123CSm2` и `%s` разошлись" % EXPECT_CS_TWIN)
    else:
        w(u"   ✔ совпали побитово по всем шести колонкам")
    return bad


def check_rule(path):
    u"""Запасная ветвь правила жива? Иначе п. 2 судит мёртвое выражение."""
    w(u"7. запасная ветвь `DecayParentRule.LevelClause`")
    if not os.path.isfile(path):
        w(u"   ⛔ ФАЙЛА НЕТ: %s", path)
        return [u"нет файла правила"], 2
    with io.open(path, encoding="utf-8") as f:
        text = f.read()
    bad = []
    for needle in (u"coalesce", u"min(y.parent_l_seqno)"):
        if needle in text:
            w(u"   ✔ найдено `%s`", needle)
        else:
            w(u"   ⛔ НЕ НАЙДЕНО `%s` — правило переписано, и ожидание п. 2"
              u" («ровно одно имя на запасной ветви») больше ничего не значит",
              needle)
            bad.append(u"в правиле нет `%s`" % needle)
    return bad, 0


def check_scheme(db, path):
    u"""§8: числа описания против базы, и названо ли различие строк и имён."""
    w(u"8. числа `database/scheme.md` против базы")
    rows = db.execute(u"select count(*) from nuclides").fetchone()[0]
    names = db.execute(u"select count(distinct nucid) from nuclides").fetchone()[0]
    w(u"   в базе: строк %d, различных имён %d", rows, names)
    bad = []
    if rows != EXPECT_NUCLIDES_ROWS or names != EXPECT_NUCLIDES_NAMES:
        w(u"   ⛔ БАЗА ИЗМЕНИЛАСЬ: ожидалось строк %d, имён %d",
          EXPECT_NUCLIDES_ROWS, EXPECT_NUCLIDES_NAMES)
        bad.append(u"числа `nuclides` не %d/%d"
                   % (EXPECT_NUCLIDES_ROWS, EXPECT_NUCLIDES_NAMES))
    if not os.path.isfile(path):
        w(u"   ⛔ ФАЙЛА НЕТ: %s", path)
        return [u"нет файла описания"], 2
    with io.open(path, encoding="utf-8") as f:
        text = f.read()
    # ⚠ Искать число ПОИСКОМ «любого числа того же порядка» НЕЛЬЗЯ: в §2 рядом
    # законно стоят 4348 (строк с пустой `half_life_is_limit`) и 4377 (строк
    # снятой колонки `mag_mom`), и такой сторож краснеет на верном тексте.
    # Поэтому каждое место названо ЯКОРЕМ, и пропажа якоря — тоже отказ:
    # текст, сменивший форму, мог сменить и число.
    #
    # ⚠ Исторические числа с датой (в §2 стоит «Строк стало 4423 … 08.08.2026»)
    # сторож НЕ судит и судить не должен: это запись о прошлом состоянии, и
    # подгонять её под сегодняшнюю базу значит переписывать историю. Якоря
    # выбраны так, чтобы попадать ТОЛЬКО в утверждения о текущем состоянии.
    anchors = (
        (u"таблица §0", r"\|\s*`nuclides`\s*\|\s*nuc\s*\|\s*([0-9]+)\s*\|",
         rows),
        (u"§2, заголовок таблицы", r"\*\*`nuclides`\*\*\s*—\s*([0-9]+)\s+строк",
         rows),
        # ⛔ Число РАЗЛИЧНЫХ ИМЁН обязано стоять рядом со числом строк: без
        # него читатель `scheme.md` считает `nucid` ключом, а он им не
        # является (`D41`, три строки `144TBm`).
        (u"§2, различных имён", u"различных имён\\s*\\*{0,2}([0-9]+)", names),
    )
    for title, pat, want in anchors:
        m = re.search(pat, text)
        if m is None:
            w(u"   ⛔ ЯКОРЬ ПРОПАЛ (%s): образец `%s` в описании не найден —"
              u" текст переписан, число больше ничем не держится", title, pat)
            bad.append(u"в scheme.md нет якоря «%s»" % title)
        elif int(m.group(1)) != want:
            w(u"   ⛔ %s: в описании %s, в базе %d", title, m.group(1), want)
            bad.append(u"scheme.md «%s» даёт %s вместо %d"
                       % (title, m.group(1), want))
        else:
            w(u"   ✔ %s: %d", title, want)
    return bad, 0


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--scheme", default=DEFAULT_SCHEME)
    p.add_argument("--rule", default=DEFAULT_RULE)
    a = p.parse_args()

    w(u"# Изомерная разметка `nucdb`: имя против уровня (`D40`, `D41`, `D47`)")
    w(u"база: %s", a.nucdb)
    if not os.path.isfile(a.nucdb):
        w(u"⛔ ФАЙЛА НЕТ")
        OUT.flush()
        return 2
    db = open_ro(a.nucdb)
    bad = []
    w(u"")
    bad += check_dups(db)
    w(u"")
    bad += check_orphans(db, u"decay_radiations", u"parent_nucid",
                         u"parent_l_seqno",
                         u"2. `D40` свой уровень отсутствует в `decay_radiations`",
                         EXPECT_ORPHAN_DR)
    w(u"")
    bad += check_orphans(db, u"l_decays", u"nucid", u"l_seqno",
                         u"3. `D40` свой уровень отсутствует в `l_decays`",
                         EXPECT_ORPHAN_LD)
    w(u"")
    bad += check_multi(db, u"decay_radiations", u"parent_nucid",
                       u"parent_l_seqno",
                       u"4. `D47` строки более чем на одном уровне"
                       u" в `decay_radiations`", EXPECT_MULTI_DR)
    w(u"")
    bad += check_multi(db, u"l_decays", u"nucid", u"l_seqno",
                       u"5. `D47` строки более чем на одном уровне"
                       u" в `l_decays`", EXPECT_MULTI_LD)
    w(u"")
    bad += check_cs_twin(db)
    w(u"")
    rule_bad, code = check_rule(a.rule)
    bad += rule_bad
    if code == 2:
        OUT.flush()
        return 2
    w(u"")
    sch_bad, code = check_scheme(db, a.scheme)
    bad += sch_bad
    if code == 2:
        OUT.flush()
        return 2

    w(u"")
    if bad:
        w(u"ОТКАЗ: " + u"; ".join(bad))
        w(u"⚠ Расхождение НЕ значит «база испорчена»: оно значит, что поставка"
          u" изменилась, а ожидания здесь — нет. Пересверить разбор (`D40`,"
          u" `D41`, `D47`) и обновить числа ЭТОГО файла тем же движением.")
        OUT.flush()
        return 1
    w(u"ИЗОМЕРНАЯ РАЗМЕТКА СОШЛАСЬ: 1 дубль имени, 1 сирота в"
      u" `decay_radiations`, 2 в `l_decays`, 4 и 5 имён на двух уровнях.")
    OUT.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())
