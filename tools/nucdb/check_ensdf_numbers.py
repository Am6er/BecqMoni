# -*- coding: utf-8 -*-
u"""Сторож чисел ENSDF и совпадений: `database/scheme.md` против самих баз.

Зачем. Числа §7 и §8 схемы — это то, чем пользуются, не открывая базу, и они
УЖЕ врали трижды. `W17`: «35437 уровней» было числом РАЗОБРАННОГО, а в таблицу
ложилось 30857. 10.09.2026 (полоса П15) нашлись ещё три того же разряда —
«3486 наборов» при 3484 строках, «1920 дочерних» при 1919 и «23518 питаний»
при 23498: во всех трёх в документ уехало число прочитанных ЗАПИСЕЙ, а не
строк таблицы, и разница — ровно тестовые заглушки ЛСРМ `290XX`, вычищенные
`D17`. Глаз этого не ловит: цифры правдоподобны и стоят рядом с верными.

Что судит. Таблицу «Числа таблиц» в §7 и §8 `database/scheme.md`: каждая её
строка называет таблицу, базу и число строк, и сторож сверяет их СЧЁТОМ.
Читаются базы только на чтение (`mode=ro`), не пишется ничего.

⚠ Сторож НЕ судит поставку: `C:\\LSRM\\…\\ENSDF2` есть не на всякой машине, и
перепись поставки живёт отдельным ключом `--census`, который в приёмку не
входит. Без ключа сторожу нужны только дерево и три базы.

  python tools/nucdb/check_ensdf_numbers.py
  python tools/nucdb/check_ensdf_numbers.py --doc=<копия scheme.md>   # контроль
  python tools/nucdb/check_ensdf_numbers.py --census[=<каталог ENSDF2>]

Коды возврата: 0 — сошлось; 1 — расхождение (названо построчно); 2 — нет
файла или таблицы, по которой судить.
"""

import io
import os
import re
import sqlite3
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DOC = os.path.join(ROOT, "database", "scheme.md")
DBS = {
    "schemedb": os.path.join(ROOT, "BecquerelMonitor", "schemedb.sqlite"),
    "nucdb": os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite"),
}
ENSDF_DIR = r"C:\LSRM\NuclideMaster\TCCFCALC\LIB\ENSDF2"

#: Строка таблицы: `| <база>.<таблица> | <что> | <число> |`. Число печатается
#: с неразрывными пробелами в разрядах — их сторож снимает сам.
ROW = re.compile(
    r"^\|\s*`(schemedb|nucdb)\.([a-z_0-9]+)`\s*\|([^|]*)\|"
    r"\s*\*{0,2}([0-9\u00a0\u202f ]+)\*{0,2}\s*\|\s*$")
#
# \u26a0 \u0425\u0432\u043e\u0441\u0442 `\|\s*$` \u2014 \u043d\u0435 \u0443\u043a\u0440\u0430\u0448\u0435\u043d\u0438\u0435. \u0411\u0435\u0437 \u043d\u0435\u0433\u043e \u043e\u0431\u0440\u0430\u0437\u0435\u0446 \u043b\u043e\u0432\u0438\u043b \u0441\u0442\u0440\u043e\u043a\u0443 \u0442\u0430\u0431\u043b\u0438\u0446\u044b \u00a70
# (\u00ab`schemedb.sqlite` | \u0421\u0425\u0415\u041c\u042b\u2026 | 6 | 26.7 \u041c\u0411 | \u2026\u00bb), \u0433\u0434\u0435 \u0442\u0440\u0435\u0442\u044c\u044f \u0433\u0440\u0430\u0444\u0430 \u2014 \u0447\u0438\u0441\u043b\u043e
# \u0442\u0430\u0431\u043b\u0438\u0446 \u0424\u0410\u0419\u041b\u0410, \u0438 \u0441\u0442\u043e\u0440\u043e\u0436 \u043f\u0430\u0434\u0430\u043b \u0437\u0430\u043f\u0440\u043e\u0441\u043e\u043c `select count(*) from sqlite`.
# \u041f\u043e\u0439\u043c\u0430\u043d\u043e \u043f\u0435\u0440\u0432\u044b\u043c \u0436\u0435 \u043f\u0440\u043e\u0433\u043e\u043d\u043e\u043c 10.09.2026.

