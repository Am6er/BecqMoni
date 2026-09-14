# -*- coding: utf-8 -*-
"""П67 (AMBER22): состав стекла диска «ThO₂ по активности + Ba/La по плотности» как функция плотности.

Правило (одна правка меняет всё):
  * силикатная матрица — лёгкие оксиды РФА (таблица Amber 14.09.2026, П56 `atten.py`), нормированные;
    её собственная плотность взята 2.45 (правило аддитивности объёмов на стекле 73/14/9/3/1 даёт 2.318 при
    табличных 2.40–2.50 — П56 §3; константы лёгких оксидов масштабированы так, чтобы матрица давала 2.45);
  * ThO₂ — по активности: A(Th-232) = 854 Бк (FSA rev22, П56) → m(Th) = 854 / 4.06e3 Бк/г = 0.2103 г →
    m(ThO₂) = 0.2103 · 264.04/232.04 = 0.2393 г; массовая доля = m(ThO₂) / (ρ · V), V = объём кабошона;
  * тяжёлая часть — BaO и La₂O₃ ПОРОВНУ по массе (K-рентген обоих в спектре, `A280`), её доля — из правила
    аддитивности объёмов под заданную ρ;
  * U (0.016 %) и Ra-226 массы не несут (П56: −0.0 %).

Параметры, которые заменяются замером Amber: V_CM3 (объём кабошона; по взвешиванию в воде), RHO (плотность).
"""
import math
import sqlite3
import sys

import numpy as np

DB = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\matdb.sqlite'
NA = 6.02214076e23
_con = sqlite3.connect('file:' + DB + '?mode=ro', uri=True)
_cur = _con.cursor()
AW = {z: a for z, a in _cur.execute('select z, atomic_weight from xcom_elements')}
XS = {}
for z, e, coh, inc, ph, pn, pe in _cur.execute('select z, energy_ev, coherent_b, incoherent_b, photoelectric_b, pair_nuclear_b, pair_electron_b from xcom_cross_sections order by z, energy_ev'):
    XS.setdefault(z, []).append((e, coh, inc, ph, pn, pe))

A_TH = 854.0            # Бк, FSA rev22 (П56)
SPEC_TH232 = 4.06e3     # Бк/г
M_THO2 = A_TH / SPEC_TH232 * 264.04 / 232.04   # г
V_CM3 = 5.03            # объём кабошона «кромка 3 + купол 2» (П56 §4; решение распорядителя 14.09.2026: весов нет, держать 5.0) — параметр

OX = {'SiO2': (14, 1, 2), 'Na2O': (11, 2, 1), 'Al2O3': (13, 2, 3), 'CaO': (20, 1, 1), 'Fe2O3': (26, 2, 3), 'MgO': (12, 1, 1),
      'K2O': (19, 2, 1), 'TiO2': (22, 1, 2), 'P2O5': (15, 2, 5), 'ThO2': (90, 1, 2), 'UO2': (92, 1, 2),
      'B2O3': (5, 2, 3), 'La2O3': (57, 2, 3), 'BaO': (56, 1, 1), 'PbO': (82, 1, 1), 'MnO': (25, 1, 1)}
RHO_OX = {'SiO2': 2.2, 'Na2O': 2.27, 'Al2O3': 3.95, 'CaO': 3.34, 'Fe2O3': 5.24, 'MgO': 3.58, 'K2O': 2.35, 'TiO2': 4.23, 'P2O5': 2.39,
          'ThO2': 10.0, 'UO2': 10.97, 'B2O3': 2.46, 'La2O3': 6.51, 'BaO': 5.72, 'PbO': 9.53, 'MnO': 5.43}
# лёгкие оксиды по таблице РФА (элемент % → оксид %), П56 atten.py
LIGHT = {'SiO2': 23.7645 * 60.08 / 28.09, 'Na2O': 2.5137 * 61.98 / 45.98, 'Al2O3': 0.7218 * 101.96 / 53.96, 'CaO': 0.6645 * 56.08 / 40.08,
         'Fe2O3': 0.4606 * 159.69 / 111.69, 'MgO': 0.2812 * 40.30 / 24.31, 'K2O': 0.0433 * 94.2 / 78.2, 'TiO2': 0.0832 * 79.87 / 47.87,
         'P2O5': 0.1032 * 141.94 / 61.95, 'MnO': 0.0204 * 70.94 / 54.94}
RHO_MATRIX = 2.45       # собственная плотность силикатной матрицы (калибровка правила по стеклу плоскому)


