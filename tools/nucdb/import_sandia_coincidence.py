# -*- coding: utf-8 -*-
"""
Втягивает в nucdb.sqlite гамма-совпадения из `sandia.decay.xml` — файла
библиотеки распада SandiaDecay, что едет с InterSpec.

Зачем. Для поправки на каскадное суммирование нужно знать, какие две гаммы
вылетают вместе и с какой вероятностью. У нас есть схемы распада ENSDF
(`ensdf_levels`, `ensdf_gammas`), но к конечному уровню там привязано лишь
78.3 % переходов, а по 5038 гаммам привязки нет вовсе — каскад по ним не
собрать. У Sandia связи уже посчитаны и обкатаны в чужом приложении.

Формат. Внутри `<transition>` каждая гамма может нести детей
`<coincidentGamma id intensity>`; `id` указывает на другую гамму ТОГО ЖЕ
перехода:

    <transition branchRatio="1" child="Ni60" mode="b-" parent="Co60">
        <gamma energy="1173.228" intensity="0.9985">
            <coincidentGamma id="3" intensity="0.99987"/>
        </gamma>
        <gamma energy="1332.492" id="3" intensity="0.999826"/>
    </transition>

Три вещи, которые надо знать, прежде чем этим пользоваться:

1. **Доля условна по той линии, которая её несёт.** Запись выше читается как
   P(1332.492 | 1173.228) = 0.99987. Обратной записи в файле НЕТ: пара хранится
   один раз (проверено — встречных пар ноль из 334 382). Обратная условная
   считается сама: P(A|B) = P(B|A)·I_A/I_B, поэтому оба выхода сложены в ту же
   строку и таблица самодостаточна.

2. **Интенсивность внутри перехода, а не на распад.** Умножаем на
   `branchRatio` перехода и складываем по переходам родителя — тогда получается
   процент на распад, как в `decay_radiations`.

3. **Метастабильные у Sandia — отдельные нуклиды, и с нашими они не сходятся.**
   У Cs-137 у них нет ни одной значимой гаммы: 661.657 принадлежит Ba-137m,
   который стоит следом в цепочке. У нас всё сложено на 137CS, а под 137BA в
   `decay_radiations` нет вообще ни строки. Наш `l_seqno` — это НОМЕР УРОВНЯ
   в схеме NuclideMaster, а не порядковый номер изомера: значения доходят до
   200, и порядковый номер Sandia лежит отдельным полем `isomer`.

   ⚠ **Вывод «ни один из 418 изомеров не сошёлся» был НЕВЕРЕН (D11,
   09.08.2026).** Сверка делалась по БАЗОВОМУ имени (`108AG`), а изомер лежит
   в `nuclides` отдельной строкой С СУФФИКСОМ — `108AGm`, `234PAm1`. По
   полному имени находятся 236 изомеров из 418, и номер берётся у них
   ГОТОВЫМ, а не вычисляется. Остальные 182 — нуклиды, которых у нас нет
   вовсе; им `l_seqno` остаётся `null`, потому что нашей нумерации для этого
   состояния не существует, а придумывать её нельзя: на ней держится вся
   привязка линий.

Отсечка и укладка. Без отсечки пар 334 382 и база пухнет на 20 МБ. Берём пары,
где обе линии дают не меньше 0.1 % на распад, а доля совпадения не меньше 0.1 %:
этого хватает для поправки по всем практически значимым линиям. Дальше числа
уложены целыми (энергия в тысячных кэВ, доли в миллионных) и выходы линий
вынесены в отдельную таблицу — иначе `REAL` по восемь байт на поле раздувает
128 429 строк до +9.5 МБ вместо +3.5 МБ. Округление энергии до 0.001 кэВ
задевает 124 линии из 109 743 и на 0.0005 кэВ — против лучшей ПШПВ германия в
полкэВ это ничто. Читать полагается через представления `v_gamma_coincidence` и
`v_gamma_coincidence_line`, где кэВ и проценты возвращены на место.

Запуск:

    python tools/nucdb/import_sandia_coincidence.py BecquerelMonitor/nucdb.sqlite \
        "<InterSpec>/data/sandia.decay.xml"

Источник — InterSpec / SandiaDecay, Sandia National Laboratories (NTESS),
LGPL v2.1; разбор поставки — `tools/interspec/README.md`.
"""

import os
import re
import sqlite3
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

NS = "{sandia.decay.xsd}"

#: Минимальный выход линии, % на распад родителя.
MIN_INTENSITY = 0.1
#: Минимальная доля совпадения.
MIN_FRACTION = 0.001

ELEMENT = re.compile(r"^([A-Za-z]+)(\d+)(m\d*)?$")


def mkev(energy):
    """Энергия в тысячных кэВ."""
    return int(round(energy * 1000.0))


def ppm(fraction):
    """Доля в миллионных."""
    return int(round(fraction * 1.0e6))


