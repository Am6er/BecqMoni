# -*- coding: utf-8 -*-
u"""Альфы и β⁻-ветви ряда Ac-227 в `nucdb.decay_radiations` — по LNHB/DDEP (`AMBER274` Э3, полоса `a274n`,
14.11.2026).

ЗАЧЕМ. Критик `crit274a` (журнал `handover/handover-2026-11-14-a274a.md` §9 (а)) нашёл неполноту выходов альф
ряда Ac-227 в базе: Σ выходов α Ra-223 97.8 %, Th-227 99.16 %, Rn-219 99.8 %; у Ac-227 β 99.0 % + α 1.413 % =
100.41 %. Отсюда 4.969 альфы на распад головы ряда вместо ≈ 4.997, и счёт ряда по альфам (`CountByAlphas`) завышен
≈ +0.6 %. Решение Amber 14.11.2026 вопросником, дословно: **«Поправить nucdb по LNHB»** — её слово на ЗАПИСЬ.

ЧТО ДЕЛАЕТСЯ — только названное (второй круг 14.11.2026 — по вердикту критика `crit274n` и решениям Amber того
же дня, вопросником, дословно: Th-227 — «LNHB (файл цепочки) (Рекомендую)»; β⁻ Ac-227 — «Нормировать к 98.62 %
(Рекомендую)»; Pb-211 β⁻ — «Да, по LNHB (Рекомендую)»; граница Tl-207 — «Новая строка 1418 (Рекомендую)»):

  * α (строки `type_a = 'A'` уровня родителя, `DecayParentRule.LevelClause`) Ac-227, Ra-223, Rn-219, Po-215,
    Bi-211, Po-211 — по разделу LNHB «α Emissions» (`data/lnhb/<нуклид>_alpha.tsv`, снимает
    `lnhb_alpha_extract.py`), Th-227 — по машинному файлу ЦЕПОЧКИ LNHB `Ac-227.lara.txt` (своей таблицы у Th-227
    нет; копия строк — `data/lnhb/Ac-227.lara.Th-227.txt`, TSV — `lnhb_alpha_extract.py --lara`): строка
    поставки, у которой есть пара в LNHB (|ΔE| ≤ max(0.6 кэВ, 5·10⁻⁴·E), ближайшая, один к одному), правится НА
    МЕСТЕ — энергия, выход, погрешности по LNHB (номер `dr_pk` тот же); линия LNHB без пары — ДОБАВЛЯЕТСЯ своей
    строкой; строка поставки без пары — не трогается и считается расхождением (`--check` — код 1). Пола выхода
    нет: смысл правки — полнота суммы, а слабые линии LNHB несут свой признак (`AP` у ≈/∼);
  * β⁻ Ac-227, Tl-207, Pb-211 — ветви LNHB (граница, вероятность, средняя). Строка поставки — пара ветви LNHB по
    границе в допуске max(15 кэВ, 1 %) (одна ветвь при разных оценках границы); сдвиг границы в допуске сторожа
    номеров (`AMBER281`, max(1 кэВ, 0.2 %)) — правка НА МЕСТЕ; больше — НОВАЯ строка, а прежняя ОСТАЁТСЯ в базе
    как была и записывается заменённой в таблицу `decay_radiations_superseded` (`dr_pk` → `by_dr_pk`, источник,
    примечание; таблицу заводит этот скрипт). Решение Amber 14.11.2026 вопросником (третий круг), дословно:
    **«Строка остаётся, отсев (Рекомендую)»** — правило 02.11.2026 «исходные данные с признаком не удалять —
    отсеивать у читателей»; читатели β отсеивают заменённые довеском `DecayParentRule.NotSupersededClause`
    (сторож — `tools/check_nucdb_superseded.py`). Это Tl-207: 1431 кэВ (поставка, 42767) → 1418(5) кэВ LNHB. Ветвь LNHB без пары —
    новая строка; предел («<», признак `LT`) — не ветвь и не берётся (Tl-207 β⁻0,1 < 0.00008 %, Pb-211 β⁻0,2
    600 кэВ < 0.09 %; печатается);
  * Ac-227 β⁻: вероятности LNHB главных ветвей приближённые — «≈53, ≈35, ≈10» (Σ с β⁻0,3 = 98.3 %) при β⁻-моде
    LNHB 98.62 % (`l_decays` — то же). Ключ `--ac227-beta=norm` (умолчание): ЧИСЛО выхода (`intensity_num`)
    приближённых ветвей умножено на (98.62 − 0.3)/98, ТЕКСТ (`intensity`) остаётся как у LNHB («53», «35», «10»)
    с признаком `AP` — так устроена поставка (Bi-211: текст «83.54», число 83.5387948); `printed` — как напечатано.

Новые строки — `dr_pk` 400001…499999 (диапазон «альфы и β ряда Ac-227 по LNHB», `dr_pk_ledger.RANGES`). Повторный
прогон даёт ту же базу до бита: строки поставки уже совпадают с LNHB, свои строки сводятся с планом без
перенумерации (`dr_pk_ledger.sync`), свою строку, которой больше нет в плане, — в реестр снятых.

ПРИЁМКА ЧИСЛОМ. До и после — sha256 КАЖДОЙ таблицы базы; меняться вправе только `decay_radiations`, иначе
останов и откат. Итог — Σ выходов α и β⁻ по родителям до и после.

    python tools/nucdb/correct_lnhb_alpha.py                    # показать (база не тронута)
    python tools/nucdb/correct_lnhb_alpha.py --apply            # записать в BecquerelMonitor/nucdb.sqlite
    python tools/nucdb/correct_lnhb_alpha.py --db=<путь> [--apply] [--table=<файл.tsv>] [--ac227-beta=printed]
    python tools/nucdb/correct_lnhb_alpha.py --check [--db=<путь>]   # код 1 — база расходится с LNHB

`--check` печатает и известное: пределы LNHB, которые не берутся, и строки поставки без пары (их — код 1).

Коды: 0 — сделано (или показано, или сверка сошлась); 1 — останов или расхождение; 2 — неверный вызов.
"""
from __future__ import print_function
import argparse
import io
import os
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
sys.path.insert(0, HERE)
import dr_pk_ledger as ledger  # noqa: E402
from correct_lnhb import table_hashes, read_tsv, level_of  # noqa: E402
# (`S89`, `T74`) уровень родителя — ТЕМ ЖЕ выражением, что у приложения (`DecayParentRule.LevelClause`
# читается из исходника через `chains.py`); своей копии правила здесь нет — сторож `check_parent_rule.py`.
sys.path.insert(0, os.path.join(ROOT, 'tools', 'CORPUS', 'scripts'))
from chains import LEVEL_CLAUSE, LEVEL_PARAM  # noqa: E402

