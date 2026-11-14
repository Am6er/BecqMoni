#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож таблицы замен `nucdb.decay_radiations_superseded` (`AMBER274` Э3, полоса `a274n`, 14.11.2026).

ЗАЧЕМ. Строка поставки, у которой оценка LNHB сдвинула ключ ветви сверх допуска номера (`AMBER281`: Tl-207 β⁻
1431 кэВ NuDat → 1418(5) кэВ LNHB), из базы не удаляется — решение Amber 14.11.2026 вопросником, дословно:
«Строка остаётся, отсев (Рекомендую)» (правило 02.11.2026 «исходные данные с признаком не удалять — отсеивать у
читателей»). Замена лежит новой строкой, пара «строка → замена» — в `decay_radiations_superseded`, читатели β
отсеивают заменённые довеском `DecayParentRule.NotSupersededClause` (`FsaSampleLibrary`, `CascadeAtomicData`,
`tools/nucdb/import_beta_shapes.py`). Таблица — ДАННЫЕ, и они расходятся с правилом молча: замена удалена,
указывает на чужого родителя, заменили гамму, которую отсеивать некому, — и выход двоится или пропадает без отказа.

ЧТО СУДИТ:
  1. заменённая строка и её замена есть в `decay_radiations`, это РАЗНЫЕ строки, замена сама не заменена;
  2. у них один родитель, уровень родителя и тип излучения;
  3. заменяются только строки β (`B-`, `B+`) — гамм и рентгена довесок у читателей нет;
  4. источник и примечание не пусты;
  5. якорь: Σβ⁻ Tl-207 глазами читателя (заменённые отсеяны) = 100.0 % (без отсева — 199.729 %).
Положительный контроль (всегда): копия базы в памяти — замена на строку чужого родителя, замена гаммы, замена
несуществующей строкой, снятая таблица замен — каждая подмена обязана дать отказ.

    python tools/check_nucdb_superseded.py [--db=<nucdb>]