#: Особые счётчики: имя в третьей графе -> запрос. Заведены потому, что не
#: всякий факт есть число строк таблицы: «доля совпадения больше единицы» —
#: это подсчёт по условию, и он-то и есть самый ломкий.
SPECIAL = {
    u"пар с долей > 1":
        ("nucdb", "select count(*) from gamma_coincidence where fraction_ppm > 1000000"),
    u"родителей с долей > 1":
        ("nucdb", "select count(distinct parent_id) from gamma_coincidence"
                  " where fraction_ppm > 1000000"),
    u"гамм с полным коэффициентом конверсии":
        ("schemedb", "select count(*) from ensdf_gammas where conv_coef is not null"),
    u"гамм с мультипольностью":
        ("schemedb", "select count(*) from ensdf_gammas where multipolarity is not null"
                     " and multipolarity <> ''"),
    u"гамм без конечного уровня":
        ("schemedb", "select count(*) from ensdf_gammas where to_level_seq is null"),
    u"разных дочерних нуклидов":
        ("schemedb", "select count(distinct nucid) from ensdf_datasets"),
    u"наборов с номером уровня родителя":
        ("schemedb", "select count(*) from ensdf_datasets where parent_l_seqno is not null"),
}


def digits(text):
    return int(re.sub(r"[^0-9]", "", text))


def read_rows(doc_path):
    u"""Строки таблицы чисел из документа: (база, таблица, что, число, строка)."""
    with io.open(doc_path, encoding="utf-8") as f:
        lines = f.read().split("\n")
    out = []
    for n, line in enumerate(lines, 1):
        m = ROW.match(line.strip())
        if m:
            out.append((m.group(1), m.group(2), m.group(3).strip(), digits(m.group(4)), n))
    return out


def check(doc_path, dbs):
    rows = read_rows(doc_path)
    if not rows:
        sys.stderr.write(u"⛔ в %s нет ни одной строки таблицы чисел — "
                         u"судить не по чему\n" % doc_path)
        return 2
    conn = {}
    for name, path in dbs.items():
        if not os.path.isfile(path):
            sys.stderr.write(u"⛔ нет базы %s\n" % path)
            return 2
        conn[name] = sqlite3.connect("file:%s?mode=ro" % path.replace("\\", "/"), uri=True)

    bad = 0
    for db_name, table, what, want, line_no in rows:
        key = what.strip().strip("*").strip()
        if key in SPECIAL:
            src, query = SPECIAL[key]
        else:
            src, query = db_name, "select count(*) from " + table
        try:
            have = conn[src].execute(query).fetchone()[0]
        except sqlite3.Error as error:
            print(u"  ⛔ %s.%s (%s), строка %d: запрос не прошёл — %s"
                  % (db_name, table, key, line_no, error))
            bad += 1
            continue
        mark = u"✓" if have == want else u"⛔"
        if have != want:
            bad += 1
        print(u"  %s %s.%-24s %-40s документ %8d, в базе %8d"
              % (mark, db_name, table, key, want, have))

    for c in conn.values():
        c.close()
    if bad:
        print(u"⛔ ОСТАНОВ: расхождений %d из %d. Число документа — это число "
              u"СТРОК ТАБЛИЦЫ, а не разобранных записей поставки (`W17`)." % (bad, len(rows)))
        return 1
    print(u"✓ все %d чисел §7/§8 сходятся с базами" % len(rows))
    return 0


# ---------------------------------------------------------------- перепись --

#: Типы записей ENSDF, которые разбирает `import_ensdf.py`.
PARSED_KINDS = "LGBEAP"