#: имя LNHB, nucid базы
ALPHA_TARGETS = [
    ('Ac-227', '227AC'),
    ('Ra-223', '223RA'),
    ('Rn-219', '219RN'),
    ('Po-215', '215PO'),
    ('Bi-211', '211BI'),
    ('Po-211', '211PO'),
    ('Th-227', '227TH'),      # (второй круг) файл цепочки `Ac-227.lara.txt`
]
#: β⁻ — ветви LNHB
BETA_TARGETS = [
    ('Ac-227', '227AC'),
    ('Tl-207', '207TL'),
    ('Pb-211', '211PB'),      # (второй круг)
]
#: все родители, которых касается скрипт (сверка `--check` и итог)
PARENTS = ['227AC', '227TH', '223RA', '219RN', '215PO', '211BI', '211PB', '207TL', '211PO']

FIRST_PK, LAST_PK = 400001, 499999
SUPERSEDED = 'decay_radiations_superseded'
SUPERSEDED_SCHEMA = (u'CREATE TABLE decay_radiations_superseded (\n'
                     u'        dr_pk     integer not null primary key,\n'
                     u'        by_dr_pk  integer not null,\n'
                     u'        source    text not null,\n'
                     u'        note      text not null\n'
                     u'    )')
SUPERSEDED_SOURCE = (u'correct_lnhb_alpha.py (AMBER274 Э3, полоса a274n): LNHB/DDEP; решение Amber 14.11.2026 '
                     u'«Строка остаётся, отсев (Рекомендую)»')
SUPPLY_LAST_PK = 100000
AC227_BETA_MODE_PERCENT = 98.62     # LNHB, вероятность β⁻-моды Ac-227 (LARA; `l_decays` базы — то же)
ALPHA_DEC = '0'
BETA_DEC = '2'


def tol_alpha(e):
    return max(0.6, 5e-4 * e)


def tol_beta(e):
    # та же мерка, что у сторожа номеров для границы β-строки (`check_nucdb_pk_stable.tol`)
    return max(1.0, 0.002 * e)


