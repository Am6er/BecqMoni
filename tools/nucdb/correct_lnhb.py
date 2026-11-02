# -*- coding: utf-8 -*-
u"""Сверка `nucdb.decay_radiations` с таблицами LNHB/DDEP и правка по ним (`AMBER274`, Э1,
01.11.2026, полоса `purebeta`).

ЗАЧЕМ. Критики этапа Э1 (журнал `handover/handover-2026-10-29-amber274-e1-sample-beta.md`,
«Исправления по критикам», Ф3; «Критики несходимостей») нашли расхождения поставки NuDat с
оценками LNHB у нуклидов, чья бета ложится в образ FSA: граница главной ветви Pa-234m 2290
против 2269 кэВ LNHB; у Ac-228 границы ветвей на ~10 кэВ выше LNHB, и нет ветвей 535 и 826 кэВ;
выходы ветвей и линий Cs-137, K-40, Pb-212, Bi-214 расходятся с LNHB. Решение Amber 30.10.2026
вопросником, дословно: **«Сверить и поправить по LNHB (Рекомендую)»** — её слово на ЗАПИСЬ в
`nucdb` этих расхождений (выходы гамма-линий сдвигают числа корпуса — общий счёт распорядителя).

ЧТО ДЕЛАЕТСЯ — ровно названное, ничего сверх:

  * β⁻ шести нуклидов (Pa-234m, Ac-228, Cs-137, K-40, Pb-212, Bi-214) — НАБОРОМ LNHB ЦЕЛИКОМ:
    строки `B-` уровня родителя (`DecayParentRule.LevelClause` — то же выражение, читается из исходника
    через `chains.py`) снимаются, вместо них — ветви LNHB: граница («max»), вероятность,
    средняя («avg» из «Electron Emissions»), погрешности в единицах последнего знака (как у
    поставки ENSDF). Набор, а не построчная правка: у Ac-228 и Bi-214 наборы ветвей у NuDat и
    LNHB разные (53 и 66 ветвей у LNHB против 21 и 26), и сопоставлять их по уровню нечем;
  * β⁺ K-40 (единственная β⁺ из шести) — правка строки на месте: вероятность и средняя по LNHB;
    граница строки — энергия захвата на уровень (1504.40 кэВ, соглашение поставки), у LNHB та же;
  * γ Cs-137, K-40, Pb-212 (до 02.11.2026 и Bi-214) — ВЫХОДЫ линий, сопоставленных с LNHB по энергии
    (|ΔE| ≤ max(0.6 кэВ, 5·10⁻⁴·E), ближайшая, один к одному), — на место; линии LNHB без пары в
    базе с выходом ≥ 0.01 % (пол полноты базы, `AMBER151`) — добавляются; строки базы без пары в
    LNHB не трогаются (печатаются). Энергии линий не меняются. Аннигиляция (`γ±`) не берётся:
    её линия у β⁺-излучателя считается из строк `B+` (`FsaSampleLibrary.DecayLines`).
  * γ Pa-234m и Ac-228 (`AMBER274`, полоса `dbdata`, 02.11.2026; решение Amber 02.11.2026
    вопросником, дословно: **«Править по LNHB (Рекомендую)»** — её слово на запись этих
    гамма-выходов) — НАБОРОМ LNHB (режим `set`): записи LNHB одной энергии (ближе 0.05 кэВ —
    правило добора слабых гамм) сливаются в одну линию (выходы сложены, погрешности — в
    квадратуре, в единицах последнего знака суммы), сопоставление то же (|ΔE| ≤ max(0.6 кэВ,
    5·10⁻⁴·E), ближайшая, один к одному; две линии LNHB на одну строку базы — останов кодом 1);
    у сопоставленной строки по LNHB — И ЭНЕРГИЯ, И ВЫХОД (текст, погрешность, число); линия
    LNHB без пары с выходом ≥ 0.01 % — добавляется; строка ДОБОРА (`dr_pk` 100001…199999,
    ENSDF2 ЛСРМ 1997 г., `import_weak_gammas.py`) без пары в LNHB — СНИМАЕТСЯ: добор заполнял
    линии, срезанные порогом NuDat, а у этих двух родителей полный набор даёт LNHB (Ac-228:
    327.44 кэВ рядом с 328.00, 674.16 — верхний предел ENSDF «LE», взятый добором числом,
    853.17 — та же линия, что 853.96 LNHB); строка ПОСТАВКИ без пары остаётся (печатается).
    Шаг идёт ПОСЛЕ всех ветвей и гамм прочих родителей — номера `dr_pk` строк первого
    захода (300001…300157) не сдвигаются.

Новые строки — `dr_pk` от 300001 (у поставки ≤ 66290, добор слабых гамм 100001…199999,
L-подлинии ≥ 200001): генератор корпуса берёт только поставку (`chains.SUPPLY_ONLY`), и
добавленное им не видно, как и добор. Повторный запуск даёт то же содержимое: свои строки
(`dr_pk` 300001…399999 названных родителей) снимаются и пишутся заново ПОД ТЕМИ ЖЕ НОМЕРАМИ (`PkAlloc`,
`AMBER281`, 02.11.2026: номер строки постоянен — правка на месте, новая выше края, снятая — в реестр снятых
`data/nucdb_dr_pk_retired.tsv`; `--retired=` — свой реестр для копии базы).

  * γ Bi-214 (`AMBER274`, полоса `dbdata2`, 02.11.2026; решение Amber 02.11.2026 вопросником,
    дословно: «Разобрать построчно и снять пределы (Рекомендую)», Bi-214 — правилом LNHB) — тоже
    режим `set`: 5 строк добора без пары в LNHB (Σ 0.147 %) сняты, энергии сопоставленных — по LNHB;
    правится НА СВОЁМ МЕСТЕ (`SET_IN_PLACE`), его добавленные линии — прежние номера 300149…300157.

⛔ ПОРЯДОК ИНСТРУМЕНТОВ (с 02.11.2026): `import_weak_gammas.py` → этот скрипт →
`import_l_sublines.py`. Повторный добор снимает свои строки 100001…199999 и пишет их заново — в
том числе поправленные здесь выходы и снятые строки Ac-228/Pa-234m/Bi-214, — и после него этот
скрипт обязан пройти снова. L-подлинии читают гамма-строки всех диапазонов (конверсия), поэтому
идут последними; пересчитаны у всех 02.11.2026 (полоса `dbdata2`, решение Amber «Пересчитать у всех»).

СВЕРКА (`--check`): тот же прогон на копии базы; расхождение — строка родителя, которой
после прогона нет или которая появилась (все колонки), печатается по нуклидам; код 1, если
расхождения есть. `--table=<файл>` — таблица «линия — было — стало — LNHB» по всем
тронутым гамма-строкам режима `set` (TSV).

ПРИЁМКА ЧИСЛОМ. До и после — sha256 КАЖДОЙ таблицы базы (строки по `rowid`, значения с типом);
меняться вправе только `decay_radiations`, иначе останов и откат. Итог «было → стало» — по
ветвям и линиям.

⚠ Граница ветви — ещё и ключ формы таблицей (`beta_shapes.endpoint_kev`, допуск читателя
max(2 кэВ, 0.5 %)): у Pa-234m (2290 → 2269) и Cs-137 (514.03 → 513.97, 1176 → 1175.63) после
правки форму надо переписать `tools/nucdb/import_beta_shapes.py --apply` (там границы уже
LNHB) — скрипт это проверяет и называет.

Данные — `tools/nucdb/data/lnhb/<нуклид>_beta.tsv` / `_gamma.tsv` (шапка: адрес таблицы LNHB,
sha256 PDF, оценщики и даты), сняты с PDF текстом (pymupdf), десятичная запятая → точка.

    python tools/nucdb/correct_lnhb.py                 # показать, что будет (база не тронута)
    python tools/nucdb/correct_lnhb.py --apply         # записать в BecquerelMonitor/nucdb.sqlite
    python tools/nucdb/correct_lnhb.py --db=<путь> [--apply] [--table=<файл.tsv>]
    python tools/nucdb/correct_lnhb.py --check [--db=<путь>]   # сверка: код 1 — база расходится с LNHB

Коды возврата: 0 — сделано (или показано); 1 — останов (меняется не та таблица, нет данных,
неоднозначность сопоставления); 2 — неверный вызов.
"""
from __future__ import print_function
import argparse
import hashlib
import io
import math
import os
import re
import shutil
import sqlite3
import sys
import tempfile

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DATA = os.path.join(HERE, 'data', 'lnhb')
DEFAULT_DB = os.path.join(ROOT, 'BecquerelMonitor', 'nucdb.sqlite')

