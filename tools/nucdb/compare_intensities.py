# -*- coding: utf-8 -*-
u"""D5: сверка интенсивностей `ensdf_gammas` с выходами `decay_radiations`.

Две независимые поставки разного года несут интенсивности одних и тех же
гамма-линий. До 08.08.2026 расхождения между ними не были измерены ни разу —
это была одна из шести пар «копия величины без сверки» (D23).

СРАВНИВАЕТСЯ ФОРМА, А НЕ ВЕЛИЧИНА. Нормировки разные: у ENSDF `intensity` —
относительная фотонная интенсивность внутри набора (сильнейшая линия обычно
100), у `decay_radiations` — выход в процентах НА РАСПАД. Поэтому для каждой
общей линии берётся отношение `ensdf / radiations`, и проверяется, одно ли оно
у всех линий родителя: согласие формы означает нулевой разброс этого отношения.

Якоря нет НАРОЧНО. Первый заход приводил обе шкалы к сильнейшей общей линии и
намерил хвост в миллионы процентов — мерил он при этом сам якорь: стоит
якорной линии разойтись у двух поставок, и все остальные линии родителя
получают её ошибку. Отношение к медиане от выбора якоря не зависит вовсе.

ИСКЛЮЧАЮТСЯ РОДИТЕЛИ С ИЗОМЕРАМИ. Берутся только те, у кого в
`decay_radiations` один уровень родителя И в `ensdf_datasets` один набор. Иначе
сравниваются линии РАЗНЫХ состояний родителя — у `105AG` их три (Ag-105 и
Ag-105m), и без отсева это ловилось как расхождение поставок. Это тот же W9:
набор определяется родителем и его периодом, а не именем дочки.

    python compare_intensities.py [--nucdb ...] [--schemedb ...] [--worst N]
"""
import argparse
import collections
import io
import os
import sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_NUCDB = os.path.join(ROOT, "BecquerelMonitor", "nucdb.sqlite")
DEFAULT_SCHEMEDB = os.path.join(ROOT, "BecquerelMonitor", "schemedb.sqlite")

MATCH_KEV = 0.5
MIN_LINES = 3


def median(values):
    values = sorted(values)
    return values[len(values) // 2]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--nucdb", default=DEFAULT_NUCDB)
    p.add_argument("--schemedb", default=DEFAULT_SCHEMEDB)
    p.add_argument("--worst", type=int, default=10)
    a = p.parse_args()

    nuc = sqlite3.connect(a.nucdb)
    scheme = sqlite3.connect(a.schemedb)
    out = io.open(1, "w", encoding="utf-8", closefd=False)

    levels = collections.defaultdict(set)
    for parent, seq in nuc.execute(
            "select distinct parent_nucid, parent_l_seqno from decay_radiations"):
        levels[parent].add(seq)
    datasets = collections.Counter(
        r[0] for r in scheme.execute(
            "select parent_nucid from ensdf_datasets where parent_nucid is not null"))

    rad = collections.defaultdict(list)
    for parent, e, i in nuc.execute(
            "select parent_nucid, energy_num, intensity_num from decay_radiations"
            " where type_a='G' and energy_num is not null"
            " and intensity_num is not null"):
        if i > 0.0 and len(levels.get(parent, ())) == 1 and datasets.get(parent) == 1:
            rad[parent].append((e, i))

    parents = lines = 0
    buckets = collections.Counter()
    worst = []
    for ds_id, parent in scheme.execute(
            "select id, parent_nucid from ensdf_datasets"
            " where parent_nucid is not null"):
        table = rad.get(parent)
        if not table:
            continue
        ratios = []
        for e, i in scheme.execute(
                "select energy_kev, intensity from ensdf_gammas where dataset_id=?"
                " and energy_kev is not null and intensity is not null", (ds_id,)):
            if not (i > 0.0):
                continue
            near = sorted((abs(e - re), ri) for re, ri in table
                          if abs(e - re) <= MATCH_KEV)
            if near:
                ratios.append((e, i / near[0][1]))
        if len(ratios) < MIN_LINES:
            continue

        scale = median([r for _, r in ratios])
        if not (scale > 0.0):
            continue
        parents += 1
        for e, r in ratios:
            dev = abs(100.0 * (r / scale - 1.0))
            lines += 1
            buckets[u"≤1 %" if dev <= 1 else u"≤5 %" if dev <= 5 else
                    u"≤20 %" if dev <= 20 else u"> 20 %"] += 1
            worst.append((dev, parent, e))

    out.write(u"# Сверка интенсивностей: ENSDF против decay_radiations (D5)\n\n")
    out.write(u"родителей без изомерной неоднозначности и с %d+ общими линиями: %d\n"
              % (MIN_LINES, parents))
    out.write(u"линий сверено: %d\n\n" % lines)
    for key in (u"≤1 %", u"≤5 %", u"≤20 %", u"> 20 %"):
        n = buckets[key]
        out.write(u"  отклонение от формы %-7s %6d  %5.1f %%\n"
                  % (key, n, 100.0 * n / max(1, lines)))

    worst.sort(reverse=True)
    if worst:
        allk = [d for d, _, _ in worst]
        out.write(u"\nмедиана |отклонения| %.3f %%, 90-й процентиль %.2f %%\n"
                  % (median(allk), sorted(allk)[int(0.9 * len(allk))]))
        out.write(u"\nхудшие %d:\n" % a.worst)
        for dev, parent, e in worst[:a.worst]:
            out.write(u"  %-9s %9.2f кэВ  %+.0f %%\n" % (parent, e, dev))
    out.flush()


if __name__ == "__main__":
    main()
