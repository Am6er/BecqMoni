#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож постоянства номеров `nucdb.decay_radiations.dr_pk` (`AMBER281`, полоса `dbdata2`, 02.11.2026).

ЗАЧЕМ. Решение Amber 02.11.2026, дословно: «в изотоп добавляй строку из базы (индекс) чтобы быть точно уверенным в
будущем» — линии библиотеки нуклидов хранят `dr_pk` строки базы, из которой взяты. Номер обязан жить столько же,
сколько сама строка: инструмент, снявший диапазон и вставивший строки заново подряд (так до 02.11.2026 делали
`import_l_sublines.py` и `correct_lnhb.py`), молча переводит ссылку на чужую строку. Правило и распределитель —
`tools/nucdb/dr_pk_ledger.py`; реестр снятых — `tools/nucdb/data/nucdb_dr_pk_retired.tsv`.

ЧТО СУДИТ — база дерева `BecquerelMonitor/nucdb.sqlite` против ЗАКОММИЧЕННОЙ (`git show HEAD:…` во временный файл):

  1. у номера, который есть в обеих, совпадают родитель, тип излучения и подлиния (`parent_nucid`, `type_a`,
     `type_c`), а энергия — в допуске max(1 кэВ, 0.2 % E) (правка по LNHB сдвигает до 0.43 кэВ, пересчёт подлиний —
     до 0.71 кэВ); иначе — номер переехал на другую строку: ОТКАЗ. ⚠ У ветви β (`B-`, `B+`) энергия строки — СРЕДНЯЯ
     энергия спектра, свойство формы, а не имя ветви: ветвь называет её ГРАНИЦА (`endpoint`). Поэтому у β-строк в
     допуске сверяется граница, а сдвиг средней печатается справкой. Повод — K-40 β⁺ (13928): средняя 197.325 → 251.757
     кэВ по LNHB (форма «уникальная третья запрещённая» против разрешённой у NuDat), граница (1504.40) та же — правка
     на месте полосы `purebeta` 01.11.2026, не переезд;
  2. номер, которого в дереве нет, обязан стоять в реестре снятых (печатается с источником правки); нет в реестре —
     ОТКАЗ («снят без записи»);
  3. новый номер — только выше прежнего наибольшего в своём диапазоне (поставка, добор, L-подлинии, LNHB) и выше
     всех снятых в нём; иначе — ОТКАЗ;
  4. номер дерева, стоящий в реестре снятых, — переиспользование: ОТКАЗ;
  5. положительный контроль (всегда): копия базы дерева в памяти с перенумерованным диапазоном L-подлиний (снята
     первая строка, остальные — подряд с начала диапазона) обязана дать отказ по правилам 1–4; второй — две β-ветви
     одного родителя с разными границами, обменявшиеся номерами, обязаны дать отказ по правилу 1.

Запуск:
    python tools/check_nucdb_pk_stable.py                 # дерево против HEAD
    python tools/check_nucdb_pk_stable.py --base=<база>    # против данной базы вместо HEAD
