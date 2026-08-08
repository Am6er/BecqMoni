# -*- coding: utf-8 -*-
u"""D24: сверка двух моделей фотоэффекта — XCOM против EPICS2017.

В базе фотопоглощение лежит дважды и независимо: `xcom_cross_sections`
(полное сечение канала, 9136 точек на 100 элементов) и `epics_photo_subshell`
(пооболочечное, 369 977 точек). Программа читает ОБЕ — полное сечение берётся
из XCOM, доля K-оболочки из EPICS (правило старшинства, `scheme.md` §0а).

Аудит записал, что сверить их поточечно нельзя: сетки не пересекаются ни в
одной энергии (у иода 168 узлов против 4013, общих нет). Это верно и не
означает, что сверить нельзя вовсе — сравнивать надо ИНТЕРПОЛЯЦИЕЙ, и именно
она не делалась.

КАК СРАВНИВАЕТСЯ. Пооболочечные сечения EPICS складываются по всем оболочкам в
полное фотопоглощение, дальше значение XCOM в каждом его узле сопоставляется с
линейно интерполированным по log(E)–log(σ) значением EPICS. Логарифмическая
интерполяция, а не линейная: сечение фотоэффекта идёт как E^-3, и на линейной
сетке между узлами ошибка интерполяции сама по себе доходит до десятков
процентов — мерили бы её, а не расхождение поставок.

УЗЛЫ У КРАЁВ ПОГЛОЩЕНИЯ ПРОПУСКАЮТСЯ. На краю сечение скачет в разы, и обе
поставки кладут там по две точки с одной энергией; любая интерполяция поперёк
края бессмысленна. Пропускается окрестность `--edge-window` по энергии вокруг
каждого края из `xcom_edges` (умолчание ±1 %); сколько узлов выброшено —
печатается вместе с окном, которым считали.

Окно ±1 % оставлено умолчанием НЕ потому, что оно достаточно: при нём остаются
ровно два узла с расхождением выше 20 % — у платины (Z = 78) и тулия
(Z = 69), — и оба стоят в 1–2 % за K-краем, на самом верху диапазона EPICS
(80.3 и 60.6 кэВ), то есть в последнем интервале её сетки сразу после скачка.
При ±3 % таких узлов не остаётся ни одного. Умолчание узкое нарочно: пусть
край виден в отчёте, а не заметается окном.

ЧЕГО СВЕРКА НЕ ПОКРЫВАЕТ, и это главное ограничение. Поставки перекрываются
только внизу шкалы: EPICS идёт 3.85 эВ … 219.8 кэВ, XCOM — 1 кэВ … 100 ГэВ.
**74.6 % узлов XCOM с фотоэффектом лежат ВЫШЕ верхней границы EPICS**, и про
них сверка не говорит ничего. У тяжёлых элементов потолок ещё ниже — у платины
80.3 кэВ, у тулия 60.6 кэВ.

    python compare_photo.py [--matdb ...] [--elements 1,6,13,53,82]
                            [--edge-window 0.03]
"""
import argparse
import bisect
import collections
import io
import math
import os
import sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_MATDB = os.path.join(ROOT, "BecquerelMonitor", "matdb.sqlite")

EDGE_WINDOW = 0.01          # ±1 % по энергии вокруг края поглощения (умолчание)


