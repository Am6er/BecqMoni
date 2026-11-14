# -*- coding: utf-8 -*-
u"""Номер строки `nucdb.decay_radiations.dr_pk` живёт столько же, сколько сама строка (`AMBER281`, полоса
`dbdata2`, 02.11.2026).

ЗАЧЕМ. Решение Amber 02.11.2026, дословно: «в изотоп добавляй строку из базы (индекс) чтобы быть точно уверенным
в будущем» — линии библиотеки нуклидов хранят `dr_pk` строки базы, из которой взяты. Значит, номер не смеет ни
переехать на другую строку, ни достаться новой строке после снятия старой. Прежде `import_l_sublines.py` и
`correct_lnhb.py` снимали свой диапазон и вставляли строки заново подряд: при пересчёте подлиний номера сдвигались
у всех строк после первой вставленной, и ссылка указала бы на чужую строку.

ПРАВИЛО (для всех инструментов, пишущих `decay_radiations`):

  * строка, которая уже есть (естественный ключ инструмента, повтор ключа — по порядку `dr_pk`), правится НА
    МЕСТЕ — номер тот же;
  * новая строка получает номер выше НАИБОЛЬШЕГО из занятых и снятых в своём диапазоне (`RANGES`);
  * снятая строка уходит в реестр снятых `data/nucdb_dr_pk_retired.tsv` (номер, что за строка, кто и почему
    снял); её номер больше не выдаётся;
  * повторный прогон инструмента — та же база до бита и ни одной новой записи реестра.

Сторож — `tools/check_nucdb_pk_stable.py` (база дерева против закоммиченной).

Реестр снятых для базы дерева — `tools/nucdb/data/nucdb_dr_pk_retired.tsv`; для копии базы (`--nucdb=<копия>`) —
файл рядом с ней `<копия>.retired.tsv`, чтобы опыты на копиях не писали в реестр дерева (явный путь — ключом
инструмента).
"""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
TREE_NUCDB = os.path.join(ROOT, 'BecquerelMonitor', 'nucdb.sqlite')
TREE_RETIRED = os.path.join(HERE, 'data', 'nucdb_dr_pk_retired.tsv')

#: диапазоны номеров по происхождению строки (`database/scheme.md` §2)
RANGES = (
    (u'поставка NuDat', 1, 100000),
    (u'добор слабых гамм', 100001, 199999),
    (u'L-подлинии', 200001, 299999),
    (u'строки LNHB', 300001, 399999),
    # (`AMBER274` Э3, полоса `a274n`, 14.11.2026) альфы и β⁻ ряда Ac-227 по LNHB, `correct_lnhb_alpha.py`
    (u'альфы ряда Ac-227 по LNHB', 400001, 499999),
)
COLUMNS = ('parent_l_seqno', 'parent_nucid', 'dec_type', 'type_a', 'type_c', 'intensity', 'intensity_unc',
           'intensity_num', 'energy_num', 'energy', 'energy_unc', 'endpoint', 'endpoint_unc')
HEADER = u'dr_pk\tparent_nucid\tparent_l_seqno\tdec_type\ttype_a\ttype_c\tenergy\tintensity\tисточник\n'


def range_of(pk):
    for name, lo, hi in RANGES:
        if lo <= pk <= hi:
            return name, lo, hi
    return u'вне диапазонов', pk, pk


def retired_path_for(db_path, explicit=None):
    if explicit:
        return explicit
    if os.path.normcase(os.path.abspath(db_path)) == os.path.normcase(os.path.abspath(TREE_NUCDB)):
        return TREE_RETIRED
    return db_path + '.retired.tsv'


def read_retired(path):
    out = {}
    if not os.path.isfile(path):
        return out
    with io.open(path, encoding='utf-8') as f:
        for line in f:
            if line.startswith('#') or line.startswith('dr_pk') or not line.strip():
                continue
            cells = line.rstrip('\n').split('\t')
            out[int(cells[0])] = cells
    return out


