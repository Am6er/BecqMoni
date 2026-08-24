# -*- coding: utf-8 -*-
"""`D27`: привести `decay_chain.dec_type` из TEXT к INTEGER. ТОЛЬКО СЧЁТ по умолчанию.

⛔ Запись в базу — исключительно с прямого разрешения Amber на САМУ ЗАПИСЬ
(правило 09.08.2026). Без `--apply` скрипт НИЧЕГО не меняет.

Что и почему. `decay_chain.dec_type` объявлен и лежит как TEXT (`'1'`),
`l_decays.dec_type` — как INTEGER (`1`). ⚠ **Довод строки о том, что соединение
«молча не находит ничего», ИЗМЕРЕН 12.08.2026 и оказался НЕВЕРЕН:** SQLite
применяет числовую аффинность, соединение даёт 2955 строк и напрямую, и через
`cast`, литералы `= 1` и `= '1'` работают одинаково. Молчит сравнение НЕ в SQL,
а в питоне, где `'1' != 1`. Значит здесь косметика ради единообразия, а не
починка, и цена ошибки при записи выше цены неудобства.

⛔ **UPDATE ЗДЕСЬ НЕ РАБОТАЕТ, и это проверено 24.08.2026 на живой базе.**
Столбец ОБЪЯВЛЕН `TEXT NOT NULL`, то есть имеет текстовую аффинность: SQLite
приводит результат `cast(dec_type as integer)` обратно к тексту при самой
записи. `update` отчитался «4101 строка», все три поверки (счёт, сумма столбца,
соединение) прошли — а `typeof()` остался `text` у всех 4101. Поверка, которая
не смотрит на то, что должна была изменить, проходит всегда.

Поэтому смена типа здесь — **ПЕРЕСБОРКА ТАБЛИЦЫ**, а не правка значений, и это
отдельное разрешение: `dec_type` входит в ПЕРВИЧНЫЙ КЛЮЧ
(`nucid`, `l_seqno`, `daughter_nucid`, `dec_type`).

    python tools/nucdb/dec_type_to_int.py            # только счёт
    python tools/nucdb/dec_type_to_int.py --apply    # ПЕРЕСБОРКА (по разрешению Amber)
"""
import hashlib
import os
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DB = os.path.join(HERE, os.pardir, os.pardir, 'BecquerelMonitor', 'nucdb.sqlite')

NEW_TABLE = (
    'CREATE TABLE `decay_chain` ('
    '`nucid` TEXT NOT NULL, `l_seqno` INTEGER NOT NULL,'
    ' `daughter_nucid` TEXT NOT NULL, `dec_type` INTEGER NOT NULL, `perc` TEXT,'
    ' PRIMARY KEY(`nucid`, `l_seqno`, `daughter_nucid`, `dec_type`))')


def stats(cur):
    """Всё, что обязано совпасть до и после. Считается ПО ЗНАЧЕНИЯМ, а не по типу."""
    total = cur.execute('select count(*) from decay_chain').fetchone()[0]
    types = dict(cur.execute('select typeof(dec_type), count(*) from decay_chain'
                             ' group by 1').fetchall())
    # Отпечаток содержимого. Значения приведены к тексту в ПИТОНЕ и строки
    # отсортированы там же: `group_concat` порядок не гарантирует, а отпечаток,
    # который зависит от порядка выдачи, поверял бы сам себя.
    rows = cur.execute(
        'select nucid, l_seqno, daughter_nucid, cast(dec_type as integer),'
        ' coalesce(perc, \'\') from decay_chain').fetchall()
    digest = hashlib.sha256(
        '|'.join('/'.join(str(v) for v in r) for r in sorted(rows, key=repr))
        .encode('utf-8')).hexdigest()[:16]
    join = cur.execute(
        'select count(*) from decay_chain c join l_decays d'
        '  on d.nucid = c.nucid and d.l_seqno = c.l_seqno'
        ' and d.dec_type = c.dec_type').fetchone()[0]
    return total, types, digest, join