Код возврата: 0 — номера держатся; 1 — отказ (или положительный контроль не поймал подмену); 2 — не запустился.
"""
from __future__ import print_function
import argparse
import os
import re
import sqlite3
import subprocess
import sys
import tempfile

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
sys.path.insert(0, os.path.join(ROOT, 'tools', 'nucdb'))
import dr_pk_ledger as ledger  # noqa: E402

TREE = os.path.join(ROOT, 'BecquerelMonitor', 'nucdb.sqlite')
REL = 'BecquerelMonitor/nucdb.sqlite'


BETA = ('B-', 'B+')


def endpoint_num(text):
    m = re.search(r'\d+(\.\d*)?', text or '')
    return float(m.group(0)) if m else None


def load(path):
    u"""dr_pk -> (родитель, тип, подлиния, энергия сверки, средняя энергия): у β-строк энергия сверки — граница
    ветви (`endpoint`), если она записана, иначе средняя."""
    con = sqlite3.connect('file:%s?mode=ro' % path.replace('\\', '/'), uri=True)
    try:
        out = {}
        for pk, pn, ta, tc, e, ep in con.execute(
                'select dr_pk, parent_nucid, type_a, type_c, energy_num, endpoint from decay_radiations'):
            key_e = e
            if ta in BETA and endpoint_num(ep) is not None:
                key_e = endpoint_num(ep)
            out[pk] = (pn, ta, tc, key_e, e)
        return out
    finally:
        con.close()


def tol(e):
    return max(1.0, 0.002 * abs(e or 0.0))


def judge(base, tree, retired, out):
    u"""Список отказов (строки) по правилам 1–4; out — печать сводки."""
    bad = []
    same = moved = 0
    beta_mean = []
    for pk in sorted(set(base) & set(tree)):
        b, t = base[pk], tree[pk]
        ident_b = (b[0], b[1], b[2] or '')
        ident_t = (t[0], t[1], t[2] or '')
        de = abs((t[3] or 0.0) - (b[3] or 0.0))
        if b[1] in BETA and ident_b == ident_t and abs((t[4] or 0.0) - (b[4] or 0.0)) > tol(b[4]):
            beta_mean.append((pk, b, t))
        if ident_b != ident_t or de > tol(b[3]):
            moved += 1
            if moved <= 20:
                bad.append(u'номер %d переехал: было %s %s %s %s кэВ, стало %s %s %s %s кэВ' % (
                    pk, b[0], b[1], b[2] or u'—', b[3], t[0], t[1], t[2] or u'—', t[3]))
        else:
            same += 1
    if moved > 20:
        bad.append(u'… и ещё %d переехавших номеров' % (moved - 20))
    gone = sorted(set(base) - set(tree))
    new = sorted(set(tree) - set(base))
    undocumented = [pk for pk in gone if pk not in retired]
    for pk in undocumented[:20]:
        b = base[pk]
        bad.append(u'номер %d (%s %s %s %s кэВ) снят без записи в реестре снятых' % (pk, b[0], b[1], b[2] or u'—', b[3]))
    if len(undocumented) > 20:
        bad.append(u'… и ещё %d снятых без записи' % (len(undocumented) - 20))
    low = []
    for pk in new:
        name, lo, hi = ledger.range_of(pk)
        top = max([k for k in base if lo <= k <= hi] + [k for k in retired if lo <= k <= hi and k not in tree] + [lo - 1])
        if pk <= top:
            low.append((pk, name, top))
    for pk, name, top in low[:20]:
        bad.append(u'новый номер %d (%s) не выше прежнего наибольшего %d' % (pk, name, top))
    if len(low) > 20:
        bad.append(u'… и ещё %d новых номеров ниже края' % (len(low) - 20))
    reused = [pk for pk in tree if pk in retired]
    for pk in sorted(reused)[:20]:
        bad.append(u'номер %d стоит в реестре снятых (%s), а в базе есть — переиспользование' % (pk, retired[pk][-1]))
    by_range = {}
    for pk in gone:
        by_range.setdefault(ledger.range_of(pk)[0], 0)
        by_range[ledger.range_of(pk)[0]] += 1
    new_by_range = {}
    for pk in new:
        new_by_range.setdefault(ledger.range_of(pk)[0], 0)
        new_by_range[ledger.range_of(pk)[0]] += 1
    for pk, b, t in beta_mean[:10]:
        out(u'  справка: β-ветвь %d %s %s — средняя %s → %s кэВ при той же границе %s' % (pk, b[0], b[1], b[4], t[4], b[3]))
    out(u'номеров: сохранено %d, переехало %d; снято %d (%s); заведено %d (%s); переиспользовано %d' % (
        same, moved, len(gone), u', '.join(u'%s %d' % kv for kv in sorted(by_range.items())) or u'—', len(new),
        u', '.join(u'%s %d' % kv for kv in sorted(new_by_range.items())) or u'—', len(reused)))
    return bad, gone


def renumbered(tree):
    u"""Положительный контроль: диапазон L-подлиний перенумерован подряд после снятия первой строки."""
    lo, hi = 200001, 299999
    keys = sorted(k for k in tree if lo <= k <= hi)
    if len(keys) < 2:
        keys = sorted(tree)[:50]
        lo = keys[0]
    out = dict((k, v) for k, v in tree.items() if not (keys[0] <= k <= keys[-1]))
    for i, k in enumerate(keys[1:]):
        out[lo + i] = tree[k]
    return out


def main():
    ap = argparse.ArgumentParser(description=u'Постоянство номеров dr_pk nucdb (AMBER281)')
    ap.add_argument('--base', default=None, help=u'база-образец вместо закоммиченной (HEAD)')
    ap.add_argument('--tree', default=TREE)
    a = ap.parse_args()
    if not os.path.isfile(a.tree):
        print(u'⛔ нет базы дерева: %s' % a.tree)
        return 2
    tmp = None
    base_path = a.base
    if base_path is None:
        fd, tmp = tempfile.mkstemp(suffix='.sqlite')
        os.close(fd)
        try:
            with open(tmp, 'wb') as f:
                subprocess.check_call(['git', '-C', ROOT, 'show', 'HEAD:' + REL], stdout=f)
        except (OSError, subprocess.CalledProcessError) as ex:
            os.remove(tmp)
            print(u'⛔ не взять закоммиченную базу (git show HEAD:%s): %s' % (REL, ex))
            return 2
        base_path = tmp
    try:
        print(u'база дерева: %s' % a.tree)
        print(u'образец: %s' % (u'HEAD:' + REL if a.base is None else a.base))
        base = load(base_path)
        tree = load(a.tree)
        retired = ledger.read_retired(ledger.TREE_RETIRED)
        print(u'реестр снятых: %s — %d номеров' % (ledger.TREE_RETIRED, len(retired)))
        bad, gone = judge(base, tree, retired, print)
        for pk in gone[:15]:
            r = retired.get(pk)
            print(u'  снят %d: %s' % (pk, (u'%s %s %s — %s' % (r[1], r[4], r[6], r[-1])) if r else u'НЕТ В РЕЕСТРЕ'))
        if len(gone) > 15:
            srcs = {}
            for pk in gone:
                r = retired.get(pk)
                srcs[r[-1] if r else u'НЕТ В РЕЕСТРЕ'] = srcs.get(r[-1] if r else u'НЕТ В РЕЕСТРЕ', 0) + 1
            print(u'  … всего снято %d; по источнику: %s' % (len(gone), u'; '.join(u'%s — %d' % kv for kv in sorted(srcs.items()))))
        # положительный контроль
        ctrl, _ = judge(tree, renumbered(tree), retired, lambda s: None)
        print(u'положительный контроль (перенумерованный диапазон L-подлиний): %s' % (
            u'ОТКАЗ ПОЙМАН (%d)' % len(ctrl) if ctrl else u'⛔ НЕ ПОЙМАН'))
        if not ctrl:
            print(u'⛔ сторож слеп: перенумерация не дала отказа')
            return 1
        # второй контроль — правило β-ветвей: две ветви одного родителя с разными границами меняются номерами
        # (средняя при этом «своя» у каждой) — отказ обязан быть
        swap = None
        by_parent = {}
        for pk in sorted(tree):
            v = tree[pk]
            if v[1] in BETA and v[3] is not None:
                for q in by_parent.get((v[0], v[1]), []):
                    if abs(tree[q][3] - v[3]) > tol(v[3]):
                        swap = (q, pk)
                        break
                if swap:
                    break
                by_parent.setdefault((v[0], v[1]), []).append(pk)
        if swap is None:
            print(u'⛔ контроль β-ветвей: нет двух ветвей одного родителя с разными границами — мерить нечем')
            return 1
        ctl_tree = dict(tree)
        ctl_tree[swap[0]], ctl_tree[swap[1]] = tree[swap[1]], tree[swap[0]]
        ctrl2, _ = judge(tree, ctl_tree, retired, lambda s: None)
        print(u'положительный контроль β-ветвей (номера %d и %d обменялись ветвями): %s' % (
            swap[0], swap[1], u'ОТКАЗ ПОЙМАН (%d)' % len(ctrl2) if ctrl2 else u'⛔ НЕ ПОЙМАН'))
        if not ctrl2:
            print(u'⛔ сторож слеп: обмен β-ветвей не дал отказа')
            return 1
        if bad:
            for line in bad:
                print(u'⛔ ' + line)
            print(u'ОТКАЗ: номера dr_pk не держатся (%d)' % len(bad))
            return 1
        print(u'НОМЕРА dr_pk ДЕРЖАТСЯ: ни один номер не переехал и не переиспользован, снятые — в реестре.')
        return 0
    finally:
        if tmp:
            os.remove(tmp)


if __name__ == '__main__':
    sys.exit(main())
