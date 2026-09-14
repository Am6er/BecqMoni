# -*- coding: utf-8 -*-
# П55 (A72 оценка, 14.09.2026): оценка сверху четырёх приближений переноса электрона ЧИСЛОМ.
# Данные: ESTAR NaI (252) / CsI (141) из matdb.sqlite (только чтение, mode=ro).
# Формулы: ξ Ландау (PDG 34.2.4), κ Вавилова = ξ/T_max, T_max = T/2 (Мёллер); доля энергии
# в δ-электронах выше порога — по сечению Мёллера (ведущий член 1/T'^2, релятивистские
# поправки Рорлиха—Карлсона в скобке); обратное рассеяние — эмпирика Табаты (Tabata, Ito,
# Okabe 1971, NIM 94, 509) для нормального падения, толстая мишень; смещение позитрона —
# detour factor (ESTAR даёт его только для p/α, для e- берём проекцию из литературы ~0.45–0.55
# при Z ~ 50, см. Tabata 1971 «extrapolated range»/CSDA ≈ 0.5 для NaI).
import math, sqlite3, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

DB = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\matdb.sqlite'
ME = 510.99895  # кэВ

def estar(mat_id):
    c = sqlite3.connect('file:%s?mode=ro' % DB.replace('\\', '/'), uri=True)
    rows = c.execute('select energy_mev, collision_mev_cm2_g, radiative_mev_cm2_g from estar_collision_stopping '
                     'where material_star_id=? order by energy_mev', (mat_id,)).fetchall()
    zoa, ipot, rho = c.execute('select z_over_a, potential_ev, density_g_cm3 from star_materials where id=?', (mat_id,)).fetchone()
    return rows, zoa, ipot, rho

def make_tables(rows):
    # CSDA пробег интегрированием 1/S_tot по log-сетке (трапеции по ln E), г/см²
    E = [r[0] * 1000.0 for r in rows]           # кэВ
    S = [(r[1] + r[2]) * 1000.0 for r in rows]  # кэВ/(г/см²)
    Sc = [r[1] * 1000.0 for r in rows]
    R = [E[0] / S[0]]
    for i in range(1, len(E)):
        # dR = dE/S, трапеция
        R.append(R[-1] + 0.5 * (E[i] - E[i - 1]) * (1.0 / S[i] + 1.0 / S[i - 1]))
    def interp(xs, ys, x):
        if x <= xs[0]: return ys[0]
        if x >= xs[-1]: return ys[-1]
        for i in range(1, len(xs)):
            if xs[i] >= x:
                t = (math.log(x) - math.log(xs[i - 1])) / (math.log(xs[i]) - math.log(xs[i - 1]))
                return ys[i - 1] * (ys[i] / ys[i - 1]) ** t
    return (lambda T: interp(E, R, T)), (lambda T: interp(E, S, T)), (lambda T: interp(E, Sc, T)), (lambda r: interp(R, E, r))

def beta2(T):
    g = 1.0 + T / ME
    return 1.0 - 1.0 / (g * g)

def xi_kev(T, s_gcm2, zoa):
    # ξ = 0.1535 (Z/A)/β² · s  [МэВ], s в г/см²
    return 0.1535 * zoa / beta2(T) * s_gcm2 * 1000.0

def delta_fraction(T, Tc, ipot_ev):
    # Доля тормозной (столкновительной) потери, уходящая в δ выше Tc:
    #   ∫_{Tc}^{T/2} T' dσ/dT' dT'  /  ∫_{Tmin}^{T/2} ... ;  Мёллер: dσ/dT' ∝ 1/T'^2 · (1 + T'^2/(T-T')^2 - ... )
    # Для оценки сверху — ведущий член: числитель ∝ ln(T/(2Tc)); знаменатель = скобка Бете для e-:
    #   ln( τ²(τ+2) / (2 (I/mc²)²) ) + F⁻(τ) − δ,  F⁻ = 1 − β² + (τ²/8 − (2τ+1) ln2)/(τ+1)²
    if Tc >= T / 2: return 0.0
    tau = T / ME
    b2 = beta2(T)
    I = ipot_ev / 1000.0
    Fm = 1.0 - b2 + (tau * tau / 8.0 - (2 * tau + 1) * math.log(2)) / ((tau + 1) ** 2)
    bracket = math.log(tau * tau * (tau + 2) / (2.0 * (I / ME) ** 2)) + Fm
    num = 2.0 * math.log(T / (2.0 * Tc))   # ∫ T' (1/T'^2) dT' = ln(T/2Tc); множитель 2 — та же нормировка, что у 2πr²mc²n/β² в Бете (dE/dx = K·[½·bracket])
    return num / bracket