#: имя LNHB, nucid базы, правка гамма-линий: None — нет; 'yield' — выходы сопоставленных
#: (полоса `purebeta`, 01.11.2026); 'set' — набор LNHB: энергии и выходы, дублеты слиты, строки
#: добора без пары сняты (полоса `dbdata`, 02.11.2026, решение Amber «Править по LNHB»)
TARGETS = [
    ('Pa-234m', '234PAm1', 'set'),
    ('Ac-228', '228AC', 'set'),
    ('Cs-137', '137CS', 'yield'),
    ('K-40', '40K', 'yield'),
    ('Pb-212', '212PB', 'yield'),
    ('Bi-214', '214BI', 'set'),
]

#: (`AMBER274`, полоса `dbdata2`, 02.11.2026) Bi-214 — тоже набором LNHB (режим `set`): у него 5 строк
#: добора без пары в LNHB, Σ 0.147 % (351.9 кэВ 0.069 % и др.), которые режим `yield` оставлял. Решение
#: Amber 02.11.2026 вопросником, дословно: «Разобрать построчно и снять пределы (Рекомендую)» — Bi-214
#: правилом LNHB (`set`). Набор Bi-214 правится НА СВОЁМ МЕСТЕ (сразу за его ветвями β⁻, где стояли
#: добавленные линии режима `yield`), а не в отложенном шаге: так его добавленные линии занимают прежние
#: номера, и номера `dr_pk` добавленных линий Ac-228 (300158…) не сдвигаются.
SET_IN_PLACE = ('214BI',)