def unconvertible(cur):
    return cur.execute(
        "select dec_type, count(*) from decay_chain"
        " where dec_type is null or trim(cast(dec_type as text)) = ''"
        "    or cast(dec_type as integer) is null"
        "    or cast(cast(dec_type as integer) as text) <> trim(cast(dec_type as text))"
        " group by 1 order by 2 desc").fetchall()


def main():
    apply = '--apply' in sys.argv[1:]
    path = os.path.abspath(DB)
    uri = 'file:' + path.replace(os.sep, '/') + ('' if apply else '?mode=ro')
    con = sqlite3.connect(uri, uri=True)
    cur = con.cursor()

    total, types, digest, join = stats(cur)
    bad = unconvertible(cur)
    decl = cur.execute("select sql from sqlite_master where name = 'decay_chain'"
                       ).fetchone()[0]
    indexes = cur.execute("select name, sql from sqlite_master"
                          " where type = 'index' and tbl_name = 'decay_chain'"
                          " and sql not null").fetchall()

    print('база: %s' % path)
    print('decay_chain: строк %d, класс хранения dec_type: %r' % (total, types))
    print('значения, которые целым НЕ станут: %d видов' % len(bad))
    for value, n in bad:
        print('   %-20r %d строк' % (value, n))
    print('соединение с l_decays: %d строк' % join)
    print('индексов у таблицы: %d %s'
          % (len(indexes), [n for n, _ in indexes] or ''))
    print('объявление: %s' % decl)

    if 'text' not in types:
        print()
        print('✅ УЖЕ ЦЕЛОЕ — делать нечего.')
        return 0

    if not apply:
        print()
        print('⛔ НИЧЕГО НЕ ЗАПИСАНО. Смена типа здесь — ПЕРЕСБОРКА ТАБЛИЦЫ')
        print('   (столбец объявлен TEXT и входит в первичный ключ), а не update:')
        print('   update молча возвращается к тексту той же аффинностью.')
        print('   Нужен ключ --apply И прямое разрешение Amber на пересборку.')
        return 0

    if bad:
        print()
        print('⛔ ОТКАЗ: есть значения, которые целым не станут — терять их нельзя.')
        return 2

    cur.execute('PRAGMA foreign_keys = OFF')
    cur.execute('BEGIN')
    try:
        cur.execute('ALTER TABLE `decay_chain` RENAME TO `decay_chain__old`')
        cur.execute(NEW_TABLE)
        cur.execute('INSERT INTO `decay_chain`'
                    ' (`nucid`, `l_seqno`, `daughter_nucid`, `dec_type`, `perc`)'
                    ' SELECT `nucid`, `l_seqno`, `daughter_nucid`,'
                    '        cast(`dec_type` as integer), `perc`'
                    ' FROM `decay_chain__old`')
        for name, sql in indexes:
            cur.execute(sql)
        cur.execute('DROP TABLE `decay_chain__old`')

        total2, types2, digest2, join2 = stats(cur)
        problems = []
        if total2 != total:
            problems.append('строк %d вместо %d' % (total2, total))
        if digest2 != digest:
            problems.append('отпечаток содержимого не сошёлся')
        if join2 != join:
            problems.append('соединение %d вместо %d' % (join2, join))
        if 'text' in types2:
            problems.append('класс хранения ОСТАЛСЯ текстовым: %r' % types2)
        integrity = cur.execute('PRAGMA integrity_check').fetchone()[0]
        if integrity != 'ok':
            problems.append('integrity_check: %s' % integrity)
        if problems:
            con.rollback()
            print()
            print('⛔ ОТКАЗ И ОТКАТ: ' + '; '.join(problems))
            return 2
        con.commit()
    except Exception as ex:
        con.rollback()
        print()
        print('⛔ ОТКАЗ И ОТКАТ по ошибке: %s' % ex)
        return 2

    print()
    print('✅ ПЕРЕСОБРАНО: строк %d, класс хранения %r, соединение %d, '
          'integrity_check ok, отпечаток содержимого тот же'
          % (total2, types2, join2))
    print('   индексов восстановлено: %d' % len(indexes))
    return 0


if __name__ == '__main__':
    sys.exit(main())
