# -*- coding: utf-8 -*-
u"""D1: сумма интерполяций против интерполяции суммы (каналы XCOM).

Полное ослабление в программе получается ДВУМЯ путями, и на узлах сетки они
совпадают тождественно, а между узлами — нет:

  * `Total` собирается сложением каналов В УЗЛАХ и потом интерполируется;
  * парциальные сечения интерполируются ПОРОЗНЬ и складываются потребителем.

Расхождение неизбежно: log–log интерполяция не аддитивна. Экспонента суммы
логарифмов — не сумма экспонент, и чем сильнее каналы различаются наклоном
(фотоэффект падает как E⁻³, комптон почти плоский), тем больше разница.

Прежнее число 13.7 % снято на СТАРОМ правиле, когда каналы интерполировались
лог-линейно, и после перехода на лог-лог (`2473e9e`) не перемерялось (D1,
`scheme.md` §9а A-6). Аудит записал, что для этого нужен прогон по корпусу —
не нужен: это чисто числовое свойство таблиц, и меряется оно прямо здесь.

ПРАВИЛО ПОВТОРЕНО ЗА `MaterialDatabase.Interpolate` дословно, включая обе его
оговорки: на крае поглощения (две точки с одной энергией) берётся верхнее
значение; если у одного из концов интервала ноль — интерполяция ЛИНЕЙНАЯ, а не
логарифмическая (рождение пар ниже порога тождественно нулевое).

Точки проб берутся внутри каждого интервала сетки, по умолчанию в его
логарифмической середине — там расхождение максимально.

    python check_interpolation.py [--matdb ...] [--samples 3]
"""
import argparse
import collections
import io
import math
import os
import sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_MATDB = os.path.join(ROOT, "BecquerelMonitor", "matdb.sqlite")

CHANNELS = ["coherent_b", "incoherent_b", "photoelectric_b",
            "pair_nuclear_b", "pair_electron_b"]