FIRST_PK = 300001
LAST_PK = 399999
GAMMA_FLOOR_PERCENT = 0.01      # пол полноты базы (`AMBER151`)
WEAK_FIRST_PK = 100001          # добор слабых гамм `import_weak_gammas.py` (`AMBER151`)
WEAK_LAST_PK = 199999
SAME_LINE_KEV = 0.05            # записи ближе — одна линия (правило добора, `import_weak_gammas.py`)

#: (`S89`, `T74`) уровень родителя — ТЕМ ЖЕ выражением, что у приложения: текст
#: `DecayParentRule.LevelClause` читается из исходника через `chains.py` корпуса (копии
#: правила здесь нет — сторож `check_parent_rule.py`); параметр — `$n`.
sys.path.insert(0, os.path.join(ROOT, 'tools', 'CORPUS', 'scripts'))
from chains import LEVEL_CLAUSE, LEVEL_PARAM  # noqa: E402
sys.path.insert(0, HERE)
import dr_pk_ledger as ledger  # noqa: E402


class PkAlloc(object):
    u"""(`AMBER281`, полоса `dbdata2`, 02.11.2026) Номера своих строк (300001…399999) НЕ перенумеровываются:
    строка прежнего прогона с тем же естественным ключом (родитель, тип, граница ветви у `B-` / энергия у `G`;
    повтор ключа — по порядку `dr_pk`) получает СВОЙ прежний номер, новая — выше наибольшего занятого или снятого
    в диапазоне, не взятая прогоном — в реестр снятых (`dr_pk_ledger.py`). Прежде номера шли подряд от 300001 и
    держались только тем, что порядок шагов не менялся. Решение Amber 02.11.2026, дословно: «в изотоп добавляй
    строку из базы (индекс) чтобы быть точно уверенным в будущем»."""

    def __init__(self, con, own_old, retired):
        self.hw = ledger.high_water(con, FIRST_PK, LAST_PK, retired)
        self.old = dict(ledger.with_occurrence(own_old, self.key_of_row))
        self.seen = {}
        self.taken = set()
        self.retired = []
        self.kept = 0
        self.new = 0

    @staticmethod
    def key_of_row(r):
        # колонки ledger.COLUMNS + dr_pk: 1 родитель, 3 тип, 9 энергия, 11 граница
        return (r[1], r[3], r[11] if r[3] in ('B-', 'B+') else r[9])

    def take(self, keybase):
        n = self.seen.get(keybase, 0)
        self.seen[keybase] = n + 1
        row = self.old.get(keybase + (n,))
        if row is not None and row[-1] not in self.taken:
            self.taken.add(row[-1])
            self.kept += 1
            return row[-1]
        self.hw += 1
        if self.hw > LAST_PK:
            raise SystemExit(u'⛔ диапазон dr_pk %d…%d исчерпан' % (FIRST_PK, LAST_PK))
        self.new += 1
        return self.hw

    def retire(self, con, pk):
        row = con.execute('select %s, dr_pk from decay_radiations where dr_pk = ?' % ', '.join(ledger.COLUMNS),
                          (pk,)).fetchone()
        if row is not None:
            self.retired.append(row)

    def finish(self):
        for k, row in sorted(self.old.items(), key=lambda kv: kv[1][-1]):
            if row[-1] not in self.taken:
                self.retired.append(row)


#: распределитель последнего прогона `correct` — главной, чтобы записать реестр снятых при `--apply`
LAST_ALLOC = []


def read_tsv(path):
    rows, header = [], None
    with io.open(path, encoding='utf-8') as f:
        for line in f:
            line = line.rstrip('\n')
            if not line or line.startswith('#'):
                continue
            cells = line.split('\t')
            if header is None:
                header = cells
                continue
            rows.append(dict(zip(header, cells + [''] * (len(header) - len(cells)))))
    return rows


