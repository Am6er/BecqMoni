# -*- coding: utf-8 -*-
"""Режет единую базу на три файла — по СКОРОСТИ ИЗМЕНЕНИЯ, а не поровну.

Зачем. `nucdb.sqlite` — бинарник, дельты по нему git не берёт: каждый коммит,
тронувший базу, кладёт в историю полную копию. К 08.08.2026 в истории лежало
восемь версий (5.6 → 6.5 → 18.7 → 35.8 → 35.9 → 57.4 МБ) — около 166 МБ из
223 МБ всего `.git`. Пока замороженные поставки лежат в одном файле с той
частью, которую правят каждую неделю, каждая правка тащит за собой и их.

Разрез. Три файла, и граница проходит там же, где граница потребителя —
поэтому ни одному читателю не нужно двух файлов сразу:

  matdb.sqlite     ВЕЩЕСТВО (перенос): XCOM, EPICS2017, EPDL97, профили
                   Комптона, Зельцер — Бергер, EADL, ESTAR/STAR, свет
                   сцинтилляторов, ICC. Читают MaterialDatabase,
                   ScatteringData, BremsstrahlungData. Меняется при
                   перевтягивании поставок NIST/Geant4 — редко.

  schemedb.sqlite  СХЕМЫ уровней и распада: g4_level, g4_gamma (Geant4
                   PhotonEvaporation) и ensdf_* (разбор ЛСРМ, из которого
                   G4-схемы добираются). Читает AngularCorrelation.
                   Меняется при смене версии поставки — почти никогда.

  nucdb.sqlite     НУКЛИДЫ и СОВПАДЕНИЯ: ядерная часть NuclideMaster и
                   каскадные совпадения SandiaDecay. Читают NucBase и
                   FsaCascadeSummer. Это активно правимая часть — и она
                   же самая мелкая, ~8 МБ вместо 55.

Имя `nucdb.sqlite` сохранено за третьим куском нарочно: он и есть база
нуклидов, а два других — данные о веществе и схемы, которые в неё когда-то
доложили.

Единственный междуфайловый вызов, который был. `MaterialDatabase` брал
символы элементов запросом `select z, symbol from nuclides` — сто значений из
таблицы, уезжающей в другой файл. Символы перенесены в `xcom_elements.symbol`
по ТОМУ ЖЕ правилу отбора, что в C# (`order by z, symbol`, побеждает первый:
у одного z в базе лежат разные написания — Li/LI, Ti/TI, Ni/NI). Приведение
к каноническому виду по-прежнему делает C#, здесь только выбор строки.

Скрипт НИЧЕГО не меняет в источнике: читает его в режиме `mode=ro` и пишет
три новых файла в целевой каталог. Проверка обязательна и идёт всегда: по
каждой таблице сверяется число строк и контрольная сумма содержимого.

    python tools/nucdb/split_db.py BecquerelMonitor/nucdb.sqlite <куда>

Собрать обратно (для сверки или отката) — `--merge`:

    python tools/nucdb/split_db.py --merge <каталог с тремя> <куда.sqlite>
"""

import os
import sqlite3
import sys

# ---------------------------------------------------------------- раскладка

# Единственный источник правды о том, что где лежит. Правя его, правьте и
# `database/scheme.md`, §0.
PIECES = {
    "matdb.sqlite": [
        "xcom_elements", "xcom_cross_sections", "xcom_edges",
        "epics_photo_meta", "epics_photo_fit", "epics_photo_subshell",
        "epdl_form_factor", "epdl_scattering_function",
        "compton_profile", "compton_profile_shell", "compton_profile_momentum",
        "seltzer_berger", "seltzer_berger_grid",
        "eadl_binding", "eadl_radiative", "eadl_auger",
        "xray_fluorescence", "fluorescence_yield", "fluorescence_k",
        "scint_npsm_params", "scint_electron_light_yield",
        "estar_shells", "estar_radiative_stopping", "estar_element_potential",
        "estar_collision_stopping",
        "star_materials", "star_material_composition", "star_stopping_powers",
        "icc_coefficients",
    ],
    "schemedb.sqlite": [
        "g4_level", "g4_gamma",
        "ensdf_datasets", "ensdf_levels", "ensdf_gammas", "ensdf_feedings",
    ],
    "nucdb.sqlite": [
        "nuclides", "decay_chain", "decay_radiations", "l_decays",
        "thermal_cross_sect", "cumulative_fission",
        "gamma_coincidence", "gamma_coincidence_line", "gamma_coincidence_parent",
    ],
}