def tol_beta_pair(e):
    # пара строки поставки и ветви LNHB — та же ветвь при другой оценке границы (Tl-207: 1431 против 1418)
    return max(15.0, 0.01 * e)


def fnum(text):
    try:
        return float(str(text).strip('()'))
    except (TypeError, ValueError):
        return None


def unc_of(unc, flag):
    if flag:
        return flag
    return unc or ''


def tsv(path):
    u"""Строки TSV; пустое поле в файле — «-» (без хвостовых пробелов)."""
    return [dict((k, '' if v == '-' else v) for k, v in r.items()) for r in read_tsv(path)]


def lnhb_alphas(name):
    rows = tsv(os.path.join(DATA, name + '_alpha.tsv'))
    out = []
    for r in rows:
        out.append({'label': r['label'], 'e': float(r['energy_kev']), 'e_txt': r['energy_kev'],
                    'e_unc': r['energy_unc'], 'i': float(r['prob']), 'i_txt': r['prob'],
                    'i_unc': unc_of(r['prob_unc'], r['flag'])})
    return out


def lnhb_betas(name, ac227_mode, notes):
    rows = tsv(os.path.join(DATA, name + '_beta.tsv'))
    out = []
    for r in rows:
        if r['flag'] == 'LT':
            # предел — не ветвь (Tl-207 β⁻0,1 < 0.00008 %, Pb-211 β⁻0,2 < 0.09 %)
            notes.append(u'%s β⁻: %s граница %s кэВ — предел LNHB «< %s %%», не берётся' % (name, r['label'], r['max_kev'], r['prob']))
            continue
        out.append({'label': r['label'], 'max': float(r['max_kev']), 'max_txt': r['max_kev'],
                    'max_unc': r['max_unc'], 'i': float(r['prob']), 'i_txt': r['prob'],
                    'i_unc': unc_of(r['prob_unc'], r['flag']), 'avg': float(r['avg_kev']),
                    'avg_txt': r['avg_kev'], 'avg_unc': r['avg_unc'], 'ap': r['flag'] == 'AP'})
    if name == 'Ac-227' and ac227_mode == 'norm':
        exact = sum(b['i'] for b in out if not b['ap'])
        approx = sum(b['i'] for b in out if b['ap'])
        k = (AC227_BETA_MODE_PERCENT - exact) / approx
        for b in out:
            if b['ap']:
                b['i'] = b['i'] * k     # (`crit274n` Д1) только число; текст — как у LNHB, признак `AP`
    return out


def match(rows, lines, key_e, tol):
    u"""Пары «строка поставки — линия LNHB»: ближайшая по энергии в допуске, один к одному (жадно по |ΔE|)."""
    cand = []
    for ri, r in enumerate(rows):
        for li, l in enumerate(lines):
            d = abs(key_e(r) - l['_key'])
            if d <= tol(l['_key']):
                cand.append((d, ri, li))
    cand.sort()
    used_r, used_l, pairs = set(), set(), {}
    for d, ri, li in cand:
        if ri in used_r or li in used_l:
            continue
        used_r.add(ri)
        used_l.add(li)
        pairs[ri] = li
    return pairs


COLS = ledger.COLUMNS   # parent_l_seqno, parent_nucid, dec_type, type_a, type_c, intensity, intensity_unc,
                        # intensity_num, energy_num, energy, energy_unc, endpoint, endpoint_unc


def alpha_values(nucid, l, lvl):
    return (lvl, nucid, ALPHA_DEC, 'A', '', l['i_txt'], l['i_unc'], l['i'], l['e'], l['e_txt'], l['e_unc'], None, None)


def beta_values(nucid, b, lvl):
    return (lvl, nucid, BETA_DEC, 'B-', '', b['i_txt'], b['i_unc'], b['i'], b['avg'], b['avg_txt'], b['avg_unc'],
            b['max_txt'], b['max_unc'])


def select_rows(con, nucid, type_a):
    return con.execute('select %s, dr_pk from decay_radiations where parent_nucid = $n and type_a = $t'
                       % ', '.join(COLS) + LEVEL_CLAUSE + ' order by dr_pk', {LEVEL_PARAM: nucid, 't': type_a}).fetchall()