def append_retired(path, rows, source):
    u"""rows — кортежи `select <COLUMNS>, dr_pk`; уже записанные номера не повторяются."""
    have = read_retired(path)
    new = [r for r in rows if r[-1] not in have]
    if not new:
        return 0
    fresh = not os.path.isfile(path)
    with io.open(path, 'a', encoding='utf-8', newline='\n') as f:
        if fresh:
            f.write(u'# Реестр снятых номеров nucdb.decay_radiations.dr_pk (AMBER281): номер больше не выдаётся.\n')
            f.write(u'# Пишут инструменты tools/nucdb при --apply; читает сторож tools/check_nucdb_pk_stable.py.\n')
            f.write(HEADER)
        for r in sorted(new, key=lambda r: r[-1]):
            d = dict(zip(COLUMNS + ('dr_pk',), r))
            f.write(u'%d\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' % (
                d['dr_pk'], d['parent_nucid'], d['parent_l_seqno'], d['dec_type'], d['type_a'], d['type_c'] or u'',
                d['energy'] or u'', d['intensity'] or u'', source))
    return len(new)


def high_water(con, lo, hi, retired):
    u"""Наибольший номер, занятый или снятый, в диапазоне [lo, hi]; lo − 1, если пусто."""
    occ = con.execute('select max(dr_pk) from decay_radiations where dr_pk between ? and ?', (lo, hi)).fetchone()[0]
    ret = max([k for k in retired if lo <= k <= hi] or [lo - 1])
    return max(occ if occ is not None else lo - 1, ret)


def with_occurrence(items, keyfn):
    u"""[(ключ + номер повтора, элемент)] — повтор ключа нумеруется по порядку элементов."""
    seen = {}
    out = []
    for it in items:
        k = keyfn(it)
        n = seen.get(k, 0)
        seen[k] = n + 1
        out.append((k + (n,), it))
    return out


def sync(con, existing, planned, keyfn_existing, lo, hi, retired):
    u"""Свести лежащие строки с планом без перенумерации.

    existing — строки `select <COLUMNS>, dr_pk` (по возрастанию `dr_pk`); planned — [(ключ, значения COLUMNS)]
    в порядке плана (ключ плана — тот же естественный ключ, что keyfn_existing даёт лежащей строке). Возвращает
    (keep, update, insert, delete): keep/update — [(dr_pk, значения)], insert — [(новый dr_pk, значения)]
    (номера выше наибольшего занятого или снятого в [lo, hi]), delete — строки как в existing. Ничего не пишет."""
    ex = dict(with_occurrence(existing, keyfn_existing))
    keep, update, insert = [], [], []
    used = set()
    hw = high_water(con, lo, hi, retired)
    for key, item in with_occurrence(planned, lambda it: it[0]):
        vals = item[1]
        row = ex.get(key)
        if row is None:
            hw += 1
            if hw > hi:
                raise RuntimeError(u'диапазон dr_pk %d…%d исчерпан' % (lo, hi))
            insert.append((hw, vals))
            continue
        used.add(key)
        if tuple(row[:-1]) == tuple(vals):
            keep.append((row[-1], vals))
        else:
            update.append((row[-1], vals))
    delete = [row for key, row in ex.items() if key not in used]
    delete.sort(key=lambda r: r[-1])
    return keep, update, insert, delete


def execute(cur, update, insert, delete):
    sets = ', '.join('%s = ?' % c for c in COLUMNS)
    for pk, vals in update:
        cur.execute('update decay_radiations set %s where dr_pk = ?' % sets, tuple(vals) + (pk,))
    for pk, vals in insert:
        cur.execute('insert into decay_radiations (%s, dr_pk) values (%s)' % (', '.join(COLUMNS), ', '.join('?' * (len(COLUMNS) + 1))),
                    tuple(vals) + (pk,))
    for row in delete:
        cur.execute('delete from decay_radiations where dr_pk = ?', (row[-1],))
