# -*- coding: utf-8 -*-
u"""Формы бета-спектров по ветвям в nucdb.sqlite (`AMBER274`, этап Э1, 30.10.2026).

Зачем. Образ беты пробы в FSA (`FsaBetaLibrary`, `FsaSampleLibrary.cs`) строит форму
ветви сам: разрешённая p·W·q²·F₀(Z, W) (точечное ядро R = 1.2·A^⅓ фм, без
экранирования) и уникальные запреты n = ΔJ − 1 при ΔJ ≥ 2. Критик физики Э1 нашёл,
что НЕуникальные запреты так считаются неверно: Cs-137 ветвь на основное состояние
(7/2+ → 3/2+, второй неуникальный) — средняя формы кода 442 кэВ против 284.90 у LNHB;
Bi-210 (1− → 0+, первый неуникальный) считается разрешённым — 390 против 317.0.
Решение Amber 30.10.2026 вопросником, дословно: «Таблицы BetaShape/LNHB в nucdb
(Рекомендую)» — её слово на запись этих двух НОВЫХ таблиц; прежние таблицы базы
скрипт не трогает.

Две таблицы:

    beta_shapes        — строка на ветвь: родитель, тип, граница (как
        `decay_radiations.endpoint` этой ветви), уровень дочернего ядра, характер
        перехода, средняя по источнику, вид формы и источник;
    beta_shape_points  — узлы формы вида 'points': (shape_id, t_kev, density),
        dN/dT в нормировке источника (читатель нормирует сам).

Вид 'factor' (C(W) = a0 + a1·W + a2/W + a3·W² множителем к разрешённой форме
p·W·q²·F₀ кода) схемой предусмотрен, строк его сейчас нет: все ветви взяты
ТАБЛИЦЕЙ BetaShape — она несёт экранирование и конечный размер ядра, которых у
разрешённой формы кода нет.

Сопоставление строки с ветвью базы читатель делает по (parent_nucid, type_a,
|endpoint_kev − decay_radiations.endpoint| ≤ max(2 кэВ, 0.5 %)). Поэтому граница
берётся из БАЗЫ, а не из LNHB, и скрипт это проверяет: у каждой строки ровно одна
ветвь базы в допуске, и её граница равна `endpoint_kev`. Узлы таблицы идут от 0 до
границы ИСТОЧНИКА (LNHB), последняя плотность — 0; расхождение с границей базы
печатается.

Ряды лежат в `data/beta_shapes/*.tsv` (шапка `#` — происхождение, затем
`t_kev<TAB>density`); числа берутся ТЕКСТОМ, как в файле BetaShape.

    python tools/nucdb/import_beta_shapes.py                     # показать, что будет
    python tools/nucdb/import_beta_shapes.py --apply             # записать
    python tools/nucdb/import_beta_shapes.py --db=<nucdb.sqlite> [--apply]

Без `--apply` скрипт пишет в копию базы в памяти и печатает итог; файл не тронут.
Повторный запуск даёт то же содержимое (таблицы пересоздаются целиком, `id`
заданы здесь). Описание — `database/scheme.md`, §8а.

Коды возврата: 0 — записано (или показано); иначе — останов с причиной.
"""
from __future__ import print_function
import io
import math
import os
import re
import sqlite3
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DATA = os.path.join(HERE, 'data', 'beta_shapes')
DEFAULT_DB = os.path.join(ROOT, 'BecquerelMonitor', 'nucdb.sqlite')

LNHB = u'https://www.lnhb.fr/nuclides/'
BETASHAPE = u'BetaShape 2.2 (05/2021), X. Mougeot, Phys. Rev. C 91 (2015) 055504'

