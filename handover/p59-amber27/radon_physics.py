# -*- coding: utf-8 -*-
"""П59 (AMBER27): ожидаемые отношения активностей дочерних продуктов радона и торона
на ватном фильтре — счёт, а не память.

Периоды и ветвления — из nucdb (`nuclides.half_life_sec`, `decay_chain.perc`), те же,
что у разбора FSA. Времена — из файлов спектров (StartTime, MeasurementTime).

Модель фильтра. Прокачка воздуха с постоянным расходом Q в течение τ_c до t = 0
(конец прокачки = старт спектра 1; вариант с паузой Δ между ними — ключом);
в воздухе аэрозольные дочерние Po-218 : Pb-214 : Bi-214 с активностями 1 : f_B : f_C
(равновесие в комнате неизвестно) и Pb-212 : Bi-212 = 1 : r0; газов (Rn-222, Rn-220) на
фильтре нет. Осаждение атомов d_X = Q·n_X = Q·c_X/λ_X; на фильтре dN/dt = d + λ_пред·N_пред − λ·N.
После t = 0 — только распад/подпитка по ряду (Бейтман через матричную экспоненту,
здесь — численно, шаг 1 с, для проверки — аналитика Pb-212).

FSA даёт СРЕДНЮЮ по окну съёмки активность (амплитуда = число распадов за живое время),
поэтому все ожидания — средние по окнам [0; T1] и [t2; t2+T2].

    python handover/p59-amber27/radon_physics.py [--out=<csv>]
"""
import io
import math
import os
import sqlite3
import sys
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
NUCDB = os.path.join(REPO, 'BecquerelMonitor', 'nucdb.sqlite')

# --- времена из файлов спектров (StartTime, MeasurementTime) --------------------
T0 = datetime(2023, 6, 20, 23, 11, 24, 329599)           # старт спектра 1
T1 = 6964.0                                              # с, MeasurementTime спектра 1
START2 = datetime(2023, 6, 21, 12, 8, 17, 384699)        # старт спектра 2
T2 = 7018.0                                              # с, MeasurementTime спектра 2
t2 = (START2 - T0).total_seconds()                       # начало окна 2 от старта 1


def halflives():
    c = sqlite3.connect('file:' + NUCDB.replace('\\', '/') + '?mode=ro', uri=True)
    cur = c.cursor()
    hl = {}
    for n in ('218PO', '214PB', '214BI', '212PB', '212BI', '208TL'):
        hl[n] = float(cur.execute(
            'select half_life_sec from nuclides where nucid=? and l_seqno=0', (n,)).fetchone()[0])
    br = float(cur.execute(
        "select perc from decay_chain where nucid='212BI' and l_seqno=0 and daughter_nucid='208TL'"
    ).fetchone()[0]) / 100.0
    c.close()
    return hl, br


def lam(hl):
    return math.log(2.0) / hl


def integrate_chain(lams, deposit, n0, dt, steps):
    """Явная схема по ряду A→B→C (…): lams — λ по порядку, deposit — скорость осаждения
    атомов, n0 — начальные атомы. Возвращает атомы после steps шагов и накопленные
    интегралы активностей (распадов) за это время. Шаг мал (1 с) против 3 мин."""
    n = list(n0)
    decays = [0.0] * len(n)
    for _ in range(steps):
        prev = 0.0
        newn = []
        for i, l in enumerate(lams):
            dn = deposit[i] + prev - l * n[i]
            decays[i] += l * n[i] * dt
            prev = l * n[i]
            newn.append(n[i] + dn * dt)
        n = newn
    return n, decays


def mean_activity_window(lams, n_at_zero, t_from, t_to, dt=1.0):
    """Средняя активность каждого члена по окну [t_from; t_to] при свободном распаде
    от состояния n_at_zero в момент 0 (осаждения нет)."""
    zero = [0.0] * len(lams)
    n, _ = integrate_chain(lams, zero, n_at_zero, dt, int(round(t_from / dt)))
    n, decays = integrate_chain(lams, zero, n, dt, int(round((t_to - t_from) / dt)))
    return [d / (t_to - t_from) for d in decays]