def table_hashes(con):
    out = {}
    for (name,) in con.execute("select name from sqlite_master where type = 'table' order by name"):
        h = hashlib.sha256()
        try:
            cur = con.execute('select * from "%s" order by rowid' % name)
        except sqlite3.OperationalError:
            cur = con.execute('select * from "%s"' % name)
        n = 0
        for row in cur:
            n += 1
            for v in row:
                if v is None:
                    h.update(b'N|')
                elif isinstance(v, bytes):
                    h.update(b'B' + v + b'|')
                elif isinstance(v, float):
                    h.update(('F%r|' % v).encode('ascii'))
                elif isinstance(v, int):
                    h.update(('I%d|' % v).encode('ascii'))
                else:
                    h.update(('S' + v + '|').encode('utf-8'))
            h.update(b'\n')
        out[name] = (h.hexdigest(), n)
    return out


def level_of(con, nucid):
    row = con.execute('select distinct parent_l_seqno from decay_radiations where parent_nucid = $n' + LEVEL_CLAUSE,
                      {LEVEL_PARAM: nucid}).fetchall()
    if len(row) != 1:
        raise SystemExit(u'⛔ %s: уровень родителя не один: %r' % (nucid, row))
    return row[0][0]


def fnum(text):
    return float(text)


def decimals(text):
    return len(text.split('.')[1]) if '.' in text else 0


def merge_same_energy(lines, report, lnhb):
    """Записи LNHB одной энергии (ближе SAME_LINE_KEV) — одна линия: выходы сложены, погрешность —
    в квадратуре, в единицах последнего знака суммы; энергия — запись сильнейшей. Пределы и γ± —
    вне (их не берёт и режим `yield`)."""
    usable = [x for x in lines if not x['label'].startswith(u'γ±') and not re.search(u'[≈<]', x['intensity'])]
    usable.sort(key=lambda x: fnum(x['energy_kev']))
    groups = []
    for x in usable:
        if groups and abs(fnum(x['energy_kev']) - fnum(groups[-1][-1]['energy_kev'])) <= SAME_LINE_KEV:
            groups[-1].append(x)
        else:
            groups.append([x])
    merged = []
    for g in groups:
        if len(g) == 1:
            merged.append(dict(g[0]))
            continue
        top = max(g, key=lambda x: fnum(x['intensity']))
        dec = max(decimals(x['intensity']) for x in g)
        total = sum(fnum(x['intensity']) for x in g)
        text = '%.*f' % (dec, total)
        if all(x['intensity_unc'] for x in g):
            var = sum((int(x['intensity_unc']) * 10.0 ** -decimals(x['intensity'])) ** 2 for x in g)
            unc = '%d' % max(1, int(round(math.sqrt(var) * 10.0 ** dec)))
        else:
            unc = ''
        m = dict(top)
        m['label'] = u'+'.join(x['label'] for x in g)
        m['intensity'] = text
        m['intensity_unc'] = unc
        merged.append(m)
        report.append(u'    %s γ %s кэВ: записей LNHB одной энергии %d (%s) — одна линия %s (%s) %%'
                      % (lnhb, top['energy_kev'], len(g), u' + '.join(x['intensity'] for x in g), text, unc))
    return merged