def ro(path):
    """Источник открывается только на чтение — и никак иначе."""
    return sqlite3.connect(
        "file:%s?mode=ro" % path.replace("\\", "/"), uri=True)


def table_digest(con, table, cols):
    """Контрольная сумма содержимого таблицы, не зависящая от порядка строк.

    Складываем по модулю хеши строк: сумма коммутативна, поэтому разный
    порядок выдачи (а он МЕНЯЕТСЯ после пересоздания таблицы) не даёт ложной
    тревоги, а любая изменившаяся ячейка — даёт.

    Колонки передаются списком, а не берутся из `pragma`: сверять надо по
    колонкам ИСТОЧНИКА. У `xcom_elements` в куске появляется `symbol`,
    которого в источнике нет, и по «всем колонкам» сверка ругалась бы на
    единственное изменение, сделанное нарочно.
    """
    # `quote()` различает NULL, число и строку: 1 и '1' дадут разные хеши.
    expr = " || '\x1f' || ".join('quote("%s")' % c for c in cols)
    total = 0
    for (row,) in con.execute('select %s from "%s"' % (expr, table)):
        total = (total + hash(row)) % (1 << 61)
    return total


def split(source, outdir):
    src = ro(source)
    everything = set(r[0] for r in src.execute(
        "select name from sqlite_master where type='table'"
        " and name not like 'sqlite_%'"))
    planned = set(t for ts in PIECES.values() for t in ts)

    # Раскладка проверяется МАШИННО, а не глазами: таблица, забытая при
    # правке PIECES, иначе просто исчезнет — молча и безвозвратно.
    missing = everything - planned
    extra = planned - everything
    if missing:
        sys.exit("не распределены по кускам: %s" % sorted(missing))
    if extra:
        sys.exit("в раскладке есть, в базе нет: %s" % sorted(extra))

    if not os.path.isdir(outdir):
        os.makedirs(outdir)

    report = []
    for name, tables in PIECES.items():
        target = os.path.join(outdir, name)
        if os.path.exists(target):
            os.remove(target)
        dst = sqlite3.connect(target)
        dst.execute("attach database ? as src", (source,))

        # Схема переносится ДОСЛОВНО из sqlite_master, а не пересобирается:
        # так сохраняются первичные ключи, типы и порядок колонок.
        for table in tables:
            (sql,) = src.execute(
                "select sql from sqlite_master where type='table' and name=?",
                (table,)).fetchone()
            dst.execute(sql)
            dst.execute('insert into main."%s" select * from src."%s"'
                        % (table, table))

        for (sql,) in src.execute(
                "select sql from sqlite_master where type='index'"
                " and sql is not null and tbl_name in (%s)"
                % ",".join("?" * len(tables)), tables):
            dst.execute(sql)

        # Представления переносим туда, где лежат ВСЕ их таблицы. Иначе
        # представление сошлётся в пустоту, и это вскроется только у
        # пользователя. Если представление окажется разорванным между
        # кусками — падаем: молча потерять его нельзя.
        for vname, vsql in src.execute(
                "select name, sql from sqlite_master where type='view'"):
            used = set(t for t in everything if t in vsql)
            if not used:
                sys.exit("представление %s не ссылается ни на одну таблицу"
                         % vname)
            if used <= set(tables):
                dst.execute(vsql)
            elif used & set(tables):
                sys.exit("представление %s разорвано между кусками: %s"
                         % (vname, sorted(used)))

        if name == "matdb.sqlite":
            add_symbols(dst)

        dst.commit()
        dst.execute("detach database src")
        dst.execute("vacuum")
        dst.close()
        report.append((name, target, tables))

    verify(source, outdir, report)


