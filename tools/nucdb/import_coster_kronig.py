# -*- coding: utf-8 -*-
"""Втянуть переходы Костера—Кронига f12/f13/f23 из поставки xraylib в `matdb`
(таблица `coster_kronig`) — читатель `MaterialDatabase.Fluorescence.CkSupply`,
уровень 2 ключа `EfficiencySimulator.LYieldSupply` (`M9`, решение Amber
12.09.2026 «ω_L из fluorescence_yield + f13 в СЛЕДУЮЩИЙ единый счёт склада»).

⛔ БАЗУ ПИШЕТ ТОЛЬКО AMBER (приказ 09.08.2026). Без `--apply` скрипт открывает
базу на чтение, собирает строки из поставки и печатает, что БЫЛО БЫ записано,
— вместе с расхождением против наших EADL (`eadl_auger`), чтобы решение
принималось по числам. С `--apply` пишет таблицу целиком (drop + create):
запись, которая должна пережить пересборку, идёт импортёром, а не разовым
UPDATE.

Источник: `xraylib_coskron.dat` (поставка `C:\\Users\\moroz\\source\\repos\\_supply_omega\\`,
загрузка 09.08.2026; Krause-1979 с блоками ЗАМЕН). Разбор — ТОТ ЖЕ, что у меры
`tools/nucdb/compare_coster_kronig.py`: строки «Z ключ значение», ключи
F12/F13/F23, всё прочее (F1, FP13, блок FM*) мимо; один Z встречается до трёх
раз, побеждает ПОСЛЕДНЕЕ вхождение — так читает сам xraylib. У xraylib нет f23
ниже Z = 29 (переход L2→L3 там закрыт) и f13 у Z = 98 — строки не заводятся,
читатель отдаёт ноль.

  python tools/nucdb/import_coster_kronig.py <matdb.sqlite> <xraylib_coskron.dat> [--apply]

Схема: `coster_kronig (z integer, transition text 'f12'|'f13'|'f23',
probability real, source text 'xraylib', primary key (z, transition, source))`
— источник назван в каждой строке, как у `fluorescence_yield` (правило
«каждому своё», database/scheme.md §0а).
"""
import io
import os
import sqlite3
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

SCHEMA = """
drop table if exists coster_kronig;
-- Переходы Костера—Кронига между подоболочками L: вероятность того, что дырка
-- на L1 переедет на L2 (f12) или L3 (f13), а с L2 — на L3 (f23). Источник
-- назван в каждой строке: наши EADL (`eadl_auger`) расходятся с поставкой
-- систематикой (f13 завышен: медиана 1.12, W 1.88 — сверка 24.08.2026).
create table coster_kronig (
    z           integer not null,
    transition  text    not null,   -- 'f12' | 'f13' | 'f23'
    probability real    not null,   -- 0..1
    source      text    not null,   -- 'xraylib'
    primary key (z, transition, source)
);
"""

KEYS = {"F12": "f12", "F13": "f13", "F23": "f23"}
L1, L2, L3 = 3, 5, 6
CK = (("f12", L1, L2), ("f13", L1, L3), ("f23", L2, L3))


def read_xraylib(path):
    """(Z, переход) → значение; последнее вхождение побеждает."""
    values, hits = {}, {}
    for line in io.open(path, encoding="utf-8", newline=""):
        p = line.split()
        if len(p) != 3 or p[1] not in KEYS:
            continue
        key = (int(p[0]), KEYS[p[1]])
        values[key] = float(p[2])
        hits[key] = hits.get(key, 0) + 1
    return values, hits


def main():
    args = [a for a in sys.argv[1:] if a != "--apply"]
    apply = "--apply" in sys.argv
    if len(args) != 2:
        sys.exit("usage: import_coster_kronig.py <matdb.sqlite> <xraylib_coskron.dat> [--apply]")
    db_path, coskron_path = args
    for path in (db_path, coskron_path):
        if not os.path.exists(path):
            sys.exit("нет файла: %s" % path)

    values, hits = read_xraylib(coskron_path)
    rows = [(z, lbl, v, "xraylib") for (z, lbl), v in sorted(values.items())
            if 0.0 <= v <= 1.0]
    if not rows:
        sys.exit("поставка дала ноль строк — проверьте путь")

    if apply:
        db = sqlite3.connect(db_path)
        db.executescript(SCHEMA)
        db.executemany("insert or replace into coster_kronig (z, transition, probability, source)"
                       " values (?, ?, ?, ?)", rows)
        db.commit()
    else:
        db = sqlite3.connect("file:%s?mode=ro" % db_path.replace("\\", "/"), uri=True)

    ours = {}
    for lbl, vac, frm in CK:
        for z, s in db.execute("select z, sum(probability) from eadl_auger"
                               " where vacancy_shell=? and from_shell=? group by z", (vac, frm)):
            ours[(z, lbl)] = s

    print("%s строк: %d (переопределено поздними блоками поставки: %d)"
          % ("ЗАНЕСЕНО в coster_kronig" if apply else "СОБРАНО (не записано, нет --apply)",
             len(rows), sum(1 for k, n in hits.items() if n > 1)))
    for lbl, _, _ in CK:
        zs = sorted(z for z, l, v, s in rows if l == lbl)
        print("  %s: %d элементов, Z %d…%d" % (lbl, len(zs), min(zs), max(zs)))

    print("\nпоставка против наших EADL (eadl_auger) на веществах сцен:")
    print("  Z  эл  переход  xraylib   EADL     EADL/xraylib")
    for z, name in ((26, "Fe"), (29, "Cu"), (53, "I"), (55, "Cs"), (74, "W"), (82, "Pb"), (83, "Bi")):
        for lbl, _, _ in CK:
            v = values.get((z, lbl))
            o = ours.get((z, lbl))
            if v is None:
                print("  %2d  %-3s %-4s     —        %s" % (z, name, lbl, "%.5f" % o if o else "—"))
            else:
                print("  %2d  %-3s %-4s   %.5f   %s   %s"
                      % (z, name, lbl, v, "%.5f" % o if o else "   —   ",
                         "%.4f" % (o / v) if o and v > 0 else "—"))

    if apply:
        n = db.execute("select count(*) from coster_kronig").fetchone()[0]
        print("\nв базе теперь coster_kronig: %d строк" % n)
    db.close()


if __name__ == "__main__":
    main()
