# -*- coding: utf-8 -*-
u"""Фотонные кривые света FSA в matdb.sqlite (`AMBER272`, 29.10.2026).

Фотонная кривая r(E) — средний свет пика полного поглощения на кэВ линии — ВЫХОД
переноса, снятый пробой `tools/effmaker/probes/LightScaleProbe.cs`. Читает её FSA
(`FsaLightScale` в `BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs`): привязка
шкалы по свету, каскадное суммирование, образ наложений, аудит линий. До 29.10.2026
ряды NaI:Tl и CsI:Tl были вписаны в код массивами; решение Amber 29.10.2026
вопросником, её словами: «Все таблицы должны жить в базе matdb»; что писать —
дословно «Перенос + LaBr3/CeBr3 (Рекомендую)».

Две таблицы:

    scint_photon_light_curves  — строка на кривую: имя (`material`, как его даёт
        `EfficiencySimulator.ScintillatorNameOf`), `match_prefix` (вещество кристалла,
        начинающееся с этого, получает кривую, если своей нет: «CsI» → CsI:Tl и для
        CsI:Na; NULL — только точное имя), происхождение ряда и примечание;
    scint_photon_light_yield   — узлы (material, energy_kev, yield_rel); интерполяция
        в разборе линейна по ln E, вне таблицы — крайние значения.

⛔ В ОТПЕЧАТОК ПЕРЕНОСА (`MaterialDatabase.SimulatorTables`, `mdb=` клейма матриц и
кривых) обе НЕ ВХОДЯТ: перенос их не читает, тело матрицы от них не зависит. Сторож
`tools/check_matdb_fingerprint.py` держит это правило с другой стороны — таблица,
которую начнёт читать перенос, обязана встать в список.

Ряды лежат в `data/photon_light/*.tsv` (шапка `#` — происхождение, затем
`energy_kev<TAB>yield_rel`); число берётся ТЕКСТОМ, как записано: перенос из кода
побитовый (`float("1.1740")` — тот же double, что литерал C# `1.1740`).

    python import_photon_light_yield.py <matdb.sqlite>            # показать, что будет
    python import_photon_light_yield.py <matdb.sqlite> --apply    # записать

⛔ Запись в базу — только словом Amber (`database-writes-through-amber`); без
`--apply` скрипт пишет в копию базы в памяти и печатает итог.
"""
from __future__ import print_function
import io
import math
import os
import sqlite3
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, 'data', 'photon_light')

# material, match_prefix, файл ряда, происхождение (коротко), примечание
CURVES = [
    ('NaI:Tl', 'NaI', 'NaI_Tl.tsv',
     u'LightScaleProbe G1S_point5.in (NaI 63x63, точка 5 см), физика 26, kdip=1 ecomp=1, '
     u'кривая электронов p220_t7_khodyuk2010_sums; 1e6 историй до 60 кэВ, 4e6 от 81 кэВ',
     u'П220 02.10.2026 (AMBER155); перенесена из FsaLightScale.NaITable побитово, AMBER272 29.10.2026'),
    ('CsI:Tl', 'CsI', 'CsI_Tl.tsv',
     u'LightScaleProbe ASN16_csi_point1.in (брусок ASN16, точка 1 см), физика 26, kdip=1 ecomp=1, '
     u'кривая электронов p220_khodyuk2012_free; 4e6 историй на узел',
     u'П220 02.10.2026 (AMBER155); перенесена из FsaLightScale.CsITable побитово, AMBER272 29.10.2026; '
     u'CsI:Na своей кривой нет — берёт эту по префиксу'),
    ('LaBr3:Ce', 'LaBr3', 'LaBr3_Ce.tsv',
     u'LightScaleProbe LaBr3_38x38_point5.in (LaBr3 38x38, точка 5 см), физика 27, kdip=1 ecomp=1, '
     u'кривая электронов payne2014_tableII_20C (eta 0.18, Trap 0, Birks 465); 4e6 историй на узел',
     u'AMBER272 29.10.2026: параметры Пейна как в базе, НЕ калиброваны по фотонной нПР'),
    ('CeBr3', 'CeBr3', 'CeBr3.tsv',
     u'LightScaleProbe CeBr3_38x38_point5.in (CeBr3 38x38, точка 5 см), физика 27, kdip=1 ecomp=1, '
     u'кривая электронов payne2014_tableII_20C (eta 0.345, Trap 20, Birks 132.5); 4e6 историй на узел',
     u'AMBER272 29.10.2026: параметры Пейна как в базе, НЕ калиброваны по фотонной нПР'),
]