def gamma_set(con, lnhb, nucid, lvl, dec_type, alloc, report, table):
    """Режим `set` (Pa-234m, Ac-228, Bi-214): гамма-линии родителя — набором LNHB; номера — распределителем."""
    lines = merge_same_energy(read_tsv(os.path.join(DATA, lnhb + '_gamma.tsv')), report, lnhb)
    g = con.execute("select dr_pk, energy_num, intensity, intensity_unc, intensity_num, dec_type, energy, energy_unc"
                    " from decay_radiations where parent_nucid = $n and type_a = 'G' and energy_num not null"
                    + LEVEL_CLAUSE + " order by dr_pk", {LEVEL_PARAM: nucid}).fetchall()
    decs = {}
    for r in g:
        decs[r[5]] = decs.get(r[5], 0) + 1
    g_dec = max(sorted(decs.items()), key=lambda kv: kv[1])[0] if decs else dec_type
    nearest = {}
    for i, x in enumerate(lines):
        e = fnum(x['energy_kev'])
        tol = max(0.6, 5e-4 * e)
        cand = sorted((abs(r[1] - e), r[0]) for r in g if abs(r[1] - e) <= tol)
        nearest[i] = cand[0][1] if cand else None
    taken = {}
    for i, p in nearest.items():
        if p is not None:
            taken.setdefault(p, []).append(i)
    clash = [(p, v) for p, v in sorted(taken.items()) if len(v) > 1]
    if clash:
        for p, v in clash:
            report.append(u'    ⛔ %s: строка базы %d ближайшая для линий LNHB %s'
                          % (lnhb, p, u', '.join(lines[i]['energy_kev'] for i in v)))
        raise SystemExit(u'⛔ %s: неоднозначность сопоставления (%d строк базы) — останов' % (lnhb, len(clash)))
    by_pk = dict((r[0], r) for r in g)
    changed = same = added = removed = kept = 0
    for i, x in enumerate(lines):
        p = nearest[i]
        yi = fnum(x['intensity'])
        if p is None:
            if yi >= GAMMA_FLOOR_PERCENT:
                pk = alloc.take((nucid, 'G', x['energy_kev']))
                con.execute("insert into decay_radiations (parent_l_seqno, parent_nucid, dec_type, type_a, type_c,"
                            " intensity, intensity_unc, intensity_num, energy_num, energy, energy_unc, endpoint,"
                            " endpoint_unc, dr_pk) values (?, ?, ?, 'G', '', ?, ?, ?, ?, ?, ?, null, null, ?)",
                            (lvl, nucid, g_dec, x['intensity'], x['intensity_unc'], yi, fnum(x['energy_kev']),
                             x['energy_kev'], x['energy_unc'], pk))
                table.append((lnhb, u'добавлена', pk, u'', u'', x['energy_kev'], x['intensity'], x['intensity_unc'], x['label']))
                added += 1
            continue
        r = by_pk[p]
        new = (x['energy_kev'], x['energy_unc'], fnum(x['energy_kev']), x['intensity'], x['intensity_unc'], yi)
        old = (r[6], r[7] or '', r[1], r[2], r[3] or '', r[4])
        if new == old:
            same += 1
            continue
        con.execute("update decay_radiations set energy = ?, energy_unc = ?, energy_num = ?, intensity = ?,"
                    " intensity_unc = ?, intensity_num = ? where dr_pk = ?", new + (p,))
        changed += 1
        table.append((lnhb, u'изменена', p, r[6], u'%s (%s)' % (r[2], r[3] or u''), x['energy_kev'], x['intensity'],
                      x['intensity_unc'], x['label']))
    for r in g:
        if r[0] in taken:
            continue
        if WEAK_FIRST_PK <= r[0] <= WEAK_LAST_PK:
            alloc.retire(con, r[0])
            con.execute('delete from decay_radiations where dr_pk = ?', (r[0],))
            removed += 1
            table.append((lnhb, u'снята', r[0], r[6], u'%s (%s)' % (r[2], r[3] or u''), u'', u'', u'', u'нет в LNHB'))
        else:
            kept += 1
            report.append(u'    %s γ %s кэВ %s %%: строка поставки без пары в LNHB — оставлена' % (lnhb, r[6], r[2]))
    small = sum(1 for i, x in enumerate(lines) if nearest[i] is None and fnum(x['intensity']) < GAMMA_FLOOR_PERCENT)
    report.append(u'%s γ (набор LNHB): линий LNHB %d (после слияния дублетов); энергия/выход правлены у %d, совпали %d,'
                  u' добавлено %d, ниже пола %.2f %% без пары не взято %d; строк добора без пары снято %d, поставки оставлено %d'
                  % (lnhb, len(lines), changed, same, added, GAMMA_FLOOR_PERCENT, small, removed, kept))
    return alloc


