# -*- coding: utf-8 -*-
u"""Таблица экранирования упругого рассеяния по DPWA в matdb.sqlite (`AMBER274`, физика 29, Ф1;
полоса `screen29`, 02.11.2026 — ЗАДЕЛ: схема и импорт для следующей полосы, на базе дерева НЕ запускался).

Решение Amber 02.11.2026 вопросником, дословно: «Сейчас, до склада (Рекомендую)» — параметр
экранирования, подогнанный к данным DPWA из поставки Geant4, с записью в `matdb`.

Данные — `data/dpwa_screening/dpwa_screening_{el,pos}.tsv` (строит `build_dpwa_screening.py`;
там же — вид сечения, постановка подгонки, источник и условия данных Geant4). Две таблицы:

    elastic_screening_dpwa       — узлы (particle, z, energy_kev): a_tr1, a, c0…c4, fit_rms,
                                   dpwa_tr1_cm2, dpwa_h20_cm2, dpwa_s20_cm2;
    elastic_screening_dpwa_meta  — строка на частицу: источник, условия данных, построитель,
                                   отпечаток TSV (sha256), число узлов и Z.

Перенос читает ядерную часть: dσ/dμ = C(T)·Z²·P(√μ)/(μ + a)², P(x) = Σ c_j x^j; между узлами —
линейно по ln T от ln a и c_j, вне сетки — крайний узел (`build_dpwa_screening.interp`).

⛔ ТАБЛИЦУ ЧИТАЕТ ПЕРЕНОС: `elastic_screening_dpwa` обязана встать в `MaterialDatabase.SimulatorTables`
(отпечаток `mdb=` клейма матриц и кривых), иначе сторож `tools/check_matdb_fingerprint.py` (п. 1)
краснеет, а правка таблицы молча оставит склад «годным». `_meta` перенос не читает — в список
не входит (кроме случая, когда код станет её читать).

Сверка при записи (обе — и без `--apply`):
  * узлы полные: Z 1…92, у каждого Z одна и та же сетка энергий, строго возрастающая;
  * числа конечны, a > 0, P(x) ≥ 0 на [0, √2] (57 точек);
  * модель из записанных a, c даёт σ_tr1 DPWA узла (те же замкнутые формы, что `MottMoments`) —
    отн. 1e-6; это ловит перепутанные столбцы и испорченный разбор;
  * записанное читается обратно побитово (double SQLite REAL = double Python).

    python -I import_dpwa_screening.py <matdb.sqlite>            # показать, что будет (копия в памяти)
    python -I import_dpwa_screening.py <matdb.sqlite> --apply    # записать

⛔ Запись в базу — только словом Amber (`database-writes-through-amber`). Данные — G4EMLOW 8.8 `dpwa`
(README G4EMLOW: «DATA ARE *NOT* FOR COMMERCIAL USE AND MUST BE USED WITHIN GEANT4»); решение Amber
02.11.2026, дословно: «BecqMoni - не коммерческий продукт. Просто возьми готовые данные.»
"""
from __future__ import print_function
import hashlib
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
DATA = os.path.join(HERE, 'data', 'dpwa_screening')
FILES = (('e-', 'dpwa_screening_el.tsv'), ('e+', 'dpwa_screening_pos.tsv'))
COLUMNS = ['z', 't_kev', 'a_tr1', 'a', 'c0', 'c1', 'c2', 'c3', 'c4', 'fit_rms',
           'dpwa_tr1_cm2', 'dpwa_h20_cm2', 'dpwa_s20_cm2']
ZMAX = 92
ME = 510.99895
RE = 2.8179403262e-13

SOURCE = (u'G4EMLOW8.8/dpwa/dcss (Geant4 11.4; DPWA ELSEPA — Salvat, Jablonski, Powell, CPC 165 (2005) 157; '
          u'таблицы M. Novak, CERN 2020); подгонка tools/nucdb/build_dpwa_screening.py, полоса screen29 02.11.2026')