#: id, parent_nucid, type_a, endpoint_kev (= decay_radiations.endpoint), level_kev,
#: transition, mean_kev (LNHB), kind, (a0, a1, a2, a3), файл ряда, source
SHAPES = [
    (1, '137CS', 'B-', 513.97, 661.657, '1st unique', 173.67, 'points', None, 'Cs137_B-_514.tsv',
     u'LNHB/DDEP Cs-137 (S. Leblond, 2021–2023; X. Mougeot et al., Metrologia 62 (2025) 029002), '
     u'табл. «Electron Emissions» β−0,2 avg 173.67 кэВ; ' + BETASHAPE + u', '
     + LNHB + u'Cs-137.BetaShape.zip, beta-_Cs137_trans2.bs, столбец «dN/dE calc.» (расчёт, '
     u'опытного фактора формы нет)'),
    (2, '137CS', 'B-', 1175.63, 0.0, '2nd non-unique', 284.90, 'points', None, 'Cs137_B-_1176.tsv',
     u'LNHB/DDEP Cs-137 (S. Leblond, 2021–2023; X. Mougeot et al., Metrologia 62 (2025) 029002), '
     u'табл. «Electron Emissions» β−0,0 avg 284.90 кэВ; ' + BETASHAPE + u', '
     + LNHB + u'Cs-137.BetaShape.zip, beta-_Cs137_trans0.bs, столбец «dN/dE exp.» — опытный '
     u'фактор формы H. Behrens, P. Christmas, Nucl. Phys. A 399 (1983) 131 (измерен в 710–1125 кэВ), '
     u'принятый LNHB; расчёт BetaShape той же ветви (ξ-приближение) — 423.48 кэВ'),
    (3, '210BI', 'B-', 1161.5, 0.0, '1st non-unique', 317.0, 'points', None, 'Bi210_B-_1161.tsv',
     u'LNHB/DDEP Bi-210 (V. Chisté, M.M. Bé, M.A. Kellett, 1983–2014), табл. «Electron Emissions» '
     u'β−0,0 avg 317.0 кэВ; ' + BETASHAPE + u', ' + LNHB + u'Bi-210.BetaShape.zip, '
     u'beta-_Bi210_trans0.bs, столбец «dN/dE exp.» — опытный фактор формы C(W) = 1 − 0.47·W + '
     u'0.065·W² (A. Grau Carles, Nucl. Instr. Meth. A 551 (2005) 312, измерен в 60–600 кэВ), принятый '
     u'LNHB; сверка: I. Alekseev et al., Phys. Rev. C 102 (2020) 064329, C(W) = 1 − 0.4363·W + 0.0523·W²'),
    (4, '234PAm1', 'B-', 1224.0, 1044.469, '1st non-unique', 405.6, 'points', None, 'Pa234m_B-_1224.tsv',
     u'LNHB/DDEP Pa-234m (Huang Xiaolong, Wang Baosong, CNDC, 2009–2011), табл. «Electron Emissions» '
     u'β−0,9 avg 405.6 кэВ; ' + BETASHAPE + u', ' + LNHB + u'Pa-234m.BetaShape.zip, '
     u'beta-_Pa234m_trans3.bs, столбец «dN/dE calc.» (ξ-приближение, 0− → 0+ уровня U-234 1044.469 кэВ; '
     u'опытного фактора формы нет)'),
    # (`AMBER274fix`, 30.10.2026, второй заход) уникальные первые и главная ветвь Pa-234m:
    # форма кода расходится с LNHB на +2.5 / +2.1 / +2.4 %. Столбец «calc.» — по слову
    # распорядителя; опытные факторы этих ветвей в архиве есть и названы в источнике.
    (5, '90SR', 'B-', 545.9, 0.0, '1st unique', 196.0, 'points', None, 'Sr90_B-_546.tsv',
     u'LNHB/DDEP Sr-90 (V. Chisté, 2005), табл. «Electron Emissions» β−0,0 avg 196 кэВ; ' + BETASHAPE + u', '
     + LNHB + u'Sr-90.BetaShape.zip, beta-_Sr90_trans0.bs, столбец «dN/dE calc.» (расчёт, средняя 195.5); '
     u'столбец «exp.» — фактор (1 − 0.054·W)(q² + λ₂p²), H. Daniel и др., Phys. Rev. 136 (1964) B1240, '
     u'средняя 193.5 — не взят'),
    (6, '90Y', 'B-', 2278.5, 0.0, '1st unique', 926.7, 'points', None, 'Y90_B-_2278.tsv',
     u'LNHB/DDEP Y-90 (V. Chisté, M.M. Bé, 2005–2015), табл. «Electron Emissions» β−0,0 avg 926.7 кэВ; '
     + BETASHAPE + u', ' + LNHB + u'Y-90.BetaShape.zip, beta-_Y90_trans0.bs, столбец «dN/dE calc.» (расчёт, '
     u'средняя 929.7); столбец «exp.» — фактор (1 − 0.0114·W)(q² + λ₂p²), D. Flothmann и др., Z. Physik A 272 '
     u'(1975) 219, средняя 924.9 — не взят'),
    (7, '234PAm1', 'B-', 2269.0, 0.0, '1st non-unique', 820.5, 'points', None, 'Pa234m_B-_2290.tsv',
     u'LNHB/DDEP Pa-234m (Huang Xiaolong, Wang Baosong, CNDC, 2009–2011), табл. «Electron Emissions» '
     u'β−0,0 avg 820.5 кэВ; ' + BETASHAPE + u', ' + LNHB + u'Pa-234m.BetaShape.zip, beta-_Pa234m_trans0.bs, '
     u'столбец «dN/dE calc.» (ξ-приближение, 0− → 0+ основного U-234; опытного фактора формы нет); '
     u'граница ветви — LNHB 2269 (nucdb до 01.11.2026 несла 2290 NuDat; исправлена по LNHB '
     u'`tools/nucdb/correct_lnhb.py`, решение Amber 30.10.2026 «Сверить и поправить по LNHB»)'),
]

