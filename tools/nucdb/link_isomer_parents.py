# -*- coding: utf-8 -*-
u"""D11: привязать изомеров-родителей совпадений к нашей нумерации уровней.

У 418 родителей `gamma_coincidence_parent` поле `l_seqno` пусто: это изомеры, и
искать их приходилось по `sandia_symbol` («Ag108m»), потому что имя состояния
поставка Sandia несёт, а номер уровня — нет.

ПРИВЯЗКА ПРЯМАЯ, БЕЗ ДОГАДОК. «Наша нумерация» — это `nuclides.l_seqno`, и у
изомера, который в `nuclides` есть, номер уже проставлен. Значит вся работа —
перевести имя Sandia в наш `nucid` («Ag108m» → «108AGm») и взять готовое
значение. Ничего не вычисляется.

Первый заход делался иначе и был неправ: номер выводился из
`G4ENSDFSTATE3.0` (энергия изомерного состояния) и сводился с `g4_level`. Это
ДРУГАЯ нумерация — сплошная по схеме уровней Geant4, а наша идёт по своему
списку, — и сверка на известных метастабильных дала 76.4 %, то есть почти то
же, что уже отвергнутое в D20 правило «первый долгоживущий уровень» (72 %).
Совпадение двух чисел и подсказало, что меряется не то.

ENSDFSTATE ОСТАЛСЯ — НЕЗАВИСИМОЙ ПРОВЕРКОЙ. Для тех изомеров, что нашлись,
номер сверяется со списком состояний Geant4: изомерами там считаются
возбуждённые уровни с τ выше 1 мкс (порог `G4NuclideTable`), упорядоченные по
энергии. Колонки файла расшифрованы по трём известным изомерам:

    Z   A   E, кэВ   метка   τ, НАНОСЕКУНД   2J   магнитный момент

Пятая — СРЕДНЕЕ ВРЕМЯ ЖИЗНИ в наносекундах, не период полураспада: Ag-108m
(438 лет) стоит как 1.994e19, Tc-99m (6.007 ч) как 3.120e13, Pa-234m
(1.159 мин) как 1.003e11 — все три сходятся с τ = T½/ln2 до третьей цифры.
Перепутав τ с T½, получишь связный, ровный и неверный ответ.

ЧЕГО ЭТОТ ПРОГОН НЕ ДЕЛАЕТ. Изомеры, которых в `nuclides` нет вовсе, не
привязываются: нашей нумерации для них не существует, и придумывать её —
значит заводить уровень, которого у нас нет. Они печатаются списком.

    python link_isomer_parents.py [--nucdb ...] [--schemedb ...]
                                  [--state <ENSDFSTATE.dat>] [--apply]

Без `--apply` ничего не пишется.
"""
import argparse
import collections
import io
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite")
DEFAULT_SCHEMEDB = os.path.join(ROOT, "BecquerelMonitor", "schemedb.sqlite")
DEFAULT_STATE = r"C:\Users\moroz\source\repos\GEANT4\G4ENSDFSTATE3.0\ENSDFSTATE.dat"

TAU_NS = 1.0e3               # порог изомерности: 1 мкс, как у G4NuclideTable
MATCH_KEV = 1.0              # допуск сведения энергии с g4_level
SANDIA = re.compile(r"^([A-Za-z]{1,2})(\d{1,3})(m\d?)$")