def update_row(con, pk, vals):
    con.execute('update decay_radiations set %s where dr_pk = ?' % ', '.join('%s = ?' % c for c in COLS),
                tuple(vals) + (pk,))


def insert_row(con, pk, vals):
    con.execute('insert into decay_radiations (%s, dr_pk) values (%s)' % (', '.join(COLS), ', '.join('?' * (len(COLS) + 1))),
                tuple(vals) + (pk,))


class Run(object):
    def __init__(self, retired):
        self.retired = retired
        self.retire_rows = []    # (строка, источник)
        self.superseded = []     # (dr_pk заменённой, ключ замены в плане, примечание)
        self.unpaired = 0
        self.table = []          # (нуклид, тип, dr_pk, E было, I было, E стало, I стало, погр., метка LNHB, что)
        self.notes = []


def correct_parent(con, run, nucid, type_a, lines, key_line, key_row, tol_pair, tol_place, values, src):
    u"""Одна группа строк родителя (α или β⁻): пара в `tol_pair`; сдвиг ключа в `tol_place` — правка на месте, больше —
    новая строка, а прежняя остаётся и пишется заменённой (`decay_radiations_superseded`; у α `tol_pair` = `tol_place`)."""
    lvl = level_of(con, nucid)
    rows = select_rows(con, nucid, type_a)
    supply = [r for r in rows if r[-1] <= SUPPLY_LAST_PK]
    own = [r for r in rows if FIRST_PK <= r[-1] <= LAST_PK]
    other = [r for r in rows if r not in supply and r not in own]
    if other:
        run.notes.append(u'%s %s: строк вне поставки и своего диапазона %d — не трогаются' % (nucid, type_a, len(other)))
    for l in lines:
        l['_key'] = key_line(l)
    pairs = match(supply, lines, key_row, tol_pair)
    moved = set()
    for ri, r in enumerate(supply):
        if ri not in pairs:
            run.unpaired += 1
            run.notes.append(u'⚠ %s %s: строка поставки %d (%s кэВ, %s %%) без пары в %s — не тронута (расхождение)'
                             % (nucid, type_a, r[-1], r[9] if type_a == 'A' else r[11], r[5], src))
            continue
        l = lines[pairs[ri]]
        if abs(key_row(r) - l['_key']) > tol_place(l['_key']):
            # (`AMBER281`) сдвиг ключа сверх допуска номера — та же ветвь НОВОЙ строкой; прежняя остаётся как была
            # и записывается заменённой (решение Amber 14.11.2026 «Строка остаётся, отсев»)
            moved.add(pairs[ri])
            key = (nucid, type_a, round(key_line(l), 4))
            run.superseded.append((r[-1], key, u'граница по LNHB %s(%s) кэВ, в поставке %s(%s); %s' % (
                l['max_txt'], l['max_unc'], r[11], r[12], l['label'])))
            run.table.append((nucid, type_a, r[-1], r[9] if type_a == 'A' else r[11], r[5], u'', u'', u'', l['label'],
                              u'заменена (остаётся, отсев)'))
            continue
        vals = values(nucid, l, lvl)
        if tuple(r[:-1]) != tuple(vals):
            update_row(con, r[-1], vals)
        run.table.append((nucid, type_a, r[-1], r[9] if type_a == 'A' else r[11], r[5],
                          vals[9] if type_a == 'A' else vals[11], vals[5], vals[6], l['label'], u'на месте'))
    paired_lines = set(pairs.values()) - moved
    planned = []
    for li, l in enumerate(lines):
        if li in paired_lines:
            continue
        vals = values(nucid, l, lvl)
        planned.append(((nucid, type_a, round(key_line(l), 4)), vals, l))
    keyfn = lambda r: (r[1], r[3], round(key_row(r), 4))  # noqa: E731
    keep, update, insert, delete = ledger.sync(con, own, [(k, v) for k, v, _ in planned], keyfn, FIRST_PK, LAST_PK,
                                               run.retired)
    by_vals = dict((tuple(v), l) for _, v, l in planned)
    for pk, vals in update:
        update_row(con, pk, vals)
    for pk, vals in insert:
        insert_row(con, pk, vals)
    for pk, vals in keep + update + insert:
        l = by_vals.get(tuple(vals))
        run.table.append((nucid, type_a, pk, u'', u'', vals[9] if type_a == 'A' else vals[11], vals[5], vals[6],
                          l['label'] if l else u'', u'добавлена'))
    for r in delete:
        con.execute('delete from decay_radiations where dr_pk = ?', (r[-1],))
        run.retire_rows.append((r, u'correct_lnhb_alpha.py: своя строка вне плана'))
    pk_of_key = {}
    for pk, vals in keep + update + insert:
        pk_of_key[(vals[1], vals[3], round(float(vals[8] if type_a == 'A' else fnum(vals[11])), 4))] = pk
    for old_pk, key, note in [x for x in run.superseded if x[1][0] == nucid and x[1][1] == type_a]:
        write_superseded(con, old_pk, pk_of_key[key], note)