#: Строки, у которых граница ряда (источника) НАРОЧНО расходится с границей базы
#: больше допуска читателя: id -> граница ряда, кэВ. Иначе — останов (ряд чужой ветви).
#: (`AMBER274`, 01.11.2026) расхождение Pa-234m 2290/2269 снято правкой `nucdb` по LNHB
#: (`correct_lnhb.py`): у всех семи ветвей граница ряда — в допуске читателя от границы базы, словарь пуст.
SOURCE_ENDPOINT = {
}

KINDS = ('points', 'factor')
TRANSITIONS = re.compile(r'^(allowed|(1st|2nd|3rd|[4-9]th) (unique|non-unique))$')

#: Схема — ровно та, что читает приложение (распорядитель `amber274fix`, 30.10.2026).
SCHEMA = u"""
    drop table if exists beta_shape_points;
    drop table if exists beta_shapes;
    create table beta_shapes (
        id INTEGER PRIMARY KEY,
        parent_nucid TEXT NOT NULL,
        type_a TEXT NOT NULL,
        endpoint_kev REAL NOT NULL,
        level_kev REAL,
        transition TEXT NOT NULL,
        mean_kev REAL,
        kind TEXT NOT NULL,
        a0 REAL, a1 REAL, a2 REAL, a3 REAL,
        source TEXT NOT NULL
    );
    create table beta_shape_points (
        shape_id INTEGER NOT NULL REFERENCES beta_shapes(id),
        t_kev REAL NOT NULL,
        density REAL NOT NULL,
        PRIMARY KEY (shape_id, t_kev)
    );
"""

#: Допуски проверок. Средняя ряда против средней источника — 2 % (приёмка формы
#: `AMBER274fix`); шаг узлов — не реже 2 % границы.
MEAN_TOL = 0.02
STEP_TOL = 0.02

ME = 510.99895
ALPHA = 1.0 / 137.035999084
COMPTON_FM = 386.15926796


