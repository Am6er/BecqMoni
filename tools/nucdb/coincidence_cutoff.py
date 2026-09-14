# -*- coding: utf-8 -*-
u"""`D10`: чего стоит опустить отсечку гамма-совпадений. ЧИСЛА, а не пересборка.

⛔ **Инструмент НИЧЕГО НЕ ПИШЕТ в `nucdb.sqlite`.** Запись в поставочные базы
запрещена приказом Amber 09.08.2026 и требует отдельного разрешения на САМУ
ЗАПИСЬ. Ключ `--apply` присутствует, ВЫКЛЮЧЕН по умолчанию (решение Amber
25.08.2026 по всей серии `D`) и, будучи указан, печатает готовую команду и
ОТКАЗЫВАЕТ кодом 2 — применение делает `import_sandia_coincidence.py`, и его
запуск назначает Amber.

Что меряется. В базу взяты пары, где обе линии дают не меньше `MIN_INTENSITY`
процента на распад родителя, а доля совпадения не меньше `MIN_FRACTION`
(сегодня 0.1 % и 0.001). Отброшенное этой отсечкой — то, чего у нас нет вовсе:
слабые каскады. Инструмент прогоняет сетку отсечек по `sandia.decay.xml` и для
каждой печатает, сколько пар, линий и родителей осталось бы, сколько отброшено
и во что это обошлось бы базе.

⚠ **Размер базы меряется, а не оценивается формулой.** С ключом `--size` для
каждого узла сетки собирается ОТДЕЛЬНАЯ временная база с той же схемой и той же
укладкой целыми, что у `import_sandia_coincidence.py`, и её файл после `vacuum`
взвешивается. Прикидка «столько-то байт на строку» здесь врёт: у трёх таблиц
разная ширина, а индексов у них нет вовсе.

    python tools/nucdb/coincidence_cutoff.py <sandia.decay.xml>
    python tools/nucdb/coincidence_cutoff.py <sandia.decay.xml> --size
    python tools/nucdb/coincidence_cutoff.py <sandia.decay.xml> \
        --grid=0.1:0.001,0.01:0.0001,0.001:0.00001,0:0
    python tools/nucdb/coincidence_cutoff.py <sandia.decay.xml> \
        --check --db=BecquerelMonitor/nucdb.sqlite

`--check` сверяет узел сетки, совпадающий с сегодняшней отсечкой, с тем, что
РЕАЛЬНО лежит в базе (читается только на чтение, `mode=ro`), и возвращает 1 при
расхождении. Это и есть положительный контроль разбора: если пересчёт по
поставке не даёт сегодняшних 128 429 пар — считать нечего, разбор врёт.

Источник — InterSpec / SandiaDecay, Sandia National Laboratories (NTESS),
LGPL v2.1; разбор поставки — `tools/interspec/README.md`.
"""

import os
import sqlite3
import sys
import tempfile
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ).
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

import import_sandia_coincidence as imp

#: Сетка отсечек по умолчанию: (минимальный выход линии, %; минимальная доля).
#: Первый узел — сегодняшний, он же положительный контроль.
DEFAULT_GRID = [(0.1, 0.001), (0.03, 0.0003), (0.01, 0.0001),
                (0.001, 0.00001), (0.0, 0.0)]

SCHEMA = u"""
    create table gamma_coincidence_parent (
        id            integer primary key,
        sandia_symbol text not null,
        nucid         text,
        isomer        integer,
        l_seqno       integer
    );
    create table gamma_coincidence_line (
        parent_id     integer not null,
        energy_mkev   integer not null,
        intensity_ppm integer not null
    );
    create table gamma_coincidence (
        parent_id         integer not null,
        energy_mkev       integer not null,
        coinc_energy_mkev integer not null,
        fraction_ppm      integer not null
    );
"""

#: ⚠ Указатели ставятся ТОЖЕ, и это не мелочь: на сегодняшней отсечке они
#: весят 1.6 МБ (T22), то есть почти половину самих таблиц. Взвешивание без
#: них занизило бы цену вдвое.
SCHEMA_INDEXES = u"""
    create index ix_gamma_coincidence_parent
        on gamma_coincidence_parent(nucid, l_seqno);
    create index ix_gamma_coincidence_parent_id
        on gamma_coincidence(parent_id);
    create index ix_gamma_coincidence_line_parent_id
        on gamma_coincidence_line(parent_id);
"""