SCHEMA = u"""
    drop table if exists scint_photon_light_curves;
    create table scint_photon_light_curves (
        material      text not null primary key,
        match_prefix  text unique,
        origin        text not null,
        note          text not null
    ) without rowid;

    drop table if exists scint_photon_light_yield;
    create table scint_photon_light_yield (
        material    text not null,
        energy_kev  real not null,
        yield_rel   real not null,
        primary key (material, energy_kev)
    ) without rowid;
"""


def read_series(path):
    u"""Узлы ряда: [(текст E, текст r)], проверены — числа конечны, > 0, E возрастает."""
    rows = []
    header_seen = False
    with io.open(path, encoding='utf-8') as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            if not header_seen:
                if line.split('\t') != ['energy_kev', 'yield_rel']:
                    raise SystemExit(u'%s: шапка не «energy_kev<TAB>yield_rel»: %r' % (path, line))
                header_seen = True
                continue
            parts = line.split('\t')
            if len(parts) != 2:
                raise SystemExit(u'%s: строка не из двух чисел: %r' % (path, line))
            e, r = float(parts[0]), float(parts[1])
            if not (e > 0 and r > 0 and math.isfinite(e) and math.isfinite(r)):
                raise SystemExit(u'%s: негодный узел %r' % (path, line))
            if rows and not e > float(rows[-1][0]):
                raise SystemExit(u'%s: энергии не возрастают у %r' % (path, line))
            rows.append((parts[0], parts[1]))
    if len(rows) < 2:
        raise SystemExit(u'%s: меньше двух узлов' % path)
    return rows


def fill(db):
    cur = db.cursor()
    cur.executescript(SCHEMA)
    expected = {}
    for material, prefix, fname, origin, note in CURVES:
        rows = read_series(os.path.join(DATA, fname))
        cur.execute('insert into scint_photon_light_curves values (?,?,?,?)',
                    (material, prefix, origin, note))
        cur.executemany('insert into scint_photon_light_yield values (?,?,?)',
                        [(material, float(e), float(r)) for e, r in rows])
        expected[material] = [(float(e), float(r)) for e, r in rows]
    db.commit()
    # Сверка записанного с рядом — побитово (double SQLite REAL = double Python).
    for material, want in sorted(expected.items()):
        got = cur.execute('select energy_kev, yield_rel from scint_photon_light_yield '
                          'where material = ? order by energy_kev', (material,)).fetchall()
        if [(a, b) for a, b in got] != want:
            raise SystemExit(u'%s: записанное разошлось с рядом' % material)
        print(u'%-9s %2d узлов, %g…%g кэВ, r(10) %.4f, r(661.657) %.4f, r(2614) %.4f'
              % (material, len(got), got[0][0], got[-1][0], interp(got, 10.0),
                 interp(got, 661.657), interp(got, 2614.0)))


def interp(rows, e):
    u"""Та же интерполяция, что у FsaLightScale.Interp: линейно по ln E, вне — крайние."""
    if e <= rows[0][0]:
        return rows[0][1]
    if e >= rows[-1][0]:
        return rows[-1][1]
    for (e0, r0), (e1, r1) in zip(rows, rows[1:]):
        if e <= e1:
            t = (math.log(e) - math.log(e0)) / (math.log(e1) - math.log(e0))
            return r0 + (r1 - r0) * t
    return rows[-1][1]


def main(argv):
    args = [a for a in argv if not a.startswith('--')]
    apply = '--apply' in argv
    if len(args) != 1 or any(a not in ('--apply',) for a in argv if a.startswith('--')):
        raise SystemExit(__doc__)
    path = args[0]
    if not os.path.isfile(path):
        raise SystemExit(u'нет файла базы: %s' % path)
    if apply:
        db = sqlite3.connect(path)
    else:
        src = sqlite3.connect('file:' + path + '?mode=ro', uri=True)
        db = sqlite3.connect(':memory:')
        src.backup(db)
        src.close()
        print(u'(без --apply: пишется копия базы в памяти, файл не тронут)')
    try:
        fill(db)
    finally:
        db.close()
    print(u'записано в %s' % path if apply else u'файл не изменён')


if __name__ == '__main__':
    main(sys.argv[1:])