Код: 0 — сошлось (или таблицы нет — замен нет); 1 — отказ или контроль не поймал подмену; 2 — не запустился.
"""
from __future__ import print_function
import argparse
import os
import sqlite3
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
sys.path.insert(0, os.path.join(ROOT, 'tools', 'CORPUS', 'scripts'))
from chains import LEVEL_CLAUSE, LEVEL_PARAM  # noqa: E402
DB = os.path.join(ROOT, 'BecquerelMonitor', 'nucdb.sqlite')
TABLE = 'decay_radiations_superseded'
BETA = ('B-', 'B+')
#: якоря: Σ выходов типа у родителя глазами читателя, %
EXPECT = {('207TL', 'B-'): 100.0}


def has_table(con):
    return con.execute("select 1 from sqlite_master where type = 'table' and name = ?", (TABLE,)).fetchone() is not None


def reader_sum(con, nucid, type_a):
    extra = ' and dr_pk not in (select dr_pk from %s)' % TABLE if has_table(con) else ''
    # уровень родителя — общим правилом (`DecayParentRule.LevelClause` через `chains.py`), своей копии нет
    row = con.execute('select coalesce(sum(intensity_num), 0) from decay_radiations where parent_nucid = $n and type_a = $t'
                      + LEVEL_CLAUSE + extra, {LEVEL_PARAM: nucid, 't': type_a}).fetchone()
    return row[0]


def anchors(con, bad):
    u"""Якоря — только у базы, где строки LNHB этого родителя уже лежат (`dr_pk` ≥ 400001, `correct_lnhb_alpha.py`)."""
    for (nucid, type_a), want in sorted(EXPECT.items()):
        if con.execute('select 1 from decay_radiations where parent_nucid = ? and type_a = ? and dr_pk >= 400001',
                       (nucid, type_a)).fetchone() is None:
            continue
        got = reader_sum(con, nucid, type_a)
        if abs(got - want) > 1e-6:
            bad.append(u'Σ%s %s глазами читателя %.5f %%, ждали %.5f %%' % (type_a, nucid, got, want))


def judge(con):
    bad = []
    if not has_table(con):
        anchors(con, bad)
        return bad, 0
    rows = con.execute('select dr_pk, by_dr_pk, source, note from %s' % TABLE).fetchall()
    olds = set(r[0] for r in rows)
    for pk, by, source, note in rows:
        a = con.execute('select parent_nucid, parent_l_seqno, type_a from decay_radiations where dr_pk = ?', (pk,)).fetchone()
        b = con.execute('select parent_nucid, parent_l_seqno, type_a from decay_radiations where dr_pk = ?', (by,)).fetchone()
        if a is None:
            bad.append(u'%d: заменённой строки в decay_radiations нет' % pk)
            continue
        if b is None:
            bad.append(u'%d → %d: замены в decay_radiations нет' % (pk, by))
            continue
        if pk == by:
            bad.append(u'%d: заменена сама собой' % pk)
        if by in olds:
            bad.append(u'%d → %d: замена сама заменена' % (pk, by))
        if a != b:
            bad.append(u'%d → %d: разные родитель/уровень/тип: %r против %r' % (pk, by, a, b))
        if a[2] not in BETA:
            bad.append(u'%d: заменена строка типа %s — довесок есть только у читателей β (%s)' % (pk, a[2], ', '.join(BETA)))
        if not (source or '').strip() or not (note or '').strip():
            bad.append(u'%d: пустой источник или примечание' % pk)
    anchors(con, bad)
    return bad, len(rows)


def copy_in_memory(path):
    src = sqlite3.connect('file:%s?mode=ro' % path.replace('\\', '/'), uri=True)
    mem = sqlite3.connect(':memory:')
    src.backup(mem)
    src.close()
    return mem


def controls(path):
    u"""Каждая подмена обязана дать отказ."""
    cases = []

    def first_other(con, nucid, type_a):
        return con.execute('select dr_pk from decay_radiations where parent_nucid != ? and type_a = ? limit 1',
                           (nucid, type_a)).fetchone()[0]

    def spoil_parent(con):
        pk, by = con.execute('select dr_pk, by_dr_pk from %s limit 1' % TABLE).fetchone()
        t = con.execute('select parent_nucid, type_a from decay_radiations where dr_pk = ?', (pk,)).fetchone()
        con.execute('update %s set by_dr_pk = ? where dr_pk = ?' % TABLE, (first_other(con, t[0], t[1]), pk))

    def spoil_gamma(con):
        g = con.execute("select dr_pk, parent_nucid from decay_radiations where type_a = 'G' and dr_pk < 100001 limit 2").fetchall()
        con.execute('insert into %s values (?, ?, ?, ?)' % TABLE, (g[0][0], g[1][0], u'контроль', u'контроль'))

    def spoil_missing(con):
        pk = con.execute('select dr_pk from %s limit 1' % TABLE).fetchone()[0]
        con.execute('update %s set by_dr_pk = 999999999 where dr_pk = ?' % TABLE, (pk,))

    def spoil_drop(con):
        con.execute('drop table %s' % TABLE)

    for name, spoil in ((u'замена — строка чужого родителя', spoil_parent), (u'заменена гамма', spoil_gamma),
                        (u'замены нет в базе', spoil_missing), (u'таблица замен снята (Tl-207 двоится)', spoil_drop)):
        con = copy_in_memory(path)
        try:
            spoil(con)
            bad, _ = judge(con)
        finally:
            con.close()
        cases.append((name, bool(bad), bad[:1]))
    return cases


def main():
    ap = argparse.ArgumentParser(description=u'Таблица замен decay_radiations_superseded (AMBER274 Э3)')
    ap.add_argument('--db', default=DB)
    a = ap.parse_args()
    if not os.path.isfile(a.db):
        print(u'⛔ нет базы: %s' % a.db)
        return 2
    con = sqlite3.connect('file:%s?mode=ro' % a.db.replace('\\', '/'), uri=True)
    try:
        present = has_table(con)
        bad, n = judge(con)
        sums = [(k, reader_sum(con, *k)) for k in sorted(EXPECT)]
    finally:
        con.close()
    print(u'база: %s' % a.db)
    if not present:
        for b in bad:
            print(u'  ⛔ ' + b)
        print(u'таблицы %s нет — замен нет, читатели работают, как прежде' % TABLE)
        return 1 if bad else 0
    print(u'строк замен: %d' % n)
    for (nucid, type_a), s in sums:
        print(u'  Σ%s %s глазами читателя: %.5f %%' % (type_a, nucid, s))
    ok_ctl = True
    for name, caught, why in controls(a.db):
        print(u'  положительный контроль «%s»: %s%s' % (name, u'ОТКАЗ ПОЙМАН' if caught else u'НЕ ПОЙМАН',
                                                       (u' (' + why[0] + u')') if why else u''))
        ok_ctl &= caught
    for b in bad:
        print(u'  ⛔ ' + b)
    if bad or not ok_ctl:
        print(u'ОСТАНОВ: таблица замен расходится с правилом' if bad else u'ОСТАНОВ: положительный контроль не поймал подмену')
        return 1
    print(u'ЗАМЕНЫ СОШЛИСЬ: замена того же родителя и типа, заменены только строки β, якорь Tl-207 — 100 %.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