def read_series(path):
    u"""Узлы ряда: [(текст t, текст плотности)], проверены."""
    rows = []
    header_seen = False
    with io.open(path, encoding='utf-8') as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            if not header_seen:
                if line.split('\t') != ['t_kev', 'density']:
                    raise SystemExit(u'%s: шапка не «t_kev<TAB>density»: %r' % (path, line))
                header_seen = True
                continue
            parts = line.split('\t')
            if len(parts) != 2:
                raise SystemExit(u'%s: строка не из двух чисел: %r' % (path, line))
            t, d = float(parts[0]), float(parts[1])
            if not (math.isfinite(t) and math.isfinite(d) and t >= 0 and d >= 0):
                raise SystemExit(u'%s: негодный узел %r' % (path, line))
            if rows and not t > float(rows[-1][0]):
                raise SystemExit(u'%s: энергии не возрастают у %r' % (path, line))
            rows.append((parts[0], parts[1]))
    if len(rows) < 3:
        raise SystemExit(u'%s: меньше трёх узлов' % path)
    return rows


def points_mean(rows):
    u"""Средняя по узлам при ЛИНЕЙНОЙ плотности между ними (точный интеграл ломаной)."""
    n0 = m1 = 0.0
    for (t0, d0), (t1, d1) in zip(rows, rows[1:]):
        h = t1 - t0
        n0 += h * (d0 + d1) / 2.0
        m1 += h * (d0 * (2 * t0 + t1) + d1 * (t0 + 2 * t1)) / 6.0
    return m1 / n0


def re_ln_gamma(x, y):
    u"""Re ln Γ(x + iy) — Ланцош g = 7, 9 членов, как `FsaBetaLibrary.ReLnGamma`."""
    c = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
         -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
         1.5056327351493116e-7]
    zr, zi = x - 1.0, y
    ar, ai = c[0], 0.0
    for k in range(1, len(c)):
        dr = zr + k
        d = dr * dr + zi * zi
        ar += c[k] * dr / d
        ai -= c[k] * zi / d
    tr, ti = zr + 7.5, zi
    lnr = 0.5 * math.log(tr * tr + ti * ti)
    theta = math.atan2(ti, tr)
    return 0.5 * math.log(2 * math.pi) + (zr + 0.5) * lnr - zi * theta - tr + 0.5 * math.log(ar * ar + ai * ai)


def allowed_density(e0, z, a, positron, t, factor=None):
    u"""Разрешённая форма кода p·W·q²·F₀ (точечное ядро), при `factor` — умноженная на C(W)."""
    if not 0.0 < t < e0:
        return 0.0
    w = 1.0 + t / ME
    p = math.sqrt(w * w - 1.0)
    q = 1.0 + e0 / ME - w
    az = min(ALPHA * z, 0.99)
    g = math.sqrt(1.0 - az * az)
    eta = (-1.0 if positron else 1.0) * az * w / p
    r = 1.2 * max(1, a) ** (1.0 / 3.0) / COMPTON_FM
    f0 = math.exp(math.log(2 * (1 + g)) + 2 * (g - 1) * math.log(2 * p * r) + math.pi * eta
                  + 2 * re_ln_gamma(g, eta) - 2 * re_ln_gamma(2 * g + 1, 0.0))
    v = p * w * q * q * f0
    if factor is not None:
        a0, a1, a2, a3 = factor
        v *= a0 + a1 * w + a2 / w + a3 * w * w
    return v if v > 0 and math.isfinite(v) else 0.0


def allowed_mean(e0, z, a, positron, factor=None, steps=2048):
    u"""Средняя разрешённой формы (× C(W)) — Симпсон, как `FsaBetaLibrary.Integral`."""
    h = e0 / steps
    s = f1 = 0.0
    for i in range(steps + 1):
        t = 1e-6 * e0 if i == 0 else (e0 * (1 - 1e-9) if i == steps else h * i)
        f = allowed_density(e0, z, a, positron, t, factor)
        c = 1.0 if i in (0, steps) else (4.0 if i % 2 else 2.0)
        s += c * f
        f1 += c * f * t
    return f1 / s if s > 0 else float('nan')


def db_endpoint(text):
    t = (text or '').strip().strip('() ')
    try:
        return float(t)
    except ValueError:
        return float('nan')