TERMS = (u'README G4EMLOW: «DATA ARE *NOT* FOR COMMERCIAL USE AND MUST BE USED WITHIN GEANT4»; Geant4 Software '
         u'License: уведомление и ссылка «This product includes software developed by Members of the Geant4 '
         u'Collaboration». Таблица — производная (подгонка). Решение Amber 02.11.2026, дословно: «BecqMoni - не '
         u'коммерческий продукт. Просто возьми готовые данные.»')

SCHEMA = u"""
    drop table if exists elastic_screening_dpwa;
    create table elastic_screening_dpwa (
        particle      text    not null check (particle in ('e-', 'e+')),
        z             integer not null check (z between 1 and 92),
        energy_kev    real    not null check (energy_kev > 0),
        a_tr1         real,                 -- e⁻: экранирование постановки GS при нынешнем P; e⁺ — NULL
        a             real    not null check (a > 0),
        c0            real    not null,
        c1            real    not null,
        c2            real    not null,
        c3            real    not null,
        c4            real    not null,
        fit_rms       real    not null,     -- ско отн. ошибки dσ/dΩ подгонки на 10…180°
        dpwa_tr1_cm2  real    not null,     -- σ_tr1 DPWA, см² на атом
        dpwa_h20_cm2  real    not null,     -- σ(θ > 20°) DPWA, см² на атом
        dpwa_s20_cm2  real    not null,     -- ∫_0^{20°} μ dσ DPWA, см² на атом
        primary key (particle, z, energy_kev)
    ) without rowid;

    drop table if exists elastic_screening_dpwa_meta;
    create table elastic_screening_dpwa_meta (
        particle      text    not null primary key check (particle in ('e-', 'e+')),
        source        text    not null,
        terms         text    not null,
        tsv_sha256    text    not null,
        nodes         integer not null,
        z_count       integer not null
    ) without rowid;
"""

ROOT2 = 1.4142135623730951


def moments_soft_total(a):
    u"""s_j(a, μ_c = 2) — первое транспортное по базису x^j (формы `ElectronTransport.MottMoments`)."""
    muc = 2.0
    xc = math.sqrt(muc)
    s = math.sqrt(a)
    in_xa = 1.0 / (muc + a)
    ls = math.log((muc + a) / a)
    e = math.atan(xc / s)
    return [ls + a * in_xa - 1.0,
            2.0 * xc - 3.0 * s * e + a * xc * in_xa,
            muc - 2.0 * a * ls + a * muc * in_xa,
            (2.0 / 3.0) * xc * muc - 4.0 * a * xc + 5.0 * a * s * e - a * a * xc * in_xa,
            0.5 * muc * muc - 2.0 * a * muc + 3.0 * a * a * ls - a * a * muc * in_xa]


def common(t):
    gamma = 1.0 + t / ME
    beta2 = 1.0 - 1.0 / (gamma * gamma)
    return 2.0 * math.pi * RE * RE * ME * ME / (beta2 * t * (t + 2.0 * ME))


def read_tsv(path, particle):
    u"""Строки (particle, z, E, a_tr1|None, a, c0…c4, rms, tr1, h20, s20) — проверенные."""
    rows = []
    header = None
    with io.open(path, encoding='utf-8') as fh:
        for line in fh:
            line = line.rstrip('\n')
            if not line or line.startswith('#'):
                continue
            parts = line.split('\t')
            if header is None:
                if parts != COLUMNS:
                    raise SystemExit(u'%s: шапка не та: %r' % (path, parts))
                header = parts
                continue
            if len(parts) != len(COLUMNS):
                raise SystemExit(u'%s: строка не из %d полей: %r' % (path, len(COLUMNS), line))
            rec = dict(zip(COLUMNS, parts))
            z = int(rec['z'])
            vals = [float(rec[k]) for k in COLUMNS[3:]]
            a_tr1 = float(rec['a_tr1']) if rec['a_tr1'] else None
            t = float(rec['t_kev'])
            if not all(math.isfinite(v) for v in vals + [t]) or t <= 0 or vals[0] <= 0:
                raise SystemExit(u'%s: негодный узел %r' % (path, line))
            if particle == 'e-' and not (a_tr1 and a_tr1 > 0 and math.isfinite(a_tr1)):
                raise SystemExit(u'%s: у e⁻ нет a_tr1: %r' % (path, line))
            if particle == 'e+' and a_tr1 is not None:
                raise SystemExit(u'%s: у e⁺ a_tr1 не пустой: %r' % (path, line))
            rows.append((particle, z, t, a_tr1) + tuple(vals))
    return rows