def select(lines, pairs, min_intensity, min_fraction):
    u"""Отобрать пары при заданной отсечке.

    Возвращает `(родителей, пар, линий, отброшено пар)` и, если нужно, сами
    строки для взвешивания. Правило отбора СЛОВО В СЛОВО то же, что в
    `import_sandia_coincidence.main`, — иначе меряется не та отсечка.
    """
    parents = []          # (символ, [(e1, e2, доля)], {энергия: выход})
    dropped = 0
    weight = 0.0
    by_reason = {"доля": 0, "выход": 0, "оба": 0}
    for symbol in sorted(pairs):
        kept = []
        used = set()
        for (e1, e2), fraction in pairs[symbol].items():
            i1 = lines[symbol].get(e1, 0.0)
            i2 = lines[symbol].get(e2, 0.0)
            thin = fraction < min_fraction
            weak = i1 < min_intensity or i2 < min_intensity
            if thin or weak:
                dropped += 1
                by_reason["оба" if (thin and weak)
                          else ("доля" if thin else "выход")] += 1
                continue
            kept.append((e1, e2, fraction))
            # ⚠ Вес пары — I₁·f, и он ОДИН НА ПАРУ, а не два разных по
            # направлениям: обратная условная равна f·I₁/I₂ (так устроена
            # укладка, см. шапку импортёра), и I₂·(f·I₁/I₂) даёт то же I₁·f.
            # То есть это ЧИСЛО СОВПАДАЮЩИХ ПАР на 100 распадов родителя —
            # ровно та величина, которую поправка на суммирование и двигает.
            #
            # ⛔ ДОЛЯ ЗАЖИМАЕТСЯ В ЕДИНИЦУ, И БЕЗ ЭТОГО МЕРА БЕССМЫСЛЕННА.
            # В поставке есть строки с «условной вероятностью» до 5·10⁸
            # (`Mo93m`, `Er151m`, `Ir189m2` — 1.4 % пар базы), и незажатая
            # сумма меряет не физику, а величину мусора: без зажима один
            # `Mo93m` даёт 25.4 млн из 27.5 млн всего веса. Зажим — тот же,
            # что стоит в `FsaCascadeSummer.SurviveAll`, то есть меряется
            # ровно то, чем пользуется приложение.
            share = fraction if fraction < 1.0 else 1.0
            weight += lines[symbol].get(e1, 0.0) * share
            used.add(e1)
            used.add(e2)
        if not kept:
            continue
        parents.append((symbol, kept, {e: lines[symbol][e] for e in used}))
    n_pairs = sum(len(k) for _, k, _ in parents)
    n_lines = sum(len(u) for _, _, u in parents)
    return parents, n_pairs, n_lines, dropped, weight, by_reason


def weigh(parents):
    u"""Собрать временную базу той же схемы и вернуть её размер в байтах."""
    fd, path = tempfile.mkstemp(suffix=".sqlite", prefix="coinc_")
    os.close(fd)
    os.remove(path)
    db = sqlite3.connect(path)
    try:
        db.executescript(SCHEMA)
        pid = 0
        for symbol, kept, used in parents:
            pid += 1
            nucid, isomer, _state = imp.to_nucid(symbol)
            db.execute("insert into gamma_coincidence_parent values (?,?,?,?,?)",
                       (pid, symbol, nucid, isomer, None))
            db.executemany("insert into gamma_coincidence_line values (?,?,?)",
                           [(pid, imp.mkev(e), imp.ppm(i / 100.0))
                            for e, i in used.items()])
            db.executemany("insert into gamma_coincidence values (?,?,?,?)",
                           [(pid, imp.mkev(e1), imp.mkev(e2), imp.ppm(f))
                            for e1, e2, f in kept])
        db.executescript(SCHEMA_INDEXES)
        db.commit()
        db.execute("vacuum")
        db.close()
        return os.path.getsize(path)
    finally:
        try:
            os.remove(path)
        except OSError:
            pass


def parse_grid(text):
    grid = []
    for node in text.split(","):
        node = node.strip()
        if not node:
            continue
        a, _, b = node.partition(":")
        grid.append((float(a), float(b)))
    return grid