def read_states(path):
    u"""(Z, A) -> отсортированные энергии возбуждённых состояний выше порога."""
    states = collections.defaultdict(list)
    with io.open(path, encoding="latin-1") as f:
        for line in f:
            parts = line.split()
            if len(parts) < 6:
                continue
            try:
                z, a = int(parts[0]), int(parts[1])
                energy = float(parts[2])
                tau = float(parts[-3])
            except ValueError:
                continue
            if energy > 0.0 and tau > TAU_NS:
                states[(z, a)].append(energy)
    for key in states:
        states[key].sort()
    return states


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    p.add_argument("--state", default=DEFAULT_STATE)
    p.add_argument("--apply", action="store_true")
    a = p.parse_args()
    out = io.open(1, "w", encoding="utf-8", closefd=False)
    nuc = sqlite3.connect(a.nucdb)

    ours = {}
    for nucid, seq in nuc.execute("select nucid, l_seqno from nuclides"):
        ours[nucid] = seq

    rows = nuc.execute(
        "select id, sandia_symbol from gamma_coincidence_parent"
        " where isomer > 0 and l_seqno is null").fetchall()

    linked, missing, unparsed = [], [], []
    for pid, symbol in rows:
        m = SANDIA.match(symbol or "")
        if not m:
            unparsed.append(symbol)
            continue
        tag = m.group(3)
        nucid = "%s%s%s" % (m.group(2), m.group(1).upper(), tag)
        seq = ours.get(nucid)
        if seq is None:
            missing.append((symbol, nucid))
        else:
            linked.append((pid, symbol, nucid, seq))

    out.write(u"# Привязка изомеров-родителей совпадений (D11)\n\n")
    out.write(u"изомеров без привязки: %d\n" % len(rows))
    out.write(u"  нашлись в `nuclides`, номер взят готовый: %d (%.1f %%)\n"
              % (len(linked), 100.0 * len(linked) / max(1, len(rows))))
    out.write(u"  нуклида нет в `nuclides` — привязывать не к чему: %d\n" % len(missing))
    if unparsed:
        out.write(u"  имя не разбирается: %d (%s)\n"
                  % (len(unparsed), u", ".join(unparsed[:5])))

    out.write(u"\nпримеры привязанных:\n")
    for pid, symbol, nucid, seq in linked[:8]:
        out.write(u"  %-10s -> %-10s l_seqno %s\n" % (symbol, nucid, seq))
    out.write(u"\nпримеры непривязанных (нуклида нет у нас):\n")
    for symbol, nucid in missing[:8]:
        out.write(u"  %-10s искали %s\n" % (symbol, nucid))

    # --- независимая проверка по списку состояний Geant4 -----------------
    if os.path.isfile(a.state):
        states = read_states(a.state)
        scheme = sqlite3.connect(a.schemedb)
        levels = collections.defaultdict(list)
        for z, mass, seq, energy in scheme.execute(
                "select z, a, seq, energy_ev from g4_level"):
            levels[(z, mass)].append((energy / 1000.0, seq))
        for key in levels:
            levels[key].sort()
        symbols = {s.upper(): z for z, s in nuc.execute(
            "select z, symbol from nuclides where symbol is not null")}

        same = other = nodata = 0
        for pid, symbol, nucid, seq in linked:
            m = SANDIA.match(symbol)
            z = symbols.get(m.group(1).upper())
            mass = int(m.group(2))
            tag = m.group(3)
            index = 1 if tag == "m" else int(tag[1:])
            table = states.get((z, mass)) if z else None
            if not table or index > len(table):
                nodata += 1
                continue
            energy = table[index - 1]
            near = sorted((abs(e - energy), s) for e, s in levels.get((z, mass), ())
                          if abs(e - energy) <= MATCH_KEV)
            if not near:
                nodata += 1
            elif near[0][1] == seq:
                same += 1
            else:
                other += 1
        out.write(u"\nнезависимая сверка по G4ENSDFSTATE (номер уровня Geant4\n"
                  u"против нашего — НУМЕРАЦИИ РАЗНЫЕ, совпадение не обязано быть\n"
                  u"полным, важен порядок величины расхождения):\n")
        out.write(u"  совпало %d, разошлось %d, сверить нечем %d\n"
                  % (same, other, nodata))
    else:
        out.write(u"\n(файла состояний нет — независимая сверка пропущена)\n")

    if a.apply and linked:
        nuc.executemany("update gamma_coincidence_parent set l_seqno=? where id=?",
                        [(seq, pid) for pid, _, _, seq in linked])
        nuc.commit()
        left = nuc.execute("select count(*) from gamma_coincidence_parent"
                           " where isomer > 0 and l_seqno is null").fetchone()[0]
        out.write(u"\nзаписано %d; изомеров без привязки осталось %d\n"
                  % (len(linked), left))
    elif linked:
        out.write(u"\n(без --apply ничего не записано)\n")
    out.flush()


if __name__ == "__main__":
    main()