def write_superseded(con, old_pk, new_pk, note):
    u"""Строка таблицы замен; таблицы нет — завести. Та же запись — не трогать (повторный прогон — та же база)."""
    if con.execute("select 1 from sqlite_master where type = 'table' and name = ?", (SUPERSEDED,)).fetchone() is None:
        con.execute(SUPERSEDED_SCHEMA)
    want = (old_pk, new_pk, SUPERSEDED_SOURCE, note)
    have = con.execute('select dr_pk, by_dr_pk, source, note from %s where dr_pk = ?' % SUPERSEDED, (old_pk,)).fetchone()
    if have is not None and tuple(have) == want:
        return
    con.execute('insert or replace into %s (dr_pk, by_dr_pk, source, note) values (?, ?, ?, ?)' % SUPERSEDED, want)


def not_superseded(con):
    u"""Довесок читателя (как `DecayParentRule.NotSupersededClause`); таблицы нет — пусто."""
    have = con.execute("select 1 from sqlite_master where type = 'table' and name = ?", (SUPERSEDED,)).fetchone()
    return ' and dr_pk not in (select dr_pk from %s)' % SUPERSEDED if have else ''


def correct(con, run, ac227_beta='norm'):
    for name, nucid in ALPHA_TARGETS:
        correct_parent(con, run, nucid, 'A', lnhb_alphas(name), lambda l: l['e'], lambda r: r[8] or 0.0,
                       tol_alpha, tol_alpha, alpha_values, u'LNHB')
    for name, nucid in BETA_TARGETS:
        correct_parent(con, run, nucid, 'B-', lnhb_betas(name, ac227_beta, run.notes), lambda b: b['max'],
                       lambda r: fnum(r[11]) or 0.0, tol_beta_pair, tol_beta, beta_values, u'LNHB')


def sums(con):
    out = {}
    for n in PARENTS:
        # глазами читателя: заменённые строки отсеяны
        a = con.execute("select coalesce(sum(intensity_num), 0), count(*) from decay_radiations where parent_nucid = $n"
                        " and type_a = 'A'" + LEVEL_CLAUSE + not_superseded(con), {LEVEL_PARAM: n}).fetchone()
        b = con.execute("select coalesce(sum(intensity_num), 0), count(*) from decay_radiations where parent_nucid = $n"
                        " and type_a = 'B-'" + LEVEL_CLAUSE + not_superseded(con), {LEVEL_PARAM: n}).fetchone()
        out[n] = (a[0], a[1], b[0], b[1])
    return out


def parent_rows(con):
    out = {}
    for n in PARENTS:
        out[n] = set(con.execute('select %s, dr_pk from decay_radiations where parent_nucid = ?' % ', '.join(COLS),
                                 (n,)).fetchall())
    if con.execute("select 1 from sqlite_master where type = 'table' and name = ?", (SUPERSEDED,)).fetchone():
        out[SUPERSEDED] = set(con.execute('select dr_pk, by_dr_pk, source, note from %s' % SUPERSEDED).fetchall())
    else:
        out[SUPERSEDED] = set()
    return out


def check(db, ac227_beta):
    fd, work = tempfile.mkstemp(suffix='.sqlite')
    os.close(fd)
    shutil.copyfile(db, work)
    con = sqlite3.connect(work)
    try:
        before = parent_rows(con)
        con.execute('begin')
        run = Run(ledger.read_retired(ledger.retired_path_for(db)))
        correct(con, run, ac227_beta)
        after = parent_rows(con)
        con.execute('rollback')
    finally:
        con.close()
        os.remove(work)
    total = 0
    print(u'сверка %s с LNHB (строк родителя: было / стало / расходится):' % db)
    for n in PARENTS + [SUPERSEDED]:
        d = len(before[n] ^ after[n])
        total += d
        print(u'  %-6s %4d / %4d  расходится %d' % (n, len(before[n]), len(after[n]), d))
    for n in run.notes:
        print(u'  ' + n)
    total += run.unpaired
    print(u'СВЕРКА: %s' % (u'0 расхождений' if total == 0 else u'РАСХОЖДЕНИЙ %d (строк поставки без пары %d)' % (total, run.unpaired)))
    return 0 if total == 0 else 1