def _light_norm():
    s = sum(LIGHT.values())
    return {k: v / s for k, v in LIGHT.items()}


def matrix_rho_rule():
    """плотность матрицы по правилу объёмов с табличными константами (для масштабирования)"""
    ln = _light_norm()
    return 1.0 / sum(w / RHO_OX[k] for k, w in ln.items())


def composition(rho, v_cm3=V_CM3, ba_la_split=0.5):
    """массовые доли оксидов при плотности rho: матрица + ThO₂ (по активности) + BaO/La₂O₃ (по плотности)"""
    w_th = M_THO2 / (rho * v_cm3)
    # 1/ρ = w_m/ρ_m + w_th/ρ_th + w_h/ρ_h ; w_m = 1 − w_th − w_h ; ρ_h — BaO/La₂O₃ по долям
    inv_h = ba_la_split / RHO_OX['BaO'] + (1.0 - ba_la_split) / RHO_OX['La2O3']
    inv_m = 1.0 / RHO_MATRIX
    inv_th = 1.0 / RHO_OX['ThO2']
    # 1/ρ = (1 − w_th − w_h)·inv_m + w_th·inv_th + w_h·inv_h
    w_h = (1.0 / rho - (1.0 - w_th) * inv_m - w_th * inv_th) / (inv_h - inv_m)
    if w_h < 0.0:
        raise ValueError('плотность %.3f ниже матрицы: тяжёлых < 0' % rho)
    w_m = 1.0 - w_th - w_h
    ox = {k: v * w_m for k, v in _light_norm().items()}
    ox['ThO2'] = w_th
    ox['BaO'] = w_h * ba_la_split
    ox['La2O3'] = w_h * (1.0 - ba_la_split)
    return ox


def oxide_fractions(oxides):
    fr = {}
    tot = sum(oxides.values())
    for ox, w in oxides.items():
        z, n, m = OX[ox]
        mw = n * AW[z] + m * AW[8]
        fr[z] = fr.get(z, 0.0) + w / tot * n * AW[z] / mw
        fr[8] = fr.get(8, 0.0) + w / tot * m * AW[8] / mw
    return fr


def mu_rho(z, E_keV, coherent=False):
    rows = XS[z]
    e = np.array([r[0] for r in rows]) / 1000.0
    tot = np.array([(r[1] if coherent else 0.0) + r[2] + r[3] + r[4] + r[5] for r in rows])
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


STEEL = {26: 1.0}                      # сталь оправы ≈ железо, 7.87
CELLULOSE = {6: 0.4444, 1: 0.0622, 8: 0.4934}   # (C6H10O5)n
RHO_STEEL = 7.87
RHO_PAPER = 0.7

# П13 «Ториевое стекло» (контроль)
P13 = {5: 0.05404, 8: 0.232722, 20: 0.042882, 56: 0.271383, 57: 0.258363, 90: 0.14061}
RHO_P13 = 4.345


def describe(rho, v_cm3=V_CM3):
    ox = composition(rho, v_cm3)
    fr = oxide_fractions(ox)
    lines = ['ρ = %.3f, V = %.2f см³, масса стекла %.2f г, ThO₂ %.3f г' % (rho, v_cm3, rho * v_cm3, M_THO2)]
    lines.append('  оксиды, % массы: ' + ', '.join('%s %.2f' % (k, 100 * v) for k, v in sorted(ox.items(), key=lambda kv: -kv[1])))
    lines.append('  элементы, доли: ' + ', '.join('%d:%.6f' % (z, w) for z, w in sorted(fr.items())))
    for E in (238.6, 583.2, 911.2, 2614.5):
        lines.append('  μ/ρ(%.1f) = %.4f см²/г, μ = %.4f 1/см' % (E, mix_mu_rho(fr, E), mix_mu_rho(fr, E) * rho))
    return '\n'.join(lines)


if __name__ == '__main__':
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    print('правило объёмов на матрице (табличные константы): %.3f; принято %.2f' % (matrix_rho_rule(), RHO_MATRIX))
    print('m(ThO₂) по активности %.0f Бк: %.4f г' % (A_TH, M_THO2))
    for rho in (2.8, 3.3, 3.8, 4.2):
        print(describe(rho))
    fr = P13
    print('П13 4.345: ' + ', '.join('μ(%.0f)=%.4f' % (E, mix_mu_rho(fr, E) * RHO_P13) for E in (238.6, 583.2, 911.2, 2614.5)))