def interpolate(x0, x1, y0, y1, x):
    u"""Дословно `MaterialDatabase.Interpolate` на одном интервале."""
    if not (x1 > x0):
        return y1                       # край поглощения: берётся верхняя точка
    if not (y0 > 0.0) or not (y1 > 0.0):
        f = (x - x0) / (x1 - x0)        # канал открывается не с нуля шкалы
        return y0 + f * (y1 - y0)
    t = (math.log(x) - math.log(x0)) / (math.log(x1) - math.log(x0))
    return math.exp(math.log(y0) + t * (math.log(y1) - math.log(y0)))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--matdb", default=DEFAULT_MATDB)
    p.add_argument("--samples", type=int, default=1,
                   help=u"проб внутри интервала (1 — логарифмическая середина)")
    a = p.parse_args()
    db = sqlite3.connect(a.matdb)
    out = io.open(1, "w", encoding="utf-8", closefd=False)

    buckets = collections.Counter()
    worst = []
    points = 0
    by_energy = collections.defaultdict(list)
    for z in range(1, 101):
        rows = db.execute(
            "select energy_ev, %s from xcom_cross_sections where z=?"
            " order by energy_ev" % ", ".join(CHANNELS), (z,)).fetchall()
        if len(rows) < 2:
            continue
        for i in range(len(rows) - 1):
            lo, hi = rows[i], rows[i + 1]
            if not (hi[0] > lo[0]):
                continue                # край поглощения — интервала нет
            for k in range(1, a.samples + 1):
                f = k / float(a.samples + 1)
                x = math.exp(math.log(lo[0]) + f * (math.log(hi[0]) - math.log(lo[0])))
                # путь 1: сложить в узлах, потом интерполировать
                total_lo = sum(lo[1:])
                total_hi = sum(hi[1:])
                whole = interpolate(lo[0], hi[0], total_lo, total_hi, x)
                # путь 2: интерполировать порознь, потом сложить
                parts = sum(interpolate(lo[0], hi[0], lo[1 + c], hi[1 + c], x)
                            for c in range(len(CHANNELS)))
                if not (whole > 0.0):
                    continue
                points += 1
                dev = abs(100.0 * (parts / whole - 1.0))
                buckets[u"≤0.1 %" if dev <= 0.1 else u"≤1 %" if dev <= 1 else
                        u"≤5 %" if dev <= 5 else u"> 5 %"] += 1
                worst.append((dev, z, x))
                by_energy[int(math.floor(math.log10(max(x, 1.0))))].append(dev)

    out.write(u"# Сумма интерполяций против интерполяции суммы (D1)\n\n")
    out.write(u"проб: %d (по %d внутри каждого интервала сетки)\n\n"
              % (points, a.samples))
    for key in (u"≤0.1 %", u"≤1 %", u"≤5 %", u"> 5 %"):
        n = buckets[key]
        out.write(u"  расхождение %-8s %7d  %5.1f %%\n"
                  % (key, n, 100.0 * n / max(1, points)))

    worst.sort(reverse=True)
    if worst:
        allk = sorted(d for d, _, _ in worst)
        out.write(u"\nмедиана %.4f %%, 90-й процентиль %.3f %%, максимум %.2f %%\n"
                  % (allk[len(allk) // 2], allk[int(0.9 * len(allk))], allk[-1]))
        out.write(u"\nхудшие пять: %s\n"
                  % u", ".join(u"Z=%d при %.4g эВ — %.2f %%" % (z, x, d)
                               for d, z, x in worst[:5]))

    out.write(u"\nпо декадам энергии (медиана / максимум):\n")
    for decade in sorted(by_energy):
        vals = sorted(by_energy[decade])
        out.write(u"  10^%-2d эВ  проб %6d   %.4f %%  /  %.2f %%\n"
                  % (decade, len(vals), vals[len(vals) // 2], vals[-1]))

    # Где неаддитивность видна в СЧЁТЕ. `LinearAttenuationWithoutCoherent`
    # вычитает отдельно интерполированный когерентный канал из интерполированного
    # ПОЛНОГО и зажимает разность нулём (`Math.Max(0.0, value)`). Зажим — молчащий
    # признак отказа: если он срабатывает, вещество на пути стало прозрачным.
    clamp = below = checked_ = 0
    worst_gap = 0.0
    for z in range(1, 101):
        rows = db.execute(
            "select energy_ev, %s from xcom_cross_sections where z=?"
            " order by energy_ev" % ", ".join(CHANNELS), (z,)).fetchall()
        for i in range(len(rows) - 1):
            lo, hi = rows[i], rows[i + 1]
            if not (hi[0] > lo[0]):
                continue
            for k in range(1, a.samples + 1):
                f = k / float(a.samples + 1)
                x = math.exp(math.log(lo[0]) + f * (math.log(hi[0]) - math.log(lo[0])))
                total = interpolate(lo[0], hi[0], sum(lo[1:]), sum(hi[1:]), x)
                coherent = interpolate(lo[0], hi[0], lo[1], hi[1], x)
                incoherent = interpolate(lo[0], hi[0], lo[2], hi[2], x)
                checked_ += 1
                if total - coherent <= 0.0:
                    clamp += 1
                elif incoherent > total - coherent:
                    below += 1
                    gap = 100.0 * (incoherent / (total - coherent) - 1.0)
                    worst_gap = max(worst_gap, gap)

    out.write(u"\nгде это видно в счёте (проб %d):\n" % checked_)
    out.write(u"  полное минус когерентное ушло в НОЛЬ (зажим сработал): %d\n" % clamp)
    out.write(u"  некогерентное БОЛЬШЕ остатка (доля рассеяния > 1):     %d" % below)
    out.write(u", худший перебор %.2f %%\n" % worst_gap if below else u"\n")
    out.flush()


if __name__ == "__main__":
    main()
