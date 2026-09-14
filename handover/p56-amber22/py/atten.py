# -*- coding: utf-8 -*-
"""П56, п. 3: μ/ρ смесей по XCOM (matdb.sqlite, read-only, барны → см²/г по правилу Брэгга; без когерентного —
как при переносе в EfficiencySimulator для ослабления; печатаются оба) и пропускание диска 5 мм лицом / 40 мм ребром
(среднее по равномерно распределённому источнику: T = (1 − e^(−μd))/(μd)) на 238 / 583 / 911 / 2614 кэВ;
четыре состава + плотность по объёмным долям (для «РФА»)."""
import math
import sqlite3
import sys

import numpy as np

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
DB = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\matdb.sqlite'
NA = 6.02214076e23
con = sqlite3.connect('file:' + DB + '?mode=ro', uri=True)
cur = con.cursor()
AW = {z: a for z, a in cur.execute('select z, atomic_weight from xcom_elements')}
XS = {}
for z, e, coh, inc, ph, pn, pe in cur.execute('select z, energy_ev, coherent_b, incoherent_b, photoelectric_b, pair_nuclear_b, pair_electron_b from xcom_cross_sections order by z, energy_ev'):
    XS.setdefault(z, []).append((e, coh, inc, ph, pn, pe))


def mu_rho(z, E_keV, coherent=False):
    """см²/г элемента z на E; лог-лог интерполяция по сетке XCOM (края — ближайшая точка сетки со стороны E)"""
    rows = XS[z]
    e = np.array([r[0] for r in rows]) / 1000.0
    tot = np.array([(r[1] if coherent else 0.0) + r[2] + r[3] + r[4] + r[5] for r in rows])
    # у краёв поглощения в сетке две точки одной энергии: берём по индексу первой ≥ E
    i = int(np.searchsorted(e, E_keV))
    i = min(max(i, 1), len(e) - 1)
    e0, e1, t0, t1 = e[i - 1], e[i], tot[i - 1], tot[i]
    if e1 == e0:
        b = t1
    else:
        b = math.exp(math.log(t0) + (math.log(t1) - math.log(t0)) * (math.log(E_keV) - math.log(e0)) / (math.log(e1) - math.log(e0)))
    return b * 1e-24 * NA / AW[z]


def mix_mu_rho(fractions, E, coherent=False):
    return sum(w * mu_rho(z, E, coherent) for z, w in fractions.items())


def oxide_fractions(oxides):
    """оксиды (% массы) → массовые доли элементов"""
    OX = {'SiO2': (14, 1, 2), 'Na2O': (11, 2, 1), 'Al2O3': (13, 2, 3), 'CaO': (20, 1, 1), 'Fe2O3': (26, 2, 3), 'MgO': (12, 1, 1),
          'K2O': (19, 2, 1), 'TiO2': (22, 1, 2), 'P2O5': (15, 2, 5), 'ThO2': (90, 1, 2), 'UO2': (92, 1, 2), 'U3O8': (92, 3, 8),
          'B2O3': (5, 2, 3), 'La2O3': (57, 2, 3), 'BaO': (56, 1, 1), 'PbO': (82, 1, 1), 'MnO': (25, 1, 1), 'SO3': (16, 1, 3)}
    fr = {}
    tot = sum(oxides.values())
    for ox, w in oxides.items():
        z, n, m = OX[ox]
        mw = n * AW[z] + m * AW[8]
        fr[z] = fr.get(z, 0.0) + w / tot * n * AW[z] / mw
        fr[8] = fr.get(8, 0.0) + w / tot * m * AW[8] / mw
    return fr


def density_by_volume(oxides, rho):
    """плотность по аддитивности объёмов: 1/ρ = Σ w_i/ρ_i (то же правило, что у П13)"""
    tot = sum(oxides.values())
    return 1.0 / sum(w / tot / rho[ox] for ox, w in oxides.items())


RHO_OX = {'SiO2': 2.2, 'Na2O': 2.27, 'Al2O3': 3.95, 'CaO': 3.34, 'Fe2O3': 5.24, 'MgO': 3.58, 'K2O': 2.35, 'TiO2': 4.23, 'P2O5': 2.39,
          'ThO2': 10.0, 'UO2': 10.97, 'U3O8': 8.3, 'B2O3': 2.46, 'La2O3': 6.51, 'BaO': 5.72, 'PbO': 9.53, 'MnO': 5.43, 'SO3': 1.92}
E_LIST = [238.6, 583.2, 911.2, 2614.5]


def transmission(mu, d):
    x = mu * d
    return (1 - math.exp(-x)) / x if x > 1e-9 else 1.0