def check_branch(db, sid, nucid, type_a, endpoint):
    u"""Ровно одна ветвь базы в допуске читателя, и её граница равна `endpoint_kev`."""
    row = db.execute('select z from nuclides where nucid = ? order by l_seqno desc limit 1', (nucid,)).fetchone()
    if row is None:
        raise SystemExit(u'строка %d: нет нуклида %s в nuclides' % (sid, nucid))
    z_parent = row[0]
    tol = max(2.0, 0.005 * endpoint)
    hits = []
    for (ep, inten, mean, lseq) in db.execute(
            'select endpoint, intensity_num, energy_num, parent_l_seqno from decay_radiations'
            ' where parent_nucid = ? and type_a = ?', (nucid, type_a)):
        v = db_endpoint(ep)
        if math.isfinite(v) and abs(v - endpoint) <= tol:
            hits.append((v, inten, mean, lseq, ep))
    if len(hits) != 1:
        raise SystemExit(u'строка %d (%s %s %.2f): ветвей базы в допуске ±%.2f кэВ — %d, нужна одна: %r'
                         % (sid, nucid, type_a, endpoint, tol, len(hits), hits))
    if hits[0][0] != endpoint:
        raise SystemExit(u'строка %d: граница %.4f не равна границе базы %r' % (sid, endpoint, hits[0][4]))
    return z_parent, hits[0]


def fill(db):
    for sid, nucid, type_a, endpoint, level, transition, mean, kind, factor, fname, source in SHAPES:
        if kind not in KINDS or not TRANSITIONS.match(transition) or type_a not in ('B-', 'B+'):
            raise SystemExit(u'строка %d: негодные kind/transition/type_a' % sid)
        if (kind == 'points') != (fname is not None) or (kind == 'factor') != (factor is not None):
            raise SystemExit(u'строка %d: вид %s не согласован с рядом/множителем' % (sid, kind))
    cur = db.cursor()
    plan = []
    for sid, nucid, type_a, endpoint, level, transition, mean, kind, factor, fname, source in SHAPES:
        z_parent, hit = check_branch(db, sid, nucid, type_a, endpoint)
        positron = type_a == 'B+'
        e0_beta = endpoint - 2 * ME if positron else endpoint    # у B+ поле базы — энергия захвата
        z_d = z_parent - 1 if positron else z_parent + 1
        a = int(re.match(r'^(\d+)', nucid).group(1))
        code_allowed = allowed_mean(e0_beta, z_d, a, positron)
        rows = None
        if kind == 'points':
            rows = read_series(os.path.join(DATA, fname))
            num = [(float(t), float(d)) for t, d in rows]
            if num[0][0] != 0.0 or num[-1][1] != 0.0:
                raise SystemExit(u'%s: ряд не от 0 или плотность на границе не 0' % fname)
            t_max = num[-1][0]
            if sid in SOURCE_ENDPOINT:
                if t_max != SOURCE_ENDPOINT[sid]:
                    raise SystemExit(u'%s: граница ряда %.2f не та, что заявлена (%.2f)' % (fname, t_max, SOURCE_ENDPOINT[sid]))
            elif abs(t_max - e0_beta) > max(2.0, 0.005 * e0_beta):
                raise SystemExit(u'%s: граница ряда %.2f далека от границы базы %.2f' % (fname, t_max, e0_beta))
            step = max(b[0] - a_[0] for a_, b in zip(num, num[1:]))
            if step > STEP_TOL * t_max:
                raise SystemExit(u'%s: шаг %.3g кэВ реже %.0f %% границы' % (fname, step, 100 * STEP_TOL))
            got = points_mean(num)
            how = u'ряд %d узлов, шаг ≤ %.3g кэВ (%.2f %% E0), граница ряда %.2f' % (len(num), step, 100 * step / t_max, t_max)
        else:
            got = allowed_mean(e0_beta, z_d, a, positron, factor)
            how = u'C(W) = %g %+g·W %+g/W %+g·W²' % factor
        if mean is not None and abs(got / mean - 1) > MEAN_TOL:
            raise SystemExit(u'строка %d: средняя формы %.2f против источника %.2f (%+.2f %%) — вне %.0f %%'
                             % (sid, got, mean, 100 * (got / mean - 1), 100 * MEAN_TOL))
        plan.append((sid, nucid, type_a, endpoint, level, transition, mean, kind, factor, rows, source,
                     got, code_allowed, how, hit))

    # Идемпотентность ДО БАЙТА: если обе таблицы уже несут ровно это содержимое, файл не
    # переписывается — иначе каждый прогон менял бы счётчик изменений SQLite в шапке файла
    # и git получал бы новую копию базы без единого нового числа.
    want_shapes = []
    want_points = []
    for (sid, nucid, type_a, endpoint, level, transition, mean, kind, factor, rows, source, got, code_allowed, how, hit) in plan:
        f = factor or (None, None, None, None)
        want_shapes.append((sid, nucid, type_a, endpoint, level, transition, mean, kind, f[0], f[1], f[2], f[3], source))
        want_points.extend((sid, float(t), float(d)) for t, d in (rows or []))
    have = set(r[0] for r in cur.execute("select name from sqlite_master where type = 'table'"))
    if 'beta_shapes' in have and 'beta_shape_points' in have:
        same = (cur.execute('select * from beta_shapes order by id').fetchall() == want_shapes
                and cur.execute('select * from beta_shape_points order by shape_id, t_kev').fetchall() == want_points)
        if same:
            report(plan, cur)
            print(u'содержимое уже то же — база не переписывается')
            return False

    cur.executescript(SCHEMA)
    for (sid, nucid, type_a, endpoint, level, transition, mean, kind, factor, rows, source, got, code_allowed, how, hit) in plan:
        f = factor or (None, None, None, None)
        cur.execute('insert into beta_shapes values (?,?,?,?,?,?,?,?,?,?,?,?,?)',
                    (sid, nucid, type_a, endpoint, level, transition, mean, kind, f[0], f[1], f[2], f[3], source))
        if rows:
            cur.executemany('insert into beta_shape_points values (?,?,?)',
                            [(sid, float(t), float(d)) for t, d in rows])
    db.commit()

    # Сверка записанного с рядами — побитово.
    for (sid, nucid, type_a, endpoint, level, transition, mean, kind, factor, rows, source, got, code_allowed, how, hit) in plan:
        if rows:
            back = cur.execute('select t_kev, density from beta_shape_points where shape_id = ? order by t_kev', (sid,)).fetchall()
            if back != [(float(t), float(d)) for t, d in rows]:
                raise SystemExit(u'строка %d: записанное разошлось с рядом' % sid)
    report(plan, cur)
    return True