def main():
    args = [a for a in sys.argv[1:]]
    if any(a == "--apply" for a in args):
        sys.stderr.write(
            u"⛔ ОТКАЗ: `--apply` в этом инструменте не исполняется.\n"
            u"   Запись в поставочные базы запрещена приказом Amber 09.08.2026\n"
            u"   и требует отдельного разрешения на САМУ ЗАПИСЬ.\n"
            u"   Применение — перезапуск импортёра с другой отсечкой:\n"
            u"       python tools/nucdb/import_sandia_coincidence.py \\\n"
            u"           BecquerelMonitor/nucdb.sqlite <sandia.decay.xml> \\\n"
            u"           --min-intensity=<%> --min-fraction=<доля>\n"
            u"   ⚠ После пересборки таблиц совпадений заново ставится\n"
            u"   `l_seqno` изомеров (tools/nucdb/link_isomer_parents.py).\n")
        return 2

    xml_path = None
    db_path = None
    grid = DEFAULT_GRID
    want_size = False
    want_check = False
    for a in args:
        if a.startswith("--grid="):
            grid = parse_grid(a[7:])
        elif a.startswith("--db="):
            db_path = a[5:]
        elif a == "--size":
            want_size = True
        elif a == "--check":
            want_check = True
        elif a.startswith("--"):
            sys.exit("неизвестный ключ %s" % a)
        elif xml_path is None:
            xml_path = a
        else:
            sys.exit("лишний довод %s" % a)

    if xml_path is None or not os.path.isfile(xml_path):
        sys.exit("usage: coincidence_cutoff.py <sandia.decay.xml> "
                 "[--grid=a:b,...] [--size] [--check --db=nucdb.sqlite]")

    t0 = time.time()
    lines, pairs = imp.collect(xml_path)
    t_parse = time.time() - t0
    total_src = sum(len(p) for p in pairs.values())
    print(u"поставка: %s, %.1f МБ; разбор XML %.1f с"
          % (os.path.basename(xml_path),
             os.path.getsize(xml_path) / 1048576.0, t_parse))
    print(u"различных пар у всех родителей: %d; родителей с парами: %d"
          % (total_src, len(pairs)))
    print()
    head = (u"  выход ≥ %%    доля ≥      родителей     пар     линий  "
            u"отброшено пар   Σ I₁·f    от всего")
    if want_size:
        head += u"   вес таблиц"
    print(head)

    base = None
    full_weight = None
    rows = []
    for min_i, min_f in grid:
        t1 = time.time()
        parents, n_pairs, n_lines, dropped, weight, why = select(
            lines, pairs, min_i, min_f)
        if min_i <= 0.0 and min_f <= 0.0:
            full_weight = weight
        size = None
        if want_size:
            size = weigh(parents)
            if base is None:
                base = size
        rows.append([min_i, min_f, len(parents), n_pairs, n_lines, dropped,
                     size, weight, time.time() - t1, why])

    for r in rows:
        line = (u"  %8.4f  %10.6f  %9d  %8d  %8d  %11d  %9.1f  %s"
                % (r[0], r[1], r[2], r[3], r[4], r[5], r[7],
                   u"%7.3f %%" % (100.0 * r[7] / full_weight)
                   if full_weight else u"      —"))
        if r[6] is not None:
            line += u"  %7.2f МБ (%+.2f)" % (r[6] / 1048576.0,
                                             (r[6] - base) / 1048576.0)
        print(line + u"   [%.1f с]" % r[8])

    print()
    for r in rows:
        w = r[9]
        print(u"  отсечка %g / %g: отброшено %d — по доле %d, по выходу линии %d, "
              u"по обоим %d"
              % (r[0], r[1], r[5], w[u"доля"], w[u"выход"], w[u"оба"]))
    rows = [(r[0], r[1], r[2], r[3], r[4], r[5], r[6]) for r in rows]

    if not want_check:
        return 0

    if not db_path:
        sys.exit("--check требует --db=<nucdb.sqlite>")
    db = sqlite3.connect("file:%s?mode=ro" % db_path.replace("\\", "/"), uri=True)
    have = {t: db.execute("select count(*) from " + t).fetchone()[0]
            for t in ("gamma_coincidence", "gamma_coincidence_line",
                      "gamma_coincidence_parent")}
    db.close()
    node = None
    for r in rows:
        if abs(r[0] - imp.MIN_INTENSITY) < 1e-12 and abs(r[1] - imp.MIN_FRACTION) < 1e-15:
            node = r
            break
    if node is None:
        sys.exit(u"⛔ в сетке нет узла с сегодняшней отсечкой (%.4f, %.6f) — "
                 u"сверять не с чем" % (imp.MIN_INTENSITY, imp.MIN_FRACTION))
    want = {"gamma_coincidence": node[3],
            "gamma_coincidence_line": node[4],
            "gamma_coincidence_parent": node[2]}
    bad = [t for t in want if want[t] != have[t]]
    print()
    for t in sorted(want):
        print(u"  %-26s пересчёт %7d, в базе %7d  %s"
              % (t, want[t], have[t], u"✓" if want[t] == have[t] else u"⛔ РАСХОЖДЕНИЕ"))
    if bad:
        print(u"⛔ пересчёт по поставке НЕ сходится с базой — числа ниже "
              u"по сетке доверия не заслуживают")
        return 1
    print(u"✓ пересчёт сходится с базой на сегодняшней отсечке")
    return 0


if __name__ == "__main__":
    sys.exit(main())
