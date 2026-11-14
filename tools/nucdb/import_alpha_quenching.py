# -*- coding: utf-8 -*-
u"""Таблица гашения альф `matdb.scint_alpha_quenching` (QF альф по веществу сцинтиллятора) — `AMBER274` Э3,
полоса `a274n`, 14.11.2026.

ЗАЧЕМ. Слой собственной активности кристалла LaBr3 с альфами ряда Ac-227 (`CrystalOwnActivityBuilder`, рычаг
`CrystalOwnParts.AlphaQuenching`, полоса `a274a`) берёт свет альфы E_α·QF(E_α). QF измерен полосой `a274a` по
вершинам групп альф в сумме семи защищённых фонов второго прибора LaBr3(Ce) ⌀25×25 (журнал
`handover/handover-2026-11-14-a274a.md` §3; критик `crit274a` подтвердил на трёх фонах своим кодом):
QF(E) = 0.3785 + 0.019·(E − 6 МэВ)/МэВ. Решение Amber 14.11.2026 вопросником, дословно: **«В matdb по замеру
(Рекомендую)»** — её слово на ЗАПИСЬ таблицы.

ФОРМА — строка закона, а не точки: (вещество, источник) → QF на опорной энергии, наклон на МэВ, опорная энергия,
ОХВАТ замера (энергии, на которых закон держится данными). Так лежат и параметры Пейна (`scint_npsm_params`:
вещество, источник, `is_default`). Читатель — `BecquerelMonitor/FullSpectrumAnalysis/ScintAlphaQuenching.cs`.

⛔ ТАБЛИЦА ВНЕ ОТПЕЧАТКА СКЛАДА. Перенос её не читает; в `MaterialDatabase.SimulatorTables` (отпечаток `mdb=`
клейма матриц и кривых `<Efficiency>`) она НЕ входит — как фотонные кривые света FSA (`AMBER272`). Читатель лежит
вне `EfficiencyMaker/` нарочно: сторож `tools/check_matdb_fingerprint.py` требует внести в список всякую таблицу,
имя которой стоит в коде `EfficiencyMaker/*.cs`, — а внесение объявило бы негодным весь склад и 92 кривые без
единого изменения физики. QF входит в клеймо РАЗДЕЛА слоя значением (`RecipeStamp`: `:aq…:as…`).

⚠ `is_default = 0`: строка измерена на ОДНОМ кристалле (второй прибор); выбрать её «по веществу» для всякого
LaBr3 — решение Amber (у `LABR_BRIL` — «не включать», 13.11.2026). Читатель без названного источника берёт только
строку `is_default = 1`, а такой нет.

ПРИЁМКА ЧИСЛОМ: до и после — sha256 КАЖДОЙ таблицы; меняться вправе только `scint_alpha_quenching`. Повторный
прогон — та же база.

    python tools/nucdb/import_alpha_quenching.py               # показать (база не тронута)
    python tools/nucdb/import_alpha_quenching.py --apply       # записать в BecquerelMonitor/matdb.sqlite
    python tools/nucdb/import_alpha_quenching.py --db=<путь> [--apply]

Коды: 0 — сделано или показано; 1 — останов (изменилась не та таблица); 2 — неверный вызов.
"""
from __future__ import print_function
import argparse
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
DEFAULT_DB = os.path.join(ROOT, 'BecquerelMonitor', 'matdb.sqlite')
sys.path.insert(0, HERE)
from correct_lnhb import table_hashes  # noqa: E402

TABLE = 'scint_alpha_quenching'
SCHEMA = u"""CREATE TABLE scint_alpha_quenching (
        material        text not null,
        source          text not null,
        is_default      integer not null,
        ref_kev         real not null,
        qf_ref          real not null,
        slope_per_mev   real not null,
        measured_min_kev real not null,
        measured_max_kev real not null,
        origin          text not null,
        note            text not null,
        primary key (material, source)
    ) without rowid"""

#: (вещество как в `scint_npsm_params`, источник, по умолчанию, E_ref, QF(E_ref), наклон на МэВ, охват замера, кэВ,
#: происхождение, примечание)
ROWS = [
    ('LaBr3:Ce', 'a274a_bg_vertices', 0, 6000.0, 0.3785, 0.019, 5700.0, 7400.0,
     u'замер AMBER274 a274a, 14.11.2026: вершины групп альф ряда Ac-227 (Th-227+Ra-223, Rn-219+Bi-211, Po-215) в '
     u'сумме 7 защищённых фонов второго прибора LaBr3(Ce) 25x25, 4.56e6 с; критик crit274a — на трёх фонах',
     u'QF(E) = 0.3785 + 0.019*(E - 6 МэВ)/МэВ; вершины данных 2285.3 / 2594.8 / 2988.0 кэВ-экв, невязка закона '
     u'+6 / -10 / +2 кэВ (по энергии QF не линеен: у Bi-211 6623 и Rn-219 6819 кэВ данные дают 0.392, у Po-215 '
     u'7386 кэВ - 0.405); шкала выше пика суммы известна до +-10 кэВ (+-0.003 в QF); ниже 5.7 МэВ (Ac-227 4.95 МэВ: '
     u'0.359) - продолжение закона, не замер; решение Amber 14.11.2026 «В matdb по замеру (Рекомендую)»; '
     u'is_default 0 - выбор строки по веществу для всякого LaBr3 - решение Amber (у LABR_BRIL «не включать»)'),
]


def write(con):
    have = con.execute("select sql from sqlite_master where type = 'table' and name = ?", (TABLE,)).fetchone()
    if have is None:
        con.execute(SCHEMA)
    elif ' '.join(have[0].split()) != ' '.join(SCHEMA.split()):
        raise SystemExit(u'⛔ таблица %s есть, но схема другая — останов' % TABLE)
    con.execute('delete from %s' % TABLE)
    con.executemany('insert into %s values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)' % TABLE, ROWS)


def main():
    ap = argparse.ArgumentParser(description=u'QF альф в matdb (AMBER274 Э3)')
    ap.add_argument('--db', default=DEFAULT_DB)
    ap.add_argument('--apply', action='store_true')
    a = ap.parse_args()
    if not os.path.isfile(a.db):
        print(u'⛔ нет базы: %s' % a.db)
        return 2
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
        write(con)
        after = table_hashes(con)
        changed = sorted(t for t in set(before) | set(after) if before.get(t) != after.get(t))
        if changed not in ([], [TABLE]):
            con.execute('rollback')
            print(u'⛔ изменились не те таблицы: %r — откат' % changed)
            return 1
        if changed:
            con.execute('commit')
        else:
            con.execute('rollback')      # повторный прогон не трогает и байты файла
            print(u'база уже содержит эту таблицу до бита (повторный прогон)')
        for t in sorted(set(before) | set(after)):
            b, f = before.get(t, ('—' * 16, 0)), after.get(t, ('—' * 16, 0))
            if b != f:
                print(u'  %-28s %s → %s  %d → %d  изменена' % (t, b[0][:16], f[0][:16], b[1], f[1]))
        print(u'таблиц не тронуто: %d' % sum(1 for t in before if before[t] == after.get(t)))
        for r in con.execute('select material, source, is_default, ref_kev, qf_ref, slope_per_mev, measured_min_kev,'
                             ' measured_max_kev from %s' % TABLE):
            print(u'  %s' % (r,))
        print((u'ЗАПИСАНО в %s' % a.db) if a.apply else u'ПОКАЗАНО (база не тронута; запись — ключ --apply)')
        return 0
    finally:
        con.close()
        if not a.apply:
            os.remove(work)


if __name__ == '__main__':
    sys.exit(main())