def report(plan, cur):
    for (sid, nucid, type_a, endpoint, level, transition, mean, kind, factor, rows, source, got, code_allowed, how, hit) in plan:
        print(u'%d %-8s %s %8.2f %-15s %s: средняя формы %7.2f, источник %7.2f (%+.2f %%); '
              u'разрешённая форма кода %7.2f; база: выход %s %%, средняя %s'
              % (sid, nucid, type_a, endpoint, transition, kind, got, mean, 100 * (got / mean - 1), code_allowed,
                 hit[1], hit[2]))
        print(u'      %s' % how)
    n1 = cur.execute('select count(*) from beta_shapes').fetchone()[0]
    n2 = cur.execute('select count(*) from beta_shape_points').fetchone()[0]
    print(u'beta_shapes %d строк, beta_shape_points %d строк' % (n1, n2))


def main(argv):
    path = DEFAULT_DB
    apply = False
    for a in argv:
        if a.startswith('--db='):
            path = a[5:]
        elif a == '--apply':
            apply = True
        else:
            raise SystemExit(__doc__)
    if not os.path.isfile(path):
        raise SystemExit(u'нет файла базы: %s' % path)
    if apply:
        db = sqlite3.connect(path)
    else:
        src = sqlite3.connect('file:' + path.replace('\\', '/') + '?mode=ro', uri=True)
        db = sqlite3.connect(':memory:')
        src.backup(db)
        src.close()
        print(u'(без --apply: пишется копия базы в памяти, файл не тронут)')
    try:
        wrote = fill(db)
    finally:
        db.close()
    if apply and wrote:
        print(u'записано в %s' % path)
    else:
        print(u'файл не изменён')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