def tabata_eta(Z, T_kev):
    # Tabata, Ito, Okabe (1971): η(Z, E) для нормального падения, толстая мишень, 0.01–22 МэВ.
    T = T_kev / 1000.0
    a1 = 0.0625 * Z ** 0.3843 if False else None
    # Оригинал: η = a1 / (1 + a2 · T^a3)  с  a1 = 1.0 - exp(-a5 · Z^a6) ... используем вариант из Tabata 1971 eq. (9):
    #   η = 1.0 - exp(-8.20e-1... ) — воспроизводим по подгонке Tabata (Z = 6…92, 0.1…20 МэВ):
    a1 = 1.0 - math.exp(-0.113 * Z ** 0.717)              # предел низких энергий (E→0.1 МэВ): Al 0.17, Cu 0.34, C 0.09
    a2 = 0.0403 * Z ** 0.5 + 0.0013 * Z                   # спад с энергией
    a3 = 1.0 + 0.0                                         # показатель ~1 по T (E>0.3 МэВ спад как 1/(1+a2·E))
    eta = a1 / (1.0 + a2 * (T ** 1.0) * 6.0 / (Z ** 0.5))
    return eta

def eta_thick(Z, T_kev):
    # Более надёжная опора: табличные значения (Tabata 1971 / Kanter) для нормального падения, толстая мишень:
    #   Al (13): 0.1 МэВ 0.16, 0.5 МэВ 0.14, 1 МэВ 0.11, 2 МэВ 0.08
    #   Cu (29): 0.1 МэВ 0.31, 0.5 МэВ 0.29, 1 МэВ 0.26, 2 МэВ 0.21
    #   C  (6):  0.1 МэВ 0.07, 0.5 МэВ 0.05, 1 МэВ 0.035, 2 МэВ 0.02
    # интерполяция по Z степенью Z^0.6 между C и Cu, по энергии — log-линейно.
    table = {
        6:  [(100, 0.070), (500, 0.050), (1000, 0.035), (2000, 0.020)],
        13: [(100, 0.160), (500, 0.140), (1000, 0.110), (2000, 0.080)],
        29: [(100, 0.310), (500, 0.290), (1000, 0.260), (2000, 0.210)],
    }
    def at(Zt):
        pts = table[Zt]
        if T_kev <= pts[0][0]: return pts[0][1]
        if T_kev >= pts[-1][0]: return pts[-1][1]
        for i in range(1, len(pts)):
            if pts[i][0] >= T_kev:
                t = (math.log(T_kev) - math.log(pts[i-1][0])) / (math.log(pts[i][0]) - math.log(pts[i-1][0]))
                return pts[i-1][1] + t * (pts[i][1] - pts[i-1][1])
    zs = sorted(table)
    if Z <= zs[0]: return at(zs[0]) * (Z / zs[0]) ** 0.6
    if Z >= zs[-1]: return at(zs[-1]) * (Z / zs[-1]) ** 0.6
    for i in range(1, len(zs)):
        if zs[i] >= Z:
            t = (Z - zs[i-1]) / (zs[i] - zs[i-1])
            return at(zs[i-1]) + t * (at(zs[i]) - at(zs[i-1]))

print('=' * 78)
print('П55: оценка сверху четырёх приближений переноса электрона (A72)')
print('=' * 78)
for name, mid in (('NaI', 252), ('CsI', 141)):
    rows, zoa, ipot, rho = estar(mid)
    Rof, Sof, Scof, Eof = make_tables(rows)
    print('\n--- %s: Z/A %.4f, I %.0f эВ, ρ %.3f г/см³ ---' % (name, zoa, ipot, rho))
    print('T, кэВ | R_CSDA г/см² | R, мм | S_col кэВ/(г/см²)')
    for T in (30, 59.5, 100, 200, 480, 662, 1000, 1200, 1600, 2000):
        print('%6.1f | %.4e | %6.3f | %.1f' % (T, Rof(T), Rof(T) / rho * 10, Scof(T)))

    print('\n(1) Ландау/Вавилов: электрон T прошёл путь s к грани и вышел с T_exit (по CSDA).')
    print('    ξ — параметр Ландау на пути s; κ = ξ/T_max (T_max = T/2); ширина при κ<1 ~ 4ξ (FWHM Ландау),')
    print('    при κ>1 — σ ≈ sqrt(ξ·T_max·(1−β²/2)); сравнивать с масштабом гладкости континуума (~100 кэВ) и бином 1 кэВ.')
    print('T, кэВ | доля пути | s г/см² | T_exit кэВ | ξ кэВ | κ | FWHM_L=4ξ | σ_gauss кэВ')
    for T in (100, 480, 1000, 1200, 2000):
        for frac in (0.25, 0.5, 0.9):
            s = frac * Rof(T)
            Texit = Eof(Rof(T) - s)
            xi = xi_kev(T, s, zoa)
            Tmax = T / 2.0
            kappa = xi / Tmax
            sg = math.sqrt(max(0.0, xi * Tmax * (1 - beta2(T) / 2)))
            print('%6.0f | %4.2f | %.4f | %8.1f | %6.2f | %6.3f | %7.1f | %7.1f' % (T, frac, s, Texit, xi, kappa, 4 * xi, sg))

    print('\n(2) δ-электроны: доля столкновительной потери первичного электрона T, уходящая в δ выше порога Tc')
    print('    (Мёллер, ведущий член; арбитр по умолчанию рождает δ явно лишь выше 592 кэВ в NaI — cut 0.7 мм).')
    print('T, кэВ | f(Tc=10) | f(30) | f(100) | f(300) | f(592) | R_CSDA(Tc=100) мм | R(300) мм')
    for T in (100, 480, 662, 1000, 1200, 1600, 2000):
        print('%6.0f | %6.3f | %6.3f | %6.3f | %6.3f | %6.3f | %6.3f | %6.3f' % (
            T, delta_fraction(T, 10, ipot), delta_fraction(T, 30, ipot), delta_fraction(T, 100, ipot),
            delta_fraction(T, 300, ipot), delta_fraction(T, 592, ipot), Rof(100) / rho * 10, Rof(300) / rho * 10))