def main():
    out = None
    pause = 0.0
    for a in sys.argv[1:]:
        if a.startswith('--out='):
            out = a[6:]
        elif a.startswith('--pause='):
            pause = float(a[8:]) * 3600.0   # пауза между концом прокачки и стартом, ч
        elif a.startswith('--t1='):
            # длительность окна 1, с: у спектра 1 MeasurementTime 6964, а EndTime−StartTime = 4921
            global T1
            T1 = float(a[5:])
    hl, br_tl = halflives()
    lines = []

    def say(s):
        print(s)
        lines.append(s)

    say('# П59 AMBER27: ожидания физики (nucdb: ' + ', '.join(
        '%s %.4g с' % (k, v) for k, v in sorted(hl.items())) + '; Bi-212→Tl-208 %.4f)' % br_tl)
    say('# окно 1: [0; %.1f] с; окно 2: [%.1f; %.1f] с от старта спектра 1 (%.3f ч старт-к-старту, '
        '%.3f ч от конца 1 до старта 2); пауза прокачка→старт %.2f ч' % (
            T1, t2, t2 + T2, t2 / 3600.0, (t2 - T1) / 3600.0, pause / 3600.0))

    # ---- (2) Pb-212 между съёмками: аналитика ------------------------------------
    lP = lam(hl['212PB'])
    m1 = (1.0 - math.exp(-lP * T1)) / (lP * T1)
    m2 = (math.exp(-lP * t2) - math.exp(-lP * (t2 + T2))) / (lP * T2)
    say('EXPECT,Pb212_spec1_over_spec2,%.4f,средняя активность Pb-212 окно1/окно2 при чистом распаде '
        '(λ = ln2/%.3f ч)' % (m1 / m2, hl['212PB'] / 3600.0))

    # ---- (1) спектр 2: Bi-212/Pb-212 и Tl-208/Bi-212 ----------------------------
    lB = lam(hl['212BI'])
    lT = lam(hl['208TL'])
    say('EXPECT,Bi212_over_Pb212_transient,%.4f,λB/(λB−λP) — переходное равновесие' % (lB / (lB - lP)))
    for r0 in (0.0, 0.5, 1.0):
        # атомы при t=0: Pb-212 A=1 Бк → N = 1/λ; Bi-212 r0 Бк; Tl-208 по вековому r0·br
        n0 = [1.0 / lP, r0 / lB, r0 * br_tl / lT]
        a1 = mean_activity_window([lP, lB, lT], n0, 0.0, T1)
        a2 = mean_activity_window([lP, lB, lT], n0, t2, t2 + T2)
        say('EXPECT,spec2_Bi212_over_Pb212_r0=%.1f,%.4f,средние по окну 2' % (r0, a2[1] / a2[0]))
        say('EXPECT,spec2_Tl208_over_Bi212_r0=%.1f,%.4f,физическое отношение (в единицах FSA-ряда ×1/%.4f = %.4f)'
            % (r0, a2[2] / a2[1], br_tl, a2[2] / a2[1] / br_tl))
        say('EXPECT,spec1_Bi212_over_Pb212_r0=%.1f,%.4f,средние по окну 1 (наблюдение, не тест)' % (r0, a1[1] / a1[0]))
        say('EXPECT,Pb212_spec1_over_spec2_numeric_r0=%.1f,%.4f,численно (контроль аналитики)' % (r0, a1[0] / a2[0]))

    # ---- (3) спектр 1: Bi-214/Pb-214 по Бейтману с накоплением --------------------
    lA = lam(hl['218PO'])
    lPb = lam(hl['214PB'])
    lBi = lam(hl['214BI'])
    say('# (3) накопление τ_c при воздухе Po-218:Pb-214:Bi-214 = 1:fB:fC, затем окно 1')
    grid = []
    for tau_h in (1.0, 2.0, 4.0, 1e9):
        tau = tau_h * 3600.0 if tau_h < 1e8 else 48.0 * 3600.0   # ∞ ≈ 48 ч ≫ 27 мин
        for fB, fC in ((1.0, 1.0), (0.9, 0.8), (0.7, 0.5), (0.5, 0.3), (0.6, 0.6), (0.8, 0.4)):
            dep = [1.0 / lA, fB / lPb, fC / lBi]      # d_X = c_X/λ_X при Q=1, c_A=1
            dt = 1.0
            n, _ = integrate_chain([lA, lPb, lBi], dep, [0.0, 0.0, 0.0], dt, int(tau / dt))
            if pause > 0:
                n, _ = integrate_chain([lA, lPb, lBi], [0, 0, 0], n, dt, int(pause / dt))
            a1 = mean_activity_window([lA, lPb, lBi], n, 0.0, T1)
            a2 = mean_activity_window([lA, lPb, lBi], n, t2, t2 + T2)
            ratio = a1[2] / a1[1]
            grid.append(ratio)
            say('EXPECT,spec1_Bi214_over_Pb214_tau=%s_fB=%.1f_fC=%.1f,%.4f,Pb-214 окно2/окно1 = %.2e'
                % ('inf' if tau_h > 1e8 else '%.0fh' % tau_h, fB, fC, ratio, a2[1] / a1[1]))
    say('EXPECT,spec1_Bi214_over_Pb214_range,%.3f..%.3f,диапазон по накоплению 1 ч…∞ и воздуху (1:1:1 … 1:0.5:0.3)'
        % (min(grid), max(grid)))

    # чувствительность к паузе: 10 мин между концом прокачки и стартом съёмки
    say('# пауза 0 ч; для паузы задайте --pause=<ч>')
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