def correct(con, out, table=None, retired=None):
    """Правит открытую базу; возвращает список строк отчёта «было → стало». Распределитель номеров прогона —
    `LAST_ALLOC[-1]` (снятые строки — его `retired`)."""
    if table is None:
        table = []
    report = []
    deferred = []
    names = [t[1] for t in TARGETS]
    q = ','.join('?' * len(names))
    # свои строки прежнего прогона — снять и записать заново ПОД ТЕМИ ЖЕ НОМЕРАМИ (`PkAlloc`, `AMBER281`)
    own_old = con.execute('select %s, dr_pk from decay_radiations where dr_pk between ? and ? and parent_nucid in (%s)'
                          ' order by dr_pk' % (', '.join(ledger.COLUMNS), q), [FIRST_PK, LAST_PK] + names).fetchall()
    alloc = PkAlloc(con, own_old, retired or {})
    del LAST_ALLOC[:]
    LAST_ALLOC.append(alloc)
    own = con.execute('select count(*) from decay_radiations where dr_pk between ? and ? and parent_nucid in (%s)' % q,
                      [FIRST_PK, LAST_PK] + names).fetchone()[0]
    con.execute('delete from decay_radiations where dr_pk between ? and ? and parent_nucid in (%s)' % q,
                [FIRST_PK, LAST_PK] + names)
    if own:
        out(u'  снято своих строк прежнего прогона: %d' % own)
    foreign = con.execute('select count(*) from decay_radiations where dr_pk between ? and ?',
                          [FIRST_PK, LAST_PK]).fetchone()[0]
    if foreign:
        raise SystemExit(u'⛔ в диапазоне dr_pk %d…%d есть чужие строки: %d' % (FIRST_PK, LAST_PK, foreign))

    for lnhb, nucid, mode in TARGETS:
        path = os.path.join(DATA, lnhb + '_beta.tsv')
        if not os.path.exists(path):
            raise SystemExit(u'⛔ нет данных: %s' % path)
        betas = read_tsv(path)
        lvl = level_of(con, nucid)
        old = con.execute("select dr_pk, dec_type, intensity, intensity_unc, intensity_num, energy, endpoint, endpoint_unc"
                          " from decay_radiations where parent_nucid = $n and type_a = 'B-'" + LEVEL_CLAUSE
                          + " order by intensity_num desc", {LEVEL_PARAM: nucid}).fetchall()
        dec = sorted(set(r[1] for r in old))
        if len(dec) > 1:
            raise SystemExit(u'⛔ %s: у ветвей β⁻ разные dec_type %r' % (nucid, dec))
        dec_type = dec[0] if dec else '2'
        s_old = sum(r[4] for r in old)
        # снимаются ровно выбранные выше строки (по `dr_pk`): выражение уровня внутри `delete`
        # той же таблицы судило бы по строкам, которые уже сняты
        for r in old:
            alloc.retire(con, r[0])
        con.executemany('delete from decay_radiations where dr_pk = ?', [(r[0],) for r in old])
        new_minus = [b for b in betas if b['type'] == 'B-']
        for b in new_minus:
            pk = alloc.take((nucid, 'B-', b['max_kev']))
            con.execute("insert into decay_radiations (parent_l_seqno, parent_nucid, dec_type, type_a, type_c, intensity,"
                        " intensity_unc, intensity_num, energy_num, energy, energy_unc, endpoint, endpoint_unc, dr_pk)"
                        " values (?, ?, ?, 'B-', '', ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        (lvl, nucid, dec_type, b['prob'], b['prob_unc'], fnum(b['prob']), fnum(b['avg_kev']),
                         b['avg_kev'], b['avg_unc'], b['max_kev'], b['max_unc'], pk))
        s_new = sum(fnum(b['prob']) for b in new_minus)
        if old:
            report.append(u'%s β⁻: %d ветвей Σ %.3f %% → %d ветвей LNHB Σ %.3f %%' % (lnhb, len(old), s_old, len(new_minus), s_new))
        else:
            # повторный прогон: строки LNHB прежнего прогона сняты выше как свои — «было» печатать нечего
            report.append(u'%s β⁻: набор LNHB прежнего прогона переписан заново — %d ветвей Σ %.3f %%'
                          % (lnhb, len(new_minus), s_new))
        top_old = old[:3]
        top_new = sorted(new_minus, key=lambda b: -fnum(b['prob']))[:3]
        report.append(u'    главные было: %s' % u'; '.join(u'E0 %s I %s (ср. %s)' % (r[6], r[2], r[5]) for r in top_old))
        report.append(u'    главные стало: %s' % u'; '.join(u'E0 %s I %s (ср. %s)' % (b['max_kev'], b['prob'], b['avg_kev']) for b in top_new))

        for b in [b for b in betas if b['type'] == 'B+']:
            rows = con.execute("select dr_pk, intensity, intensity_unc, energy from decay_radiations where parent_nucid = $n"
                               " and type_a = 'B+'" + LEVEL_CLAUSE, {LEVEL_PARAM: nucid}).fetchall()
            if len(rows) != 1:
                raise SystemExit(u'⛔ %s: строк β⁺ %d, ожидалась одна' % (nucid, len(rows)))
            con.execute("update decay_radiations set intensity = ?, intensity_unc = ?, intensity_num = ?, energy = ?,"
                        " energy_num = ?, energy_unc = ? where dr_pk = ?",
                        (b['prob'], b['prob_unc'], fnum(b['prob']), b['avg_kev'], fnum(b['avg_kev']), b['avg_unc'], rows[0][0]))
            report.append(u'%s β⁺: I %s (%s), ср. %s → I %s (%s), ср. %s' % (lnhb, rows[0][1], rows[0][2], rows[0][3],
                                                                         b['prob'], b['prob_unc'], b['avg_kev']))

        if mode == 'set' and nucid in SET_IN_PLACE:
            gamma_set(con, lnhb, nucid, lvl, dec_type, alloc, report, table)
            continue
        if mode == 'set':
            # после всех родителей: номера `dr_pk` первого захода (purebeta) не сдвигаются
            deferred.append((lnhb, nucid, lvl, dec_type))
            continue
        if mode != 'yield':
            continue
        lines = read_tsv(os.path.join(DATA, lnhb + '_gamma.tsv'))
        g = con.execute("select dr_pk, energy_num, intensity, intensity_unc, intensity_num, dec_type from decay_radiations"
                        " where parent_nucid = $n and type_a = 'G' and energy_num not null" + LEVEL_CLAUSE,
                        {LEVEL_PARAM: nucid}).fetchall()
        decs = {}
        for r in g:
            decs[r[5]] = decs.get(r[5], 0) + 1
        g_dec = max(decs.items(), key=lambda kv: kv[1])[0] if decs else dec_type
        nearest = {}
        for i, x in enumerate(lines):
            if x['label'].startswith(u'γ±') or re.search(u'[≈<]', x['intensity']):
                continue
            e = fnum(x['energy_kev'])
            tol = max(0.6, 5e-4 * e)
            cand = sorted((abs(r[1] - e), r[0]) for r in g if abs(r[1] - e) <= tol)
            nearest[i] = cand[0][1] if cand else None
        taken = {}
        for i, p in nearest.items():
            if p is not None:
                taken.setdefault(p, []).append(i)
        changed = same = added = 0
        biggest = []
        for i, x in enumerate(lines):
            if i not in nearest:
                continue
            p = nearest[i]
            yi = fnum(x['intensity'])
            if p is not None and len(taken[p]) > 1:
                report.append(u'    ⚠ %s γ %s: строка базы %d ближайшая для %d линий LNHB — не тронута'
                              % (lnhb, x['energy_kev'], p, len(taken[p])))
                continue
            if p is None:
                if yi >= GAMMA_FLOOR_PERCENT:
                    pk = alloc.take((nucid, 'G', x['energy_kev']))
                    con.execute("insert into decay_radiations (parent_l_seqno, parent_nucid, dec_type, type_a, type_c,"
                                " intensity, intensity_unc, intensity_num, energy_num, energy, energy_unc, endpoint,"
                                " endpoint_unc, dr_pk) values (?, ?, ?, 'G', '', ?, ?, ?, ?, ?, ?, null, null, ?)",
                                (lvl, nucid, g_dec, x['intensity'], x['intensity_unc'], yi, fnum(x['energy_kev']),
                                 x['energy_kev'], x['energy_unc'], pk))
                    added += 1
                    report.append(u'    %s γ %s кэВ: в базе нет → добавлена %s %%' % (lnhb, x['energy_kev'], x['intensity']))
                continue
            row = [r for r in g if r[0] == p][0]
            if row[4] == yi and (row[3] or '') == x['intensity_unc']:
                same += 1
                continue
            con.execute("update decay_radiations set intensity = ?, intensity_unc = ?, intensity_num = ? where dr_pk = ?",
                        (x['intensity'], x['intensity_unc'], yi, p))
            changed += 1
            rel = (yi - row[4]) / row[4] * 100.0 if row[4] else float('inf')
            biggest.append((abs(rel) * yi, u'%.3f кэВ %s → %s %% (%+.2f %%)' % (row[1], row[2], x['intensity'], rel)))
        unpaired = len(g) - len([p for p in taken if len(taken[p]) == 1])
        report.append(u'%s γ: линий LNHB %d; выход правлен %d, совпал %d, добавлено %d; строк базы без пары в LNHB %d'
                      % (lnhb, len(nearest), changed, same, added, unpaired))
        for _, text in sorted(biggest, reverse=True)[:6]:
            report.append(u'    ' + text)
    for lnhb, nucid, lvl, dec_type in deferred:
        gamma_set(con, lnhb, nucid, lvl, dec_type, alloc, report, table)
    alloc.finish()
    report.append(u'номера dr_pk своих строк: сохранено %d, заведено %d; снято строк (в реестр снятых) %d'
                  % (alloc.kept, alloc.new, len(alloc.retired)))
    return report