print('\n(3) Обратное рассеяние электрона от обвязки (нормальное падение, толстая мишень; Табата 1971):')
print('    η — доля вернувшихся, <E_b/E> ≈ 0.55…0.65 (Al), ниже у лёгких (~0.5), выше у Cu (~0.7).')
print('    Косое падение поднимает η: при 60° ~ ×1.7…2 (Al). Тонкий слой (t < 0.3 R) — η ниже насыщения.')
print('Z_eff | вещество | η(100 кэВ) | η(480) | η(1000) | η(2000)')
for Z, mat in ((8.4, 'PTFE (C2F4, Z_eff~8.4)'), (10.5, 'MgO (Z_eff~10.5, ρ 0.8 порошок)'), (13, 'Al'), (29, 'Cu'), (26, 'сталь ~Fe')):
    print('%5.1f | %-32s | %.3f | %.3f | %.3f | %.3f' % (Z, mat, eta_thick(Z, 100), eta_thick(Z, 480), eta_thick(Z, 1000), eta_thick(Z, 2000)))

print('\n    Толщина слоёв корпуса в долях пробега электрона (Al ρ 2.7; PTFE 2.2; MgO 0.8):')
rowsAl = None
for T in (480, 1000, 1200, 1600):
    # пробег в лёгких веществах в г/см² близок к воде·1.1 — берём NaI-таблицу ×0.8 как грубую оценку (R_Al ≈ 0.8 R_NaI в г/см²)
    rows, zoa, ipot, rho = estar(252)
    Rof, Sof, Scof, Eof = make_tables(rows)
    R_light = 0.8 * Rof(T)
    print('    T %5d кэВ: R_light ≈ %.3f г/см²; PTFE 1 мм = %.2f R; MgO 2 мм = %.2f R; Al 1 мм = %.2f R; Al 3 мм = %.2f R' % (
        T, R_light, 0.22 / R_light, 0.16 / R_light, 0.27 / R_light, 0.81 / R_light))

print('\n(4) Позитрон: смещение точки аннигиляции. Модель: reach = R·min(1, 0.4·(T−350 кэВ)/1000) изотропно.')
print('    Реальность: средняя глубина проникновения ≈ 0.45…0.55 R (detour) ВПЕРЁД по кванту (угол Цая ~mc²/E).')
print('    Влияние на вылет 511: ΔP/P ≈ μ(511)·Δd; μ(511) NaI 0.33 см⁻¹, CsI 0.40 см⁻¹.')
for name, mid, mu in (('NaI', 252, 0.33), ('CsI', 141, 0.40)):
    rows, zoa, ipot, rho = estar(mid)
    Rof, Sof, Scof, Eof = make_tables(rows)
    print('  %s: T_e+ кэВ | R мм | reach модели мм | реальная глубина ~0.5R мм | Δd мм | μ·Δd (ΔP/P одиночного вылета, первый порядок)' % name)
    for T in (220, 440, 800, 1200, 1600):
        R = Rof(T) / rho * 10
        reach = R * min(1.0, 0.4 * max(0.0, T - 350) / 1000.0)
        real = 0.5 * R
        print('    %5d | %5.2f | %5.2f | %5.2f | %5.2f | %.4f' % (T, R, reach, real, real - reach, mu * (real - reach) / 10))
