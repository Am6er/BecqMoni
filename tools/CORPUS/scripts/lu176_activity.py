# -*- coding: utf-8 -*-
u"""Активность Lu-176 в банке оксида лютеция — из состава вещества, а не паспорта.

ЗАЧЕМ. Три спектра Lu₂O₃ (ASN16, AS80x80, RC-103) сняты в ОДНОЙ банке 50 мл,
Ø40 × h15 мм (слово Amber 15.08.2026). Лютеций природный, а Lu-176 —
первичный радионуклид с распространённостью 2.599 %: значит активность пробы
задана её МАССОЙ и больше ничем, и эти спектры становятся паспортными без
всякого паспорта.

Что считается точно и что нет:

  ТОЧНО   удельная активность Lu₂O₃, Бк/г — из периода и распространённости
          нашей же базы (`nucdb.nuclides`), без единого допущения;
  ТОЧНО   геометрический объём слоя Ø40 × h15;
  НЕ ЗНАЕМ насыпную плотность порошка (монолит 9.42 г/см³, порошок в разы
          меньше) — а значит и массу. Поэтому активность печатается ТАБЛИЦЕЙ
          по плотности, а не одним числом.

⚠ **Ловушка, ради которой это написано отдельно.** Плотность ρ ≈ 2.45 г/см³
получена в §13ж ОБРАТНЫМ ходом — из расхождения кривой AS80x80 с измеренным
сумм-пиком. Взять её сюда, получить активность и потом проверять ею же
эффективность — круг. Настоящей паспортной проба станет только от НЕЗАВИСИМОЙ
массы: банку надо взвесить.

    python tools/CORPUS/scripts/lu176_activity.py [--mass=46.2] [--rho=2.45]
"""
import math
import os
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, os.pardir, os.pardir, os.pardir))
DB = os.path.join(REPO, 'BecquerelMonitor', 'bin', 'Debug_Codex', 'nucdb.sqlite')

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

NA = 6.02214076e23
AW_LU = 174.9668          # атомная масса природного лютеция
AW_O = 15.999
DIAMETER_MM = 40.0        # банка 50 мл: Ø40 × h15 (слово Amber)
HEIGHT_MM = 15.0
RHO_CRYSTAL = 9.42        # монолитный Lu2O3, для верхней границы


def lu176():
    conn = sqlite3.connect(DB)
    row = conn.execute(
        'select half_life_sec, abundance from nuclides where nucid = ?', ('176LU',)).fetchone()
    if not row:
        sys.exit('в nucdb нет 176LU')
    return float(row[0]), float(row[1]) / 100.0


def main():
    mass_g = rho = None
    for a in sys.argv[1:]:
        if a.startswith('--mass='):
            mass_g = float(a.split('=', 1)[1])
        elif a.startswith('--rho='):
            rho = float(a.split('=', 1)[1])

    t_half, abundance = lu176()
    lam = math.log(2.0) / t_half
    w_lu = 2 * AW_LU / (2 * AW_LU + 3 * AW_O)

    n_per_g_lu = NA / AW_LU * abundance
    a_per_g_lu = n_per_g_lu * lam
    a_per_g_oxide = a_per_g_lu * w_lu

    volume_cm3 = math.pi * (DIAMETER_MM / 20.0) ** 2 * (HEIGHT_MM / 10.0)

    print(u'Lu-176 из нашей базы: T½ = %.5g с (%.4g лет), распространённость %.3f %%'
          % (t_half, t_half / 3.1557e7, 100 * abundance))
    print(u'постоянная распада λ = %.5g с⁻¹' % lam)
    print(u'массовая доля Lu в Lu₂O₃ = %.5f' % w_lu)
    print()
    print(u'УДЕЛЬНАЯ АКТИВНОСТЬ (точно, без допущений):')
    print(u'   %.3f Бк на грамм ПРИРОДНОГО ЛЮТЕЦИЯ' % a_per_g_lu)
    print(u'   %.3f Бк на грамм Lu₂O₃' % a_per_g_oxide)
    print()
    print(u'Банка Ø%.0f × h%.0f мм → объём слоя %.3f см³' % (DIAMETER_MM, HEIGHT_MM, volume_cm3))
    print()

    if mass_g is not None:
        print(u'МАССА ЗАДАНА: %.3f г → ρ = %.3f г/см³ → A = %.1f Бк'
              % (mass_g, mass_g / volume_cm3, mass_g * a_per_g_oxide))
        return
    if rho is not None:
        m = rho * volume_cm3
        print(u'ПЛОТНОСТЬ ЗАДАНА: %.3f г/см³ → масса %.2f г → A = %.1f Бк'
              % (rho, m, m * a_per_g_oxide))
        return

    print(u'Массы нет — активность таблицей по насыпной плотности:')
    print(u'   %-10s %-12s %-12s' % (u'ρ, г/см³', u'масса, г', u'A(Lu-176), Бк'))
    for r in (1.0, 1.5, 2.0, 2.45, 2.5, 3.0, 3.5, 4.5, RHO_CRYSTAL):
        m = r * volume_cm3
        mark = u'   ← монолит' if r == RHO_CRYSTAL else (
            u'   ← из §13ж, ОБРАТНЫМ ходом (в проверку эффективности не годится)'
            if abs(r - 2.45) < 1e-9 else u'')
        print(u'   %-10.2f %-12.2f %-12.1f%s' % (r, m, m * a_per_g_oxide, mark))
    print()
    print(u'⛔ Одно взвешивание банки закрывает эту таблицу одним числом и делает')
    print(u'   три спектра паспортными. До него активность НЕ ИЗВЕСТНА, а выведена.')


if __name__ == '__main__':
    main()