def interpolate(grid, values, energy):
    u"""log–log интерполяция; None вне сетки."""
    if energy <= grid[0] or energy >= grid[-1]:
        return None
    i = bisect.bisect_left(grid, energy)
    e0, e1 = grid[i - 1], grid[i]
    v0, v1 = values[i - 1], values[i]
    if not (v0 > 0.0 and v1 > 0.0) or not (e1 > e0):
        return None
    t = (math.log(energy) - math.log(e0)) / (math.log(e1) - math.log(e0))
    return math.exp(math.log(v0) + t * (math.log(v1) - math.log(v0)))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--matdb", default=DEFAULT_MATDB)
    p.add_argument("--elements", default="")
    p.add_argument("--edge-window", type=float, default=EDGE_WINDOW,
                   help=u"полуширина окна вокруг края, доля энергии")
    a = p.parse_args()
    db = sqlite3.connect(a.matdb)
    out = io.open(1, "w", encoding="utf-8", closefd=False)

    wanted = None
    if a.elements:
        wanted = set(int(x) for x in a.elements.split(","))

    edges = collections.defaultdict(list)
    for z, e in db.execute("select z, energy_ev from xcom_edges"):
        edges[z].append(e)

    # EPICS: у КАЖДОЙ подоболочки СВОЯ сетка, начинающаяся с её края. Складывать
    # можно только значения, приведённые к одной энергии, поэтому каждая
    # оболочка интерполируется отдельно, а ниже своего края даёт ноль. Сумма
    # «того, что совпало по энергии» занижает полное сечение в тысячи раз —
    # первый заход намерил именно это и ничего больше.
    shells = collections.defaultdict(lambda: collections.defaultdict(list))
    for z, seq, e, cs in db.execute(
            "select z, shell_seq, energy_ev, cs_b from epics_photo_subshell"
            " order by z, shell_seq, energy_ev"):
        shells[z][seq].append((e, cs))

    epics = {}
    for z, by_shell in shells.items():
        curves = []
        for seq, points in by_shell.items():
            grid = [e for e, _ in points]
            values = [c for _, c in points]
            if len(grid) >= 2:
                curves.append((grid, values))
        epics[z] = curves

    def epics_total(z, energy):
        u"""Полное фотопоглощение EPICS: сумма подоболочек в этой энергии."""
        total_cs = 0.0
        covered = False
        for grid, values in epics.get(z, ()):
            if energy < grid[0]:
                continue                      # ниже края оболочки — вклада нет
            if energy > grid[-1]:
                continue
            value = interpolate(grid, values, energy)
            if value is not None:
                total_cs += value
                covered = True
        return total_cs if covered else None

    buckets = collections.Counter()
    per_element = {}
    skipped = 0
    total = 0
    for z in sorted(epics):
        if wanted and z not in wanted:
            continue
        deviations = []
        for e, xcom in db.execute(
                "select energy_ev, photoelectric_b from xcom_cross_sections"
                " where z=? and photoelectric_b > 0 order by energy_ev", (z,)):
            if any(abs(e - edge) <= a.edge_window * edge for edge in edges.get(z, ())):
                skipped += 1
                continue
            value = epics_total(z, e)
            if not value:
                continue
            total += 1
            dev = abs(100.0 * (xcom / value - 1.0))
            deviations.append(dev)
            buckets[u"≤1 %" if dev <= 1 else u"≤5 %" if dev <= 5 else
                    u"≤20 %" if dev <= 20 else u"> 20 %"] += 1
        if deviations:
            deviations.sort()
            per_element[z] = (len(deviations),
                              deviations[len(deviations) // 2],
                              deviations[-1])

    out.write(u"# Фотоэффект: XCOM против EPICS2017, интерполяцией (D24)\n\n")
    out.write(u"элементов сверено: %d, узлов XCOM: %d, "
              u"пропущено у краёв поглощения (окно ±%.0f %%): %d\n\n"
              % (len(per_element), total, 100.0 * a.edge_window, skipped))
    for key in (u"≤1 %", u"≤5 %", u"≤20 %", u"> 20 %"):
        n = buckets[key]
        out.write(u"  расхождение %-7s %6d  %5.1f %%\n"
                  % (key, n, 100.0 * n / max(1, total)))

    worst = sorted(per_element.items(), key=lambda kv: -kv[1][2])[:10]
    out.write(u"\nхудшие элементы (по максимуму):\n")
    out.write(u"  %-4s %-8s %-10s %s\n" % (u"Z", u"узлов", u"медиана", u"максимум"))
    for z, (n, med, mx) in worst:
        out.write(u"  %-4d %-8d %-10.3f %.1f %%\n" % (z, n, med, mx))

    med_all = sorted(v[1] for v in per_element.values())
    if med_all:
        out.write(u"\nмедиана медиан по элементам: %.3f %%\n"
                  % med_all[len(med_all) // 2])
    out.flush()


if __name__ == "__main__":
    main()