def main():
    ap = argparse.ArgumentParser(description=u'Альфы и β⁻ ряда Ac-227 в nucdb по LNHB (AMBER274 Э3)')
    ap.add_argument('--db', default=DEFAULT_DB)
    ap.add_argument('--apply', action='store_true', help=u'записать в базу (иначе — копия во временном файле)')
    ap.add_argument('--check', action='store_true', help=u'сверка: код 1, если база расходится с LNHB')
    ap.add_argument('--table', default=None, help=u'TSV «было — стало» по тронутым строкам')
    ap.add_argument('--ac227-beta', default='norm', choices=('norm', 'printed'))
    ap.add_argument('--retired', default=None)
    a = ap.parse_args()
    if not os.path.exists(a.db):
        print(u'⛔ нет базы: %s' % a.db)
        return 2
    if a.check:
        if a.apply:
            print(u'⛔ --check и --apply вместе не зовутся')
            return 2
        return check(a.db, a.ac227_beta)
    retired_path = ledger.retired_path_for(a.db, a.retired)
    if a.apply:
        work = a.db
    else:
        fd, work = tempfile.mkstemp(suffix='.sqlite')
        os.close(fd)
        shutil.copyfile(a.db, work)
    con = sqlite3.connect(work)
    try:
        before = table_hashes(con)
        s_before = sums(con)
        con.execute('begin')
        run = Run(ledger.read_retired(retired_path))
        correct(con, run, a.ac227_beta)
        after_tx = table_hashes(con)
        changed = sorted(t for t in set(before) | set(after_tx) if before.get(t) != after_tx.get(t))
        if not set(changed) <= {'decay_radiations', SUPERSEDED}:
            con.execute('rollback')
            print(u'⛔ изменились не те таблицы: %r — откат' % changed)
            return 1
        con.execute('commit')
        s_after = sums(con)
        if not changed:
            print(u'база уже исправлена: содержимое после прогона то же до бита (повторный прогон)')
        for n in run.notes:
            print(u'  ' + n)
        print(u'')
        print(u'Σ по родителям (уровень родителя): α % (строк) | β⁻ % (строк), было → стало')
        for n in PARENTS:
            b, f = s_before[n], s_after[n]
            print(u'  %-6s α %9.5f (%2d) → %9.5f (%2d) | β⁻ %9.5f (%d) → %9.5f (%d)' % (n, b[0], b[1], f[0], f[1], b[2], b[3], f[2], f[3]))
        if a.table:
            with io.open(a.table, 'w', encoding='utf-8', newline='\n') as f:
                f.write(u'нуклид\tтип\tdr_pk\tE (граница у β) было\tI было\tE стало\tI стало\tпогр.\tлиния LNHB\tчто\n')
                for r in run.table:
                    f.write(u'\t'.join(u'%s' % (v if v is not None else u'') for v in r) + u'\n')
            print(u'таблица «было — стало»: %s (%d строк)' % (a.table, len(run.table)))
        print(u'')
        print(u'sha256 таблиц (до → после; строк):')
        after = table_hashes(con)
        for t in sorted(set(before) | set(after)):
            b0, a0 = before.get(t, (u'—' * 16, 0)), after.get(t, (u'—' * 16, 0))
            mark = u'изменена' if b0 != a0 else u'та же'
            print(u'  %-28s %s → %s  %d → %d  %s' % (t, b0[0][:16], a0[0][:16], b0[1], a0[1], mark))
        if a.apply and run.retire_rows:
            n_ret = 0
            for row, source in run.retire_rows:
                n_ret += ledger.append_retired(retired_path, [row], source)
            print(u'в реестр снятых (%s) записано: %d' % (retired_path, n_ret))
        print((u'ЗАПИСАНО в %s' % a.db) if a.apply else u'ПОКАЗАНО (база не тронута; запись — ключ --apply)')
        return 0
    finally:
        con.close()
        if not a.apply:
            os.remove(work)


if __name__ == '__main__':
    sys.exit(main())