def report(name, fractions, rho, note=''):
    print('== %s  ρ = %.3f г/см³  %s' % (name, rho, note))
    print('   %-8s %10s %10s | %9s %9s | %9s %9s' % ('кэВ', 'μ/ρ без ког', 'μ/ρ с ког', 'T(5 мм)', 'T(40 мм)', 'e^-μ·5мм', 'e^-μ·40мм'))
    out = {}
    for E in E_LIST:
        m = mix_mu_rho(fractions, E)
        mc = mix_mu_rho(fractions, E, True)
        mu = m * rho
        t5, t40 = transmission(mu, 0.5), transmission(mu, 4.0)
        out[E] = (m, t5, t40)
        print('   %-8.1f %10.4f %10.4f | %9.4f %9.4f | %9.4f %9.4f' % (E, m, mc, t5, t40, math.exp(-mu * 0.5), math.exp(-mu * 4.0)))
    r5 = out[238.6][1] / out[2614.5][1]
    r40 = out[238.6][2] / out[2614.5][2]
    print('   отношение пропусканий 238 : 2614 — лицом (5 мм) %.4f, ребром (40 мм) %.4f' % (r5, r40))
    return out


if __name__ == '__main__':
    results = {}
    # 1. «Ториевое стекло» П13 — доли элементов из засева (B, O, Ca, Ba, La, Th), 4.345
    p13 = {5: 0.05404, 8: 0.232722, 20: 0.042882, 56: 0.271383, 57: 0.258363, 90: 0.14061}
    results['P13'] = report('«Ториевое стекло» П13 (B₂O₃–La₂O₃–ThO₂–BaO–CaO, ThO₂ 16 %)', p13, 4.345)
    # 2. то же + уран по замеру: верхний предел 0.15 % U (как UO2 0.17 %), и «след» 0.016 %
    for wU, lab in ((0.0015, 'U 0.15 % (верхний предел по 1001)'), (0.00016, 'U 0.016 % (равновесно с Ra-226 55 Бк)')):
        f = {z: w * (1 - wU) for z, w in p13.items()}
        f[92] = f.get(92, 0.0) + wU
        results['P13+' + lab] = report('П13 + ' + lab, f, 4.345)
    # 3. «урановое стекло»: натрий-кальций-силикатное (SiO2 73, Na2O 14, CaO 9, MgO 3, Al2O3 1) 2.5 + U 1 % (UO2)
    ug = oxide_fractions({'SiO2': 73, 'Na2O': 14, 'CaO': 9, 'MgO': 3, 'Al2O3': 1})
    ugu = {z: w * 0.99 for z, w in ug.items()}
    ugu[92] = 0.01 * 238.03 / 270.03 + 0.0
    ugu[8] = ugu[8] + 0.01 * 32.0 / 270.03
    results['UG'] = report('«урановое стекло» натрий-кальций-силикатное 2.5 + UO₂ 1 %', ugu, 2.5)
    # 4. «РФА» (Amber 14.09.2026): силикатная матрица по таблице + ThO2 по разности 35 / 40 / 45 %, + U 0.016 %
    light = {'SiO2': 23.7645 * 60.08 / 28.09, 'Na2O': 2.5137 * 61.98 / 45.98, 'Al2O3': 0.7218 * 101.96 / 53.96, 'CaO': 0.6645 * 56.08 / 40.08,
             'Fe2O3': 0.4606 * 159.69 / 111.69, 'MgO': 0.2812 * 40.30 / 24.31, 'K2O': 0.0433 * 94.2 / 78.2, 'TiO2': 0.0832 * 79.87 / 47.87,
             'P2O5': 0.1032 * 141.94 / 61.95, 'MnO': 0.0204 * 70.94 / 54.94}
    print('\nлёгкие оксиды по РФА, % массы: ' + ', '.join('%s %.2f' % (k, v) for k, v in light.items()) + '; сумма %.2f' % sum(light.values()))
    for th in (35.0, 40.0, 45.0):
        ox = dict(light)
        scale = (100.0 - th) / sum(light.values())
        ox = {k: v * scale for k, v in ox.items()}
        ox['ThO2'] = th
        rho = density_by_volume(ox, RHO_OX)
        fr = oxide_fractions(ox)
        fr = {z: w * (1 - 0.00016) for z, w in fr.items()}
        fr[92] = 0.00016
        results['XRF%d' % int(th)] = report('«РФА»: силикат + ThO₂ %.0f %% + U 0.016 %%' % th, fr, rho, '(плотность по объёмным долям, ThO₂ 10.0, силикат ~2.2–2.5)')
    # 5. гамма-оценка: силикат + ThO2 0.8 % (Th 0.7 %, 700 Бк) — если ряд равновесен
    ox = dict(light)
    scale = (100.0 - 0.8) / sum(light.values())
    ox = {k: v * scale for k, v in ox.items()}
    ox['ThO2'] = 0.8
    rho = density_by_volume(ox, RHO_OX)
    fr = oxide_fractions(ox)
    results['gamma'] = report('«гамма»: силикат + ThO₂ 0.8 % (Th-232 ≈ 700 Бк при равновесии ряда)', fr, rho)
    # сводка по отношению 238 : 2614
    print('\nСводка: отношение пропусканий 238 : 2614 (лицом 5 мм) и его сдвиг против П13')
    base = results['P13'][238.6][1] / results['P13'][2614.5][1]
    for k, v in results.items():
        r = v[238.6][1] / v[2614.5][1]
        print('   %-42s %.4f  (%+.1f %% против П13)' % (k, r, 100 * (r / base - 1)))
