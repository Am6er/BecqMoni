# -*- coding: utf-8 -*-
u"""Доля когерентного (рэлеевского) рассеяния в веществе кристалла по энергии.

Заведено 10.09.2026 по вопросу Amber: «где рэлей перестаёт быть заметен в CsI».
Источник — NIST XCOM, таблица xcom_cross_sections в matdb.sqlite; читается
только на чтение (file:...?mode=ro), базу не трогает.

    python tools/effmaker/coherent_share.py

CsI берётся как Cs (Z=55) и I (Z=53) в равных долях по атомам: макроскопическое
сечение — сумма атомных с весом 1:1.

ВЫВОД СЧИТАННЫХ ЧИСЕЛ — в handover/handover-2026-09-10-s3-channels.md.
Там же оговорка, которая важнее самих чисел: это доля в ОСЛАБЛЕНИИ, а влияние
на отклик она завышает — когерентное энергию не передаёт, депозита не даёт,
меняет только направление.
"""
import math
import sqlite3
import sys

DB = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\matdb.sqlite"
Z_CS, Z_I = 55, 53

con = sqlite3.connect("file:%s?mode=ro" % DB.replace("\\", "/"), uri=True)
cur = con.cursor()

rows = {}
for z in (Z_CS, Z_I):
    cur.execute("select energy_ev, coherent_b, incoherent_b, photoelectric_b,"
                " pair_nuclear_b, pair_electron_b from xcom_cross_sections"
                " where z = ? order by energy_ev", (z,))
    rows[z] = dict((r[0], r[1:]) for r in cur.fetchall())

common = sorted(set(rows[Z_CS]) & set(rows[Z_I]))
data = []
for ev in common:
    a, b = rows[Z_CS][ev], rows[Z_I][ev]
    coh = a[0] + b[0]
    inc = a[1] + b[1]
    pho = a[2] + b[2]
    pair = a[3] + b[3] + a[4] + b[4]
    total = coh + inc + pho + pair
    if total > 0:
        data.append((ev / 1000.0, coh, inc, pho, pair, total))

sys.stdout.write(u"узлов сетки XCOM, общих у Cs и I: %d\n" % len(data))
sys.stdout.write(u"\n%9s %10s %10s %10s %10s\n"
                 % (u"кэВ", u"когер.,%", u"неког.,%", u"фото,%", u"пары,%"))
for kev, coh, inc, pho, pair, total in data:
    if 10.0 <= kev <= 3000.0:
        sys.stdout.write(u"%9.2f %10.3f %10.3f %10.3f %10.3f\n"
                         % (kev, 100 * coh / total, 100 * inc / total,
                            100 * pho / total, 100 * pair / total))


def crossing(level):
    u"""ПОСЛЕДНЕЕ пересечение сверху вниз: выше этой энергии доля больше не
    поднимается до level. Кривая НЕ монотонна — у неё два горба (до K-краёв и
    около 200 кэВ), поэтому первое пересечение слева дало бы вздор."""
    last = None
    for i in range(1, len(data)):
        k0 = data[i - 1][0]
        s0 = 100.0 * data[i - 1][1] / data[i - 1][5]
        k1 = data[i][0]
        s1 = 100.0 * data[i][1] / data[i][5]
        if s0 >= level > s1:
            t = (math.log(s0) - math.log(level)) / (math.log(s0) - math.log(s1))
            last = math.exp(math.log(k0) + t * (math.log(k1) - math.log(k0)))
    return last


sys.stdout.write(u"\n=== выше какой энергии доля когерентного больше НЕ поднимается ===\n")
for level in (8.0, 5.0, 3.0, 2.0, 1.0, 0.5):
    e = crossing(level)
    sys.stdout.write(u"  ниже %4.1f %% — выше %s\n"
                     % (level, (u"%.0f кэВ" % e) if e else u"(не пересекает)"))

sys.stdout.write(u"\n=== когерентных на сто взаимодействий, ДАЮЩИХ сигнал ===\n")
want = (20.0, 30.0, 40.0, 60.0, 100.0, 200.0, 300.0, 500.0, 1000.0, 1500.0, 2000.0, 3000.0)
for kev, coh, inc, pho, pair, total in data:
    if kev in want:
        signal = inc + pho + pair
        if signal > 0:
            sys.stdout.write(u"  %8.1f кэВ  %7.3f\n" % (kev, 100.0 * coh / signal))

peak = max(data, key=lambda r: r[1] / r[5])
sys.stdout.write(u"\nмаксимум доли когерентного: %.2f %% при %.2f кэВ\n"
                 % (100 * peak[1] / peak[5], peak[0]))

# Второй горб — после K-краёв Cs (35.99) и I (33.17).
above = [r for r in data if r[0] > 40.0]
peak2 = max(above, key=lambda r: r[1] / r[5])
sys.stdout.write(u"второй горб (выше K-краёв): %.2f %% при %.2f кэВ\n"
                 % (100 * peak2[1] / peak2[5], peak2[0]))