def to_nucid(symbol):
    """`Ba137m` -> `('137BA', 1, '137BAm')`; `Co60` -> `('60CO', 0, '60CO')`.

    Второе — порядковый номер изомера У SANDIA, а не наш `l_seqno`; см. шапку.
    Третье — наш `nucid` ВМЕСТЕ С СУФФИКСОМ состояния: изомер лежит в
    `nuclides` отдельной строкой (`108AGm`, `234PAm1`), и искать его по
    базовому имени бессмысленно — на этом и споткнулась первая версия (D11).
    Возвращает `(None, None, None)`, если символ не разбирается (нейтрон `n1`
    и прочая экзотика).
    """
    m = ELEMENT.match(symbol or "")
    if not m:
        return None, None, None
    el, mass, iso = m.group(1), m.group(2), m.group(3)
    isomer = 0
    if iso:
        isomer = int(iso[1:]) if len(iso) > 1 else 1
    base = "%s%s" % (mass, el.upper())
    return base, isomer, base + (iso or "")


def collect(xml_path):
    """Собирает пары совпадений по всем переходам, приводя выходы к % на распад.

    Возвращает `{символ Sandia: {(E1, E2): [доля, I1, I2]}}`. Складывание по
    переходам одного родителя нужно потому, что одна и та же линия может идти
    из нескольких ветвей распада.
    """
    root = ET.parse(xml_path).getroot()

    lines = defaultdict(lambda: defaultdict(float))   # символ -> энергия -> I, %
    pairs = defaultdict(dict)                         # символ -> (E1,E2) -> доля

    for tr in root.iter(NS + "transition"):
        parent = tr.get("parent")
        try:
            br = float(tr.get("branchRatio") or 0.0)
        except ValueError:
            continue
        if not parent or br <= 0.0:
            continue

        gammas = [g for g in tr if g.tag == NS + "gamma"]
        if not gammas:
            continue

        by_id = {g.get("id"): g for g in gammas if g.get("id")}

        for g in gammas:
            energy = float(g.get("energy"))
            lines[parent][energy] += 100.0 * float(g.get("intensity")) * br

        for g in gammas:
            energy = float(g.get("energy"))
            for c in g:
                if c.tag != NS + "coincidentGamma":
                    continue
                other = by_id.get(c.get("id"))
                if other is None:
                    continue
                key = (energy, float(other.get("energy")))
                fraction = float(c.get("intensity"))
                # У одного родителя пара может встретиться в двух ветвях —
                # берём наибольшую долю, складывать вероятности нельзя.
                if fraction > pairs[parent].get(key, 0.0):
                    pairs[parent][key] = fraction

    return lines, pairs