def shape_check(con, out):
    """beta_shapes: у каждой строки ровно одна ветвь базы в допуске читателя и граница та же."""
    bad = 0
    if not con.execute("select count(*) from sqlite_master where name = 'beta_shapes'").fetchone()[0]:
        return 0
    for sid, nucid, typ, e in con.execute('select id, parent_nucid, type_a, endpoint_kev from beta_shapes order by id').fetchall():
        rows = con.execute('select endpoint from decay_radiations where parent_nucid = $n and type_a = $t' + LEVEL_CLAUSE,
                           {LEVEL_PARAM: nucid, 't': typ}).fetchall()
        vals = []
        for (text,) in rows:
            m = re.search(r'\d+(\.\d*)?', text or '')
            if m:
                vals.append(float(m.group(0)))
        near = [v for v in vals if abs(v - e) <= max(2.0, 0.005 * e)]
        if len(near) != 1 or near[0] != e:
            bad += 1
            out(u'  ⚠ beta_shapes #%d %s %.2f кэВ: ветви базы в допуске %r — перепишите формы'
                u' `tools/nucdb/import_beta_shapes.py --apply`' % (sid, nucid, e, near))
    return bad


def parent_rows(con):
    """Строки названных родителей (все колонки) — множество, для сверки «до/после»."""
    names = [t[1] for t in TARGETS]
    out = {}
    for n in names:
        out[n] = set(con.execute('select * from decay_radiations where parent_nucid = ?', (n,)).fetchall())
    return out