def verify(rows, particle):
    by_z = {}
    for r in rows:
        by_z.setdefault(r[1], []).append(r)
    if sorted(by_z) != list(range(1, ZMAX + 1)):
        raise SystemExit(u'%s: Z не 1…%d' % (particle, ZMAX))
    grid = [r[2] for r in by_z[1]]
    if any(b <= a for a, b in zip(grid, grid[1:])):
        raise SystemExit(u'%s: энергии не возрастают' % particle)
    worst = 0.0
    for z, rz in by_z.items():
        if [r[2] for r in rz] != grid:
            raise SystemExit(u'%s Z=%d: сетка энергий не та, что у Z=1' % (particle, z))
        for r in rz:
            t, a, c = r[2], r[4], r[5:10]
            for i in range(57):
                x = ROOT2 * i / 56.0
                if c[0] + x * (c[1] + x * (c[2] + x * (c[3] + x * c[4]))) < 0.0:
                    raise SystemExit(u'%s Z=%d T=%g: P < 0 при x=%.3f' % (particle, z, t, x))
            st = moments_soft_total(a)
            tr1 = common(t) * z * z * sum(cj * sj for cj, sj in zip(c, st))
            dev = abs(tr1 / r[11] - 1.0)
            worst = max(worst, dev)
            if dev > 1e-6:
                raise SystemExit(u'%s Z=%d T=%g: σ_tr1 модели %.6e против DPWA %.6e' % (particle, z, t, tr1, r[11]))
    return grid, worst


def fill(db):
    cur = db.cursor()
    cur.executescript(SCHEMA)
    for particle, name in FILES:
        path = os.path.join(DATA, name)
        with open(path, 'rb') as fh:
            # отпечаток — по содержимому с LF: git с core.autocrlf выписывает файл с CRLF
            sha = hashlib.sha256(fh.read().replace(b'\r\n', b'\n')).hexdigest()
        rows = read_tsv(path, particle)
        grid, worst = verify(rows, particle)
        cur.executemany('insert into elastic_screening_dpwa values (?,?,?,?,?,?,?,?,?,?,?,?,?,?)', rows)
        cur.execute('insert into elastic_screening_dpwa_meta values (?,?,?,?,?,?)',
                    (particle, SOURCE, TERMS, sha, len(grid), ZMAX))
        db.commit()
        got = cur.execute('select * from elastic_screening_dpwa where particle = ? order by z, energy_kev',
                          (particle,)).fetchall()
        if [tuple(g) for g in got] != sorted(rows, key=lambda r: (r[1], r[2])):
            raise SystemExit(u'%s: записанное разошлось с TSV' % particle)
        print(u'%s: %d строк (Z 1…%d × %d узлов, %g…%g кэВ); σ_tr1 модели против DPWA — худшее %.1e; sha256 TSV %s…'
              % (particle, len(got), ZMAX, len(grid), grid[0], grid[-1], worst, sha[:16]))


def main(argv):
    args = [a for a in argv if not a.startswith('--')]
    apply = '--apply' in argv
    if len(args) != 1 or any(a != '--apply' for a in argv if a.startswith('--')):
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