def add_symbols(dst):
    """Символы элементов — в `xcom_elements`, чтобы вещество не тянуло нуклиды.

    Правило отбора буква в букву как в `MaterialDatabase`: z > 0 (нейтрон
    исключён — его «N» столкнулся бы с азотом), при нескольких написаниях
    берётся первое по возрастанию, `min()` при двоичном сравнении и есть
    первое в `order by symbol`.
    """
    dst.execute("alter table xcom_elements add column symbol TEXT")
    dst.execute(
        "update xcom_elements set symbol = ("
        "  select min(n.symbol) from src.nuclides n"
        "   where n.z = xcom_elements.z and n.symbol is not null and n.z > 0)")
    blank = dst.execute(
        "select count(*) from xcom_elements where symbol is null").fetchone()[0]
    if blank:
        # Молчать тут нельзя: без символа элемент выпадет из разбора формул,
        # а выглядеть это будет как «такого вещества не бывает».
        print("  ВНИМАНИЕ: без символа осталось элементов: %d" % blank)


def verify(source, outdir, report):
    """Сверка обязательная: строки и содержимое, по каждой таблице."""
    src = ro(source)
    print("проверка: таблица | строк в источнике | строк в куске | суммы")
    bad = 0
    total_rows = 0
    for name, target, tables in report:
        dst = ro(target)
        for table in tables:
            cols = [r[1] for r in src.execute('pragma table_info("%s")' % table)]
            a = src.execute('select count(*) from "%s"' % table).fetchone()[0]
            b = dst.execute('select count(*) from "%s"' % table).fetchone()[0]
            da = table_digest(src, table, cols)
            db = table_digest(dst, table, cols)
            ok = (a == b and da == db)
            if not ok:
                bad += 1
                print("  РАСХОЖДЕНИЕ %-30s %8d %8d %s"
                      % (table, a, b, "суммы разошлись" if a == b else ""))
            total_rows += b
        dst.close()

    # Перенесённые символы сверяем отдельно: это единственное, что не
    # копия, а вывод из другого куска. Молча разойтись оно не должно.
    mat = ro(os.path.join(outdir, "matdb.sqlite"))
    want = dict(src.execute(
        "select z, min(symbol) from nuclides"
        " where symbol is not null and z > 0 group by z"))
    got = dict(mat.execute(
        "select z, symbol from xcom_elements where symbol is not null"))
    mat.close()
    wrong = [z for z in got if want.get(z) != got[z]]
    print("  символы элементов: перенесено %d, расхождений %d, без символа %d"
          % (len(got), len(wrong), 100 - len(got)))
    if wrong:
        bad += len(wrong)
        print("    расходятся у z: %s" % sorted(wrong)[:20])

    src_rows = sum(
        src.execute('select count(*) from "%s"' % t).fetchone()[0]
        for ts in PIECES.values() for t in ts)
    print("  строк: источник %d, куски %d" % (src_rows, total_rows))
    if bad or src_rows != total_rows:
        sys.exit("СВЕРКА НЕ СОШЛАСЬ: расхождений %d" % bad)

    print("\nсошлось. размеры:")
    was = os.path.getsize(source)
    now = 0
    for name, target, tables in report:
        size = os.path.getsize(target)
        now += size
        print("  %-16s %6.1f МБ  таблиц %2d" % (name, size / 1048576.0, len(tables)))
    print("  %-16s %6.1f МБ  (было одним файлом %.1f МБ)"
          % ("итого", now / 1048576.0, was / 1048576.0))


def merge(indir, target):
    """Обратная сборка — для сверки и на случай отката."""
    if os.path.exists(target):
        os.remove(target)
    dst = sqlite3.connect(target)
    for i, name in enumerate(PIECES):
        path = os.path.join(indir, name)
        dst.execute("attach database ? as p%d" % i, (path,))
        src = ro(path)
        for (tname, sql) in src.execute(
                "select name, sql from sqlite_master where type='table'"
                " and name not like 'sqlite_%'"):
            dst.execute(sql)
            dst.execute('insert into main."%s" select * from p%d."%s"'
                        % (tname, i, tname))
        for (sql,) in src.execute(
                "select sql from sqlite_master where type in ('index','view')"
                " and sql is not null"):
            dst.execute(sql)
        src.close()
        dst.commit()
        dst.execute("detach database p%d" % i)
    dst.execute("vacuum")
    dst.close()
    print("собрано: %s (%.1f МБ)" % (target, os.path.getsize(target) / 1048576.0))


if __name__ == "__main__":
    if len(sys.argv) == 4 and sys.argv[1] == "--merge":
        merge(sys.argv[2], sys.argv[3])
    elif len(sys.argv) == 3:
        split(sys.argv[1], sys.argv[2])
    else:
        sys.exit(__doc__)