def check(db):
    """Сверка: прогон на копии; расхождение — строка родителя, которой после прогона нет или которая появилась."""
    fd, work = tempfile.mkstemp(suffix='.sqlite')
    os.close(fd)
    shutil.copyfile(db, work)
    con = sqlite3.connect(work)
    try:
        before = parent_rows(con)
        con.execute('begin')
        correct(con, print)
        after = parent_rows(con)
        con.execute('rollback')
    finally:
        con.close()
        os.remove(work)
    total = 0
    print(u'сверка %s с LNHB (строк родителя: было / стало по LNHB / расходится):' % db)
    for lnhb, nucid, mode in TARGETS:
        gone = before[nucid] - after[nucid]
        new = after[nucid] - before[nucid]
        kinds = {}
        for r in gone | new:
            kinds[r[3]] = kinds.get(r[3], 0) + 1
        n = len(gone) + len(new)
        total += n
        print(u'  %-8s %-8s γ %-5s %4d / %4d  расходится %4d%s' % (lnhb, nucid, mode or u'—', len(before[nucid]), len(after[nucid]), n,
              (u'  (' + u', '.join(u'%s %d' % kv for kv in sorted(kinds.items())) + u')') if kinds else u''))
    print(u'СВЕРКА: %s' % (u'0 расхождений' if total == 0 else u'РАСХОЖДЕНИЙ %d' % total))
    return 0 if total == 0 else 1


def main():
    ap = argparse.ArgumentParser(description=u'Сверка nucdb с LNHB и правка (AMBER274)')
    ap.add_argument('--db', default=DEFAULT_DB)
    ap.add_argument('--apply', action='store_true', help=u'записать в базу (иначе — копия во временном файле)')
    ap.add_argument('--check', action='store_true', help=u'сверка: код 1, если база расходится с LNHB')
    ap.add_argument('--table', default=None, help=u'TSV «линия — было — стало — LNHB» гамма-строк режима set')
    ap.add_argument('--retired', default=None, help=u'реестр снятых dr_pk (`dr_pk_ledger.py`; по умолчанию — дерева'
                                                     u' для базы дерева, иначе <база>.retired.tsv)')
    a = ap.parse_args()
    retired_path = ledger.retired_path_for(a.db, a.retired)
    if not os.path.exists(a.db):
        print(u'⛔ нет базы: %s' % a.db)
        return 2
    if a.check:
        if a.apply:
            print(u'⛔ --check и --apply вместе не зовутся')
            return 2
        return check(a.db)

    if a.apply:
        work = a.db
    else:
        fd, work = tempfile.mkstemp(suffix='.sqlite')
        os.close(fd)
        shutil.copyfile(a.db, work)
    con = sqlite3.connect(work)
    try:
        before = table_hashes(con)
        con.execute('begin')
        rows = []
        lines = correct(con, print, rows, ledger.read_retired(retired_path))
        after_tx = table_hashes(con)
        changed = sorted(t for t in before if before[t] != after_tx.get(t))
        if changed not in ([], ['decay_radiations']):
            con.execute('rollback')
            print(u'⛔ изменились не те таблицы: %r — откат' % changed)
            return 1
        con.execute('commit')
        if not changed:
            print(u'база уже исправлена: содержимое после прогона то же до бита (повторный прогон)')
        for line in lines:
            print(line)
        if a.table:
            with io.open(a.table, 'w', encoding='utf-8', newline='\n') as f:
                f.write(u'нуклид\tчто\tdr_pk\tE было\tI было (погр.)\tE стало\tI стало\tпогр.\tлиния LNHB\n')
                for r in sorted(rows, key=lambda r: (r[0], fnum(r[5] or r[3]))):
                    f.write(u'\t'.join(u'%s' % v for v in r) + u'\n')
            print(u'таблица гамма-строк режима set: %s (%d строк)' % (a.table, len(rows)))
        print(u'')
        print(u'sha256 таблиц (до → после; строк):')
        after = table_hashes(con)
        for t in sorted(before):
            mark = u'изменена' if before[t] != after[t] else u'та же'
            print(u'  %-26s %s → %s  %d → %d  %s' % (t, before[t][0][:16], after[t][0][:16], before[t][1], after[t][1], mark))
        bad = shape_check(con, print)
        print(u'')
        if a.apply and changed:
            n_ret = ledger.append_retired(retired_path, LAST_ALLOC[-1].retired, u'correct_lnhb.py: набор LNHB')
            print(u'в реестр снятых (%s) записано: %d' % (retired_path, n_ret))
        print((u'ЗАПИСАНО в %s' % a.db) if a.apply else u'ПОКАЗАНО (база не тронута; запись — ключ --apply)')
        if bad:
            print(u'⚠ формы таблицей расходятся с границами базы: %d — перепишите beta_shapes' % bad)
        return 0
    finally:
        con.close()
        if not a.apply:
            os.remove(work)


if __name__ == '__main__':
    sys.exit(main())
