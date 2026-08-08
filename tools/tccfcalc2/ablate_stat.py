# -*- coding: utf-8 -*-
u"""
Разбор лестницы абляций С ПОГРЕШНОСТЯМИ (F26).

`ablate_phys.py` печатает отношения к Geant4, но без них сдвиги в 1–5 %
прочитать нельзя: у пика на 1461 кэВ в маринелли на миллион историй
приходится порядка тысячи событий. Здесь берутся те же csv, но со столбцом
ошибки, и печатается:

* сдвиг каждой правки ОТНОСИТЕЛЬНО физики 6, в процентах и в сигмах;
* проверка аддитивности: (связанное + тормозное) против «обе вместе».
  Если правки независимы, сумма сдвигов обязана совпасть с совместным
  сдвигом; расхождение больше двух сигм — либо взаимодействие правок, либо
  недостаток статистики, и то и другое надо назвать, а не замолчать.

Ошибка сдвига считается как корень из суммы квадратов: потоки случайных
чисел у вариантов расходятся с первого же лишнего розыгрыша, поэтому
считать их коррелированными нельзя.

    python ablate_stat.py [--dir=out/ablate] [--geoms=...]
"""
import argparse
import io
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

VARIANTS = [u"физика_6", u"p_связанное", u"p_тормозное", u"физика_8"]
TITLES = {u"физика_6": u"физика 6", u"p_связанное": u"+ связанное",
          u"p_тормозное": u"+ тормозное", u"физика_8": u"физика 8"}


def read_csv(path):
    u"""{E: (эффективность, ошибка в процентах)} из выхода effsim."""
    out = {}
    if not os.path.exists(path):
        return out
    for line in io.open(path, encoding="utf-8"):
        parts = line.split(",")
        if len(parts) < 3:
            continue
        try:
            out[round(float(parts[0]), 1)] = (float(parts[1]), float(parts[2]))
        except ValueError:
            continue
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--dir", default=os.path.join(HERE, "out", "ablate"))
    p.add_argument("--geoms", default="Nano16Pro_tube,Nano16Pro,"
                                      "Nano16Pro_Marinelli,RadiaCode_AuthorMarinelli0.5")
    args = p.parse_args()

    print(u"# Абляции 7 и 8: сдвиги относительно физики 6, в сигмах")
    print(u"")
    print(u"Сдвиг — по ПИКУ полного поглощения, в процентах от физики 6.")
    print(u"σ — статистика самого расчёта (корень из суммы квадратов двух")
    print(u"прогонов); Geant4 в неё не входит, он общий делитель и в разности")
    print(u"сокращается.")

    for g in args.geoms.split(","):
        data = {}
        for v in VARIANTS:
            data[v] = read_csv(os.path.join(args.dir, "%s_%s.csv" % (g, v)))

        if not data[u"физика_6"]:
            continue

        print(u"")
        print(u"## %s" % g)
        print(u"")
        print(u"| E, кэВ | физика 6 | + связанное | + тормозное | физика 8 | сумма частей | аддитивность |")
        print(u"|---|---|---|---|---|---|---|")
        for e in sorted(data[u"физика_6"]):
            base, base_err = data[u"физика_6"][e]
            if not base:
                continue

            def shift(v):
                if e not in data[v]:
                    return None, None
                value, err = data[v][e]
                delta = 100.0 * (value / base - 1.0)
                # ошибки складываются квадратично: потоки разошлись
                sigma = math.hypot(err, base_err) * value / base
                return delta, sigma

            d_b, s_b = shift(u"p_связанное")
            d_t, s_t = shift(u"p_тормозное")
            d_8, s_8 = shift(u"физика_8")
            if d_8 is None:
                continue

            summed = (d_b or 0.0) + (d_t or 0.0)
            s_sum = math.hypot(s_b or 0.0, s_t or 0.0)
            gap = d_8 - summed
            gap_sigma = math.hypot(s_8, s_sum)
            verdict = (u"складывается" if abs(gap) <= 2.0 * gap_sigma
                       else u"**НЕ складывается**")

            def cell(d, s):
                return u"%+.1f ± %.1f" % (d, s) if d is not None else u"—"

            print(u"| %.0f | %.4e ± %.1f %% | %s | %s | %s | %+.1f ± %.1f | %s (%.1fσ) |"
                  % (e, base, base_err, cell(d_b, s_b), cell(d_t, s_t),
                     cell(d_8, s_8), summed, s_sum, verdict,
                     abs(gap) / gap_sigma if gap_sigma else 0.0))


if __name__ == "__main__":
    main()