def census(ensdf_dir):
    u"""Перепись поставки: что читается, что нет (`W8`). Ничего не судит."""
    if not os.path.isdir(ensdf_dir):
        sys.stderr.write(u"⛔ нет каталога поставки %s\n" % ensdf_dir)
        return 2
    total = headers = 0
    parsed = {}
    unparsed_kind = {}
    cont_data = cont_comment = 0
    groups = {u"пооболочечные ICC (KC/LC/MC/…)": 0,
              u"средняя энергия бета/захвата (EAV)": 0,
              u"доли захвата по оболочкам (CK/CL/CM)": 0,
              u"пометки записей (FLAG)": 0,
              u"прочее продолжение": 0}
    shell = re.compile(r"\b(KC|LC|MC|NC|OC|PC|L1C|L2C|L3C|M1C|M2C|M3C|M4C|M5C)\+?\s*=")
    files = 0
    for name in sorted(os.listdir(ensdf_dir)):
        if not name.upper().endswith(".ENX"):
            continue
        files += 1
        with io.open(os.path.join(ensdf_dir, name), encoding="latin-1") as f:
            for raw in f.read().split("\n"):
                line = raw.rstrip("\r")
                if len(line) < 8:
                    continue
                total += 1
                c6, c7, c8 = line[5], line[6], line[7]
                if len(line) > 9 and line[8] == "*" and c6 == " " and c8 == " ":
                    headers += 1
                    continue
                if c7 != " ":
                    if c6 != " ":
                        cont_comment += 1
                    else:
                        unparsed_kind[u"комментарий (кол. 7)"] = \
                            unparsed_kind.get(u"комментарий (кол. 7)", 0) + 1
                    continue
                if c8 == "I":
                    # ⚠ НЕ запись продолжения, хотя выглядит ею: у указателя
                    # ЛСРМ в колонках 1–6 стоят массовое число и Z (`  1  1 I`),
                    # и колонка 6 непуста по этой причине. Стандарт ENSDF типа
                    # `I` не знает вовсе — это добавка поставки, оглавление
                    # файла массовой цепочки. Данных в ней нет.
                    unparsed_kind[u"указатель ЛСРМ (тип 'I')"] = \
                        unparsed_kind.get(u"указатель ЛСРМ (тип 'I')", 0) + 1
                    continue
                if c6 != " ":
                    cont_data += 1
                    body = line[9:] if len(line) > 9 else ""
                    if shell.search(body):
                        groups[u"пооболочечные ICC (KC/LC/MC/…)"] += 1
                    elif "EAV=" in body:
                        groups[u"средняя энергия бета/захвата (EAV)"] += 1
                    elif re.search(r"\bC[KLM]\+?\s*=", body):
                        groups[u"доли захвата по оболочкам (CK/CL/CM)"] += 1
                    elif "FLAG=" in body:
                        groups[u"пометки записей (FLAG)"] += 1
                    else:
                        groups[u"прочее продолжение"] += 1
                    continue
                if c8 in PARSED_KINDS:
                    parsed[c8] = parsed.get(c8, 0) + 1
                else:
                    k = u"запись типа '%s'" % (c8 if c8 != " " else u"·")
                    unparsed_kind[k] = unparsed_kind.get(k, 0) + 1

    n_parsed = sum(parsed.values()) + headers
    n_cont = cont_data + cont_comment
    n_unparsed = sum(unparsed_kind.values())
    print(u"поставка ENSDF2: %d файлов, записей (длиной ≥ 8) %d" % (files, total))
    print(u"\nЧИТАЕТСЯ (%d, %.1f %%):" % (n_parsed, 100.0 * n_parsed / total))
    print(u"   заголовки наборов                       %7d" % headers)
    for k in PARSED_KINDS:
        if k in parsed:
            print(u"   записи типа '%s'                         %7d" % (k, parsed[k]))
    print(u"\nНЕ ЧИТАЕТСЯ ВОВСЕ (%d, %.1f %%):"
          % (n_cont + n_unparsed, 100.0 * (n_cont + n_unparsed) / total))
    print(u"   записи продолжения СОДЕРЖАТЕЛЬНЫЕ       %7d" % cont_data)
    for k in sorted(groups, key=lambda x: -groups[x]):
        print(u"      из них %-36s %7d" % (k, groups[k]))
    print(u"   продолжения-комментарии                 %7d" % cont_comment)
    for k in sorted(unparsed_kind, key=lambda x: -unparsed_kind[x]):
        print(u"   %-40s %7d" % (k, unparsed_kind[k]))
    return 0


def main():
    doc = DOC
    dbs = dict(DBS)
    do_census = None
    for a in sys.argv[1:]:
        if a.startswith("--doc="):
            doc = a[6:]
        elif a.startswith("--scheme-db="):
            dbs["schemedb"] = a[12:]
        elif a.startswith("--nuc-db="):
            dbs["nucdb"] = a[9:]
        elif a == "--census":
            do_census = ENSDF_DIR
        elif a.startswith("--census="):
            do_census = a[9:]
        else:
            sys.exit("неизвестный ключ %s" % a)
    if do_census is not None:
        return census(do_census)
    if not os.path.isfile(doc):
        sys.stderr.write(u"⛔ нет документа %s\n" % doc)
        return 2
    return check(doc, dbs)


if __name__ == "__main__":
    sys.exit(main())