def main():
    if len(sys.argv) != 3:
        sys.exit("usage: import_sandia_coincidence.py <nucdb.sqlite> <sandia.decay.xml>")

    db_path, xml_path = sys.argv[1], sys.argv[2]
    if not os.path.isfile(xml_path):
        sys.exit("нет файла %s" % xml_path)

    lines, pairs = collect(xml_path)

    db = sqlite3.connect(db_path)
    db.executescript("""
        drop view  if exists v_gamma_coincidence;
        drop view  if exists v_gamma_coincidence_line;
        drop table if exists gamma_coincidence;
        drop table if exists gamma_coincidence_line;
        drop table if exists gamma_coincidence_parent;

        create table gamma_coincidence_parent (
            id            integer primary key,
            sandia_symbol text not null,   -- как зовут родителя у Sandia: Co60, Ba137m
            nucid         text,            -- 60CO, 137BA (null, если символ не разобран)
            isomer        integer,         -- порядковый номер изомера У SANDIA: 0, 1, 2...
            l_seqno       integer          -- наш номер уровня; заполнен только у isomer = 0
        );

        -- Выходы линий, участвующих в совпадениях. Вынесены сюда, чтобы не
        -- дублировать их в каждой паре: 43 231 строка против 256 858 значений.
        create table gamma_coincidence_line (
            parent_id     integer not null,
            energy_mkev   integer not null,  -- энергия в тысячных кэВ
            intensity_ppm integer not null   -- выход, миллионных доли на распад родителя
        );

        -- P(coinc_energy | energy) = fraction. Обратная условная считается сама:
        --   P(energy | coinc_energy) = fraction * I(energy) / I(coinc_energy),
        -- выходы обеих линий лежат в gamma_coincidence_line.
        create table gamma_coincidence (
            parent_id         integer not null,
            energy_mkev       integer not null,
            coinc_energy_mkev integer not null,
            fraction_ppm      integer not null  -- доля совпадения, миллионных
        );

        create view v_gamma_coincidence as
            select p.nucid, p.isomer, p.l_seqno, p.sandia_symbol,
                   c.energy_mkev/1000.0       as energy_kev,
                   c.coinc_energy_mkev/1000.0 as coinc_energy_kev,
                   c.fraction_ppm/1.0e6       as fraction
              from gamma_coincidence c
              join gamma_coincidence_parent p on p.id = c.parent_id;

        create view v_gamma_coincidence_line as
            select p.nucid, p.isomer, p.l_seqno, p.sandia_symbol,
                   l.energy_mkev/1000.0     as energy_kev,
                   l.intensity_ppm/1.0e4    as intensity_pct
              from gamma_coincidence_line l
              join gamma_coincidence_parent p on p.id = l.parent_id;
    """)

    n_pairs = n_lines = n_parents = n_unmapped = 0
    n_isomer_linked = n_isomer_unlinked = 0
    dropped = 0
    parent_id = 0
    # Наши номера уровней по ПОЛНОМУ имени состояния — читаются один раз.
    our_levels = {n: s for n, s in db.execute(
        "select nucid, l_seqno from nuclides where l_seqno is not null")}
    for symbol in sorted(pairs):
        kept = []
        for (e1, e2), fraction in pairs[symbol].items():
            i1 = lines[symbol].get(e1, 0.0)
            i2 = lines[symbol].get(e2, 0.0)
            if (fraction < MIN_FRACTION
                    or i1 < MIN_INTENSITY or i2 < MIN_INTENSITY):
                dropped += 1
                continue
            kept.append((e1, e2, fraction, i1, i2))
        if not kept:
            continue

        parent_id += 1
        n_parents += 1
        nucid, isomer, state_nucid = to_nucid(symbol)
        if nucid is None:
            n_unmapped += 1
        # `l_seqno` у основного состояния — ноль; у изомера берётся ГОТОВЫМ из
        # `nuclides` по имени С СУФФИКСОМ (`108AGm`), а не вычисляется. Если
        # такой строки у нас нет, остаётся null: нашей нумерации для этого
        # состояния не существует, и придумывать её нельзя (D11).
        if isomer == 0:
            l_seqno = 0
        else:
            l_seqno = our_levels.get(state_nucid)
            if l_seqno is None:
                n_isomer_unlinked += 1
            else:
                n_isomer_linked += 1
        db.execute("insert into gamma_coincidence_parent values (?,?,?,?,?)",
                   (parent_id, symbol, nucid, isomer, l_seqno))
        db.executemany("insert into gamma_coincidence values (?,?,?,?)",
                       [(parent_id, mkev(e1), mkev(e2), ppm(f))
                        for e1, e2, f, _, _ in sorted(kept)])
        n_pairs += len(kept)

        involved = {}
        for e1, e2, _, i1, i2 in kept:
            involved[e1] = i1
            involved[e2] = i2
        db.executemany("insert into gamma_coincidence_line values (?,?,?)",
                       [(parent_id, mkev(e), ppm(0.01 * i))
                        for e, i in sorted(involved.items())])
        n_lines += len(involved)

    # Указатели: на родителях (их 1643, он ничего не стоит) и на parent_id
    # обеих больших таблиц.
    #
    # Прежде на самих парах указателя нарочно не было — довод «весит два
    # мегабайта, а таблицы всё равно грузятся в память целиком» оказался
    # НЕВЕРЕН: `FsaCascadeSummer` спрашивает их через представления с фильтром
    # по нуклиду, план запроса был `SCAN` — полный проход на КАЖДЫЙ нуклид
    # библиотеки, 9.2 мс на пары и 2.9 мс на линии, ≈0.36 с на библиотеке из
    # тридцати. С указателем план стал `SEARCH` (T22, 08.08.2026); цена — 1.6 МБ
    # на базу.
    db.execute("create index ix_gamma_coincidence_parent"
               " on gamma_coincidence_parent(nucid, l_seqno)")
    db.execute("create index ix_gamma_coincidence_parent_id"
               " on gamma_coincidence(parent_id)")
    db.execute("create index ix_gamma_coincidence_line_parent_id"
               " on gamma_coincidence_line(parent_id)")
    db.commit()

    # Сколько родителей нашлось в нашей ядерной части — это не проверка данных,
    # а мера того, насколько таблицей вообще можно пользоваться через nucid.
    known = db.execute(
        "select count(*) from gamma_coincidence_parent p"
        " where p.isomer = 0 and exists (select 1 from nuclides n"
        "               where n.nucid = p.nucid and n.l_seqno = 0)"
    ).fetchone()[0]
    ground = db.execute("select count(*) from gamma_coincidence_parent"
                        " where isomer = 0").fetchone()[0]

    db.execute("vacuum")
    db.close()

    print("совпадения: %d пар и %d линий у %d родителей (отброшено по отсечке %d)"
          % (n_pairs, n_lines, n_parents, dropped))
    print("основных состояний %d, из них есть в nuclides %d; изомеров %d"
          " — привязано к нашей нумерации %d, нуклида нет у нас у %d;"
          " символ не разобран у %d"
          % (ground, known, n_parents - ground, n_isomer_linked,
             n_isomer_unlinked, n_unmapped))
    print("размер базы: %.2f МБ" % (os.path.getsize(db_path) / 1e6))


if __name__ == "__main__":
    main()
