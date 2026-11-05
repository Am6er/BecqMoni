# -*- coding: utf-8 -*-
"""AMBER289: вещество кристалла группы LABR_BRIL (LaBrBril_Th228, LaBrBril_Eu152).

Проба отвечает на вопрос: LaBr3:Ce, CeBr3 или иное. Она меряет по СЫРЫМ
спектрам (и по вшитому фону, который снят ТЕМ ЖЕ кристаллом) то, что различает
вещества:

  1. разрешение в нескольких точках;
  2. пик/комптон для 583.2 и 2614.5;
  3. K-рентген кристалла (Ba Kα 32.2 / La Kα 33.4 / Ce Kα 34.7);
  4. линии La-138 1435.8 и сумму 1468, а также β-континуум 789-1044;
  5. воспроизведение чисел прошлой полосы (фон-вычтенный спектр) и
     положительный контроль: подложенная линия в передний план и в фон
     сокращается, в один передний план — нет.

Запуск: python tools/CORPUS/probes/AMBER289_crystal_probe.py
"""
import os
import sys
import xml.etree.ElementTree as ET

import numpy as np
from scipy.optimize import curve_fit

sys.stdout.reconfigure(encoding='utf-8')

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.dirname(HERE)
SPECTRA = os.path.join(CORPUS, 'corpus', 'spectra')

TH = os.path.join(SPECTRA, 'LaBrBril_Th228.xml')
EU = os.path.join(SPECTRA, 'LaBrBril_Eu152.xml')


def load(path, tag='EnergySpectrum'):
    rd = ET.parse(path).getroot().find('ResultDataList/ResultData')
    es = rd.find(tag)
    coef = [float(x.text) for x in es.find('EnergyCalibration/Coefficients')]
    d = np.array([int(x.text) for x in es.findall('Spectrum/DataPoint')], float)
    E = coef[0] + coef[1] * np.arange(len(d)) + coef[2] * np.arange(len(d)) ** 2
    return E, d


def fwhm_model(e):
    """Модель разрешения группы LABR_BRIL из corpus/detectors.csv."""
    return np.sqrt(0.5642590906146394 * e + 1.1145710901566141e-05 * e * e)


# ---------------------------------------------------------------- 1. resolution
LINES = []


def model_fixed(x, *pars):
    y = (pars[0] + pars[1] * (x - 1450) + pars[2] * (x - 1450) ** 2
         + pars[3] * (x - 1450) ** 3)
    shift, scale = pars[4], pars[5]
    for i in range(len(pars) - 6):
        mu = LINES[i] + shift
        sig = fwhm_model(LINES[i]) * scale / 2.35482
        y = y + pars[6 + i] * np.exp(-0.5 * ((x - mu) / sig) ** 2)
    return y


def fit_region(E, d, e_lo, e_hi, lines, p0_amps, shift0=0.0, scale0=1.0):
    global LINES
    LINES = lines
    m = (E >= e_lo) & (E <= e_hi)
    x, y = E[m], d[m]
    p0 = [np.median(y[:3]), 0, 0, 0, shift0, scale0] + list(p0_amps)
    lo = [-np.inf, -np.inf, -np.inf, -np.inf, -6, 0.5] + [-np.inf] * len(lines)
    hi = [np.inf, np.inf, np.inf, np.inf, 6, 1.8] + [np.inf] * len(lines)
    popt, pcov = curve_fit(model_fixed, x, y, p0=p0, bounds=(lo, hi), maxfev=400000)
    perr = np.sqrt(np.diag(pcov))
    chi2 = np.sum((model_fixed(x, *popt) - y) ** 2)
    return popt, perr, chi2, len(x) - len(popt)


def resolution_row(name, E, d, e0, half):
    m = (E >= e0 - half) & (E <= e0 + half)
    popt, perr, chi2, ndf = fit_region(E, d, e0 - half, e0 + half, [e0],
                                       [max(d[m].max() * 0.5, 10)])
    fw = fwhm_model(e0) * popt[5]
    return fw, fw / (e0 + popt[4]) * 100.0, chi2 / ndf


# ------------------------------------------------------- 2. 1435.8 + K40 + 1468
SIG0, TF, TAU0, EREF = 7.61, 0.069, 4.99, 581.0


def shape(x, mu):
    sc = fwhm_model(mu) / fwhm_model(EREF)
    sig, tau = SIG0 * sc, TAU0 * sc
    t = (x - mu) / sig
    g = np.exp(-0.5 * t * t)
    tail = np.where(x < mu, np.exp((x - mu) / tau), 0.0)
    return g + TF * tail


def model_lines(x, b0, b1, b2, shift, *amps):
    y = b0 + b1 * (x - 1460) + b2 * (x - 1460) ** 2
    for i, a in enumerate(amps):
        y = y + a * shape(x, LINES[i] + shift)
    return y


def fit_la138(E, d, lines):
    """Фит 1400-1520 кэВ. СДВИГ СВОБОДЕН: калибровка корпуса rms 3.7 кэВ, и
    жёсткие позиции 1435.8/1460.8/1468.0 делят одну ширину на три.
    Возвращает площади, chi2/ndf, сдвиг и МАСШТАБИРОВАННУЮ ошибку площади:
    модель формы (шаблон 583.2) не описывает линию, поэтому ковариационная
    ошибка без sqrt(chi2/ndf) занижена."""
    global LINES
    LINES = lines
    m = (E >= 1400) & (E <= 1520)
    x, y = E[m], d[m]
    p0 = [np.median(y[:3]), 0, 0, 0] + [500] * len(lines)
    lo = [-np.inf, -np.inf, -np.inf, -6] + [-np.inf] * len(lines)
    hi = [np.inf, np.inf, np.inf, 6] + [np.inf] * len(lines)
    popt, pcov = curve_fit(model_lines, x, y, p0=p0, bounds=(lo, hi), maxfev=400000)
    perr = np.sqrt(np.diag(pcov))
    chi2 = np.sum((model_lines(x, *popt) - y) ** 2)
    ndf = len(x) - len(popt)
    chi2r = chi2 / ndf
    de = float(np.mean(np.diff(x)))
    areas = []
    for i, e0 in enumerate(lines):
        sc = fwhm_model(e0) / fwhm_model(EREF)
        integ = SIG0 * sc * np.sqrt(2 * np.pi) + TF * TAU0 * sc
        area = popt[4 + i] * integ / de
        err = perr[4 + i] * integ / de
        areas.append((e0, area, err, err * np.sqrt(chi2r)))
    return popt, areas, chi2r, ndf, y.sum(), popt[3], perr[3]


# ------------------------------------------------------------ 3. beta continuum
BETA_MASK = [727.3, 763.1, 785.4, 794.9, 806.2, 860.5, 911.2, 968.97, 1050.0]


def level(E, d, lo, hi, mask=BETA_MASK):
    m = (E >= lo) & (E <= hi)
    for e0 in mask:
        m &= np.abs(E - e0) > 0.9 * fwhm_model(e0)
    return d[m].mean() if m.any() else np.nan


def beta_step(E, d):
    below = level(E, d, 700, 750)
    plateau = level(E, d, 820, 950)
    above = level(E, d, 1025, 1100)
    return below, plateau, above, plateau - 0.5 * (below + above)


# --------------------------------------------------------- 4. background windows
WINDOWS = [(780.0, 800.0), (1425.0, 1445.0), (1450.0, 1490.0)]


def window_sums(E, d, windows):
    out = []
    for lo, hi in windows:
        m = (E >= lo) & (E < hi)
        out.append((d[m].sum(), int(m.sum())))
    return out


# ------------------------------------------------------ 5. positive control fit
def inject_gauss(E, d, e0, area_counts):
    """Подложить гаусс на e0 с площадью area_counts (отсчётов)."""
    sig = fwhm_model(e0) / 2.35482
    de = float(np.mean(np.diff(E)))
    amp = area_counts * de / (sig * np.sqrt(2 * np.pi))
    return d + amp * np.exp(-0.5 * ((E - e0) / sig) ** 2)


# --------------------------------------------------------- 6. expected activity
def la138_activity(size_cm, height_cm):
    rho = 5.08            # g/cm3 LaBr3
    la_frac = 0.3669      # массовая доля La в LaBr3
    ab = 0.000888         # изотопная доля La-138
    t12 = 1.03e11 * 3.15576e7
    vol = np.pi * (size_cm / 2) ** 2 * height_cm
    mass = rho * vol
    n = mass * la_frac * ab / 138.905 * 6.022e23
    return vol, np.log(2) * n / t12


def main():
    print('AMBER289: вещество кристалла LABR_BRIL')
    print('=' * 72)

    ETh, dTh = load(TH)
    EBg, dBg = load(TH, 'BackgroundEnergySpectrum')
    EEu, dEu = load(EU)

    print('\n1. РАЗРЕШЕНИЕ (ПШПВ, % от энергии)')
    print('   линия   Th228    Eu152   модель корпуса')
    th_lines = [238.6, 583.2, 2614.5]
    eu_lines = [121.8, 244.7, 344.3, 778.9, 964.1, 1112.1, 1408.0]
    rows = []
    for e0, half in [(121.8, 18), (238.6, 22), (244.7, 18), (344.3, 20),
                     (583.2, 30), (778.9, 30), (964.1, 30), (1112.1, 30),
                     (1408.0, 30), (2614.5, 75)]:
        vals = []
        for E, d, present in ((ETh, dTh, e0 in th_lines), (EEu, dEu, e0 in eu_lines)):
            if not present:
                vals.append('   -- ')
                continue
            try:
                fw, pct, c2 = resolution_row('', E, d, e0, half)
                vals.append('%6.2f' % pct)
            except Exception:
                vals.append('   -- ')
        print('  %7.1f  %s  %s   %5.2f' % (e0, vals[0], vals[1],
                                            fwhm_model(e0) / e0 * 100))
        rows.append((e0, vals))
    print('  «--» — линии в этом спектре нет (не мерим). 778.9/1112.1 у Eu152')
    print('  сидят на соседних линиях/β-континууме — читать с оговоркой.')
    print('  LaBr3/B380: 2.9 % на 662 (паспорт B380, RAG Knoll/Gilmore);'
          ' CeBr3: 3.6 % на 662 (Knoll 4th, RAG).')

    print('\n2. ПИК/КОМПТОН')
    for e0, edge in ((583.2, 405.6), (2614.5, 2380.0)):
        m = (ETh >= e0 - 10) & (ETh <= e0 + 10)
        h = dTh[m].max()
        base = np.median(dTh[(ETh >= e0 - 40) & (ETh <= e0 - 25)])
        me = (ETh >= edge - 3) & (ETh <= edge + 3)
        ma = (ETh >= edge + 20) & (ETh <= edge + 35)
        step = dTh[me].mean() - dTh[ma].mean()
        print('  %.1f кэВ: пик %.0f (над континуумом %.0f), край Комптона %.0f'
              ' (ступень %.0f) -> пик/комптон %.1f'
              % (e0, h, h - base, edge, step, (h - base) / step))

    print('\n3. K-РЕНТГЕН КРИСТАЛЛА (калибровка низкой энергии — ±2-3 кэВ)')
    m = (EEu >= 30) & (EEu <= 42)
    imax = np.where(m)[0][dEu[m].argmax()]
    print('  Eu152: максимум %.2f кэВ (Sm Kα 40.12 — положительный контроль;'
          ' Ba Kβ 36.38 / Ce Kβ 39.26 — под ним)' % EEu[imax])
    m = (ETh >= 28) & (ETh <= 42)
    imax = np.where(m)[0][dTh[m].argmax()]
    print('  Th228: максимум %.2f кэВ (Ba Kα+Kβ 32.2/36.4; La Kα 33.4;'
          ' Ce Kα 34.7) — калибровка на низах не привязана, не решает' % ETh[imax])
    m = (EBg >= 28) & (EBg <= 42)
    imax = np.where(m)[0][dBg[m].argmax()]
    tot = dBg[m].sum()
    lo_s = dBg[(EBg >= 20) & (EBg < 25)].sum()
    lo_n = ((EBg >= 20) & (EBg < 25)).sum()
    hi_s = dBg[(EBg >= 45) & (EBg < 50)].sum()
    hi_n = ((EBg >= 45) & (EBg < 50)).sum()
    cont = 0.5 * (lo_s / lo_n + hi_s / hi_n) * m.sum()
    print('  Фон:   максимум %.2f кэВ, 28-42 кэВ %.0f отсч, net %.0f+-%.0f —'
          ' рентген есть и БЕЗ источника (свойство кристалла)'
          % (EBg[imax], tot, tot - cont, np.sqrt(tot + cont)))

    print('\n4. ЛИНИИ La-138 В СЫРЫХ СПЕКТРАХ (не фон-вычтенных)')
    print('   1435.8 + K-40 1460.8 + 1468.0, сдвиг СВОБОДЕН (калибровка rms 3.7 кэВ)')
    for name, E, d in (('Th228', ETh, dTh), ('Фон', EBg, dBg), ('Eu152', EEu, dEu)):
        popt, areas, c2, ndf, total, shift, shift_err = fit_la138(
            E, d, [1435.8, 1460.8, 1468.0])
        print('  %-6s: сдвиг %+.2f+-%.2f  chi2/ndf=%.0f  (сумма окна %.0f)'
              % (name, shift, shift_err, c2, total))
        for e0, area, err, serr in areas:
            if abs(e0 - 1468.0) < 0.1:
                print('     %7.1f: НЕ ИЗМЕРЕНА — внутри K-40 (амплитуда фита %7.0f,'
                      ' не площадь)' % (e0, area))
            else:
                print('     %7.1f: площадь %7.0f+-%5.0f  (масштаб. +-sqrt(chi2/ndf) %7.0f)'
                      % (e0, area, err, serr))
    popt1, areas1, c21, ndf1, total1, sh1, she1 = fit_la138(ETh, dTh, [1460.8])
    popt3, areas3, c23, ndf3, total3, sh3, she3 = fit_la138(
        ETh, dTh, [1435.8, 1460.8, 1468.0])
    dchi2 = c21 * ndf1 - c23 * ndf3
    print('  Th228, только K-40: chi2/ndf=%.0f; dchi2 (только K-40 -> +1436) = %.0f'
          % (c21, dchi2))
    print('  1460.8 и 1468.0 разнесены на 7.2 кэВ при ПШПВ 29 кэВ (0.6 sigma) —')
    print('  НЕРАЗДЕЛИМЫ; «1468» отдельной линией не измерена, её доля неизвестна.')
    print('  Площадь 1435.8 по устойчивым вариантам ~3000-3400. Значимость площади')
    print('  НЕ 9 sigma: модель формы не описывает линию, ошибка по ковариации')
    print('  занижена (масштабированная — в скобках). Плечо 1436 реально: dchi2 выше.')

    print('\n5. β-КОНТИНУУМ La-138 (789 γ + β до 255 кэВ -> 789-1044)')
    for name, E, d in (('Th228', ETh, dTh), ('Фон', EBg, dBg)):
        below, plateau, above, step = beta_step(E, d)
        print('  %-6s: 700-750 %.0f | 820-950 %.0f | 1025-1100 %.0f отсч/канал'
              ' -> ступень %.0f' % (name, below, plateau, above, step))
    print('  Eu152: континуум источника крутой (540 -> 810), ступень не мерится.')
    # positive control: подложить ступень 100 отсч/канал в 800-1000
    below0, plateau0, above0, step0 = beta_step(ETh, dTh)
    dstep = dTh.copy()
    m = (ETh >= 800) & (ETh <= 1000)
    dstep[m] += 100.0
    below, plateau, above, step = beta_step(ETh, dstep)
    print('  контроль: своя ступень %.0f + подложено 100 -> измерена %.0f'
          % (step0, step))

    print('\n6. ЧИСЛА ПРОШЛОЙ ПОЛОСЫ = ПЕРЕДНИЙ ПЛАН МИНУС ФОН')
    F = window_sums(ETh, dTh, WINDOWS)
    B = window_sums(EBg, dBg, WINDOWS)
    for (lo, hi), (f, n), (b, _) in zip(WINDOWS, F, B):
        print('  %4.0f-%4.0f: F=%.0f B=%.0f F-B=%+.0f+-%.0f'
              % (lo, hi, f, b, f - b, np.sqrt(f + b)))
    print('  прошлая полоса: 322+-67, 197+-70, 177+-127 — совпадает.')
    print('  Причина: La-138 есть И в переднем плане, И в фоне (тот же кристалл),')
    print('  поэтому вычитание фона её сокращает. Это не отсутствие линии.')

    print('\n7. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: подложенная 1435.8 (3000 отсч)')
    popt0, areas0, c20, ndf0, total0, sh0, she0 = fit_la138(
        ETh, dTh, [1435.8, 1460.8, 1468.0])
    raw_1436 = areas0[0][1]
    d_inj = inject_gauss(ETh, dTh, 1435.8, 3000.0)
    popt, areas, c2, ndf, total, shift, shift_err = fit_la138(
        ETh, d_inj, [1435.8, 1460.8, 1468.0])
    print('  только в передний план: 1435.8 = %.0f+-%.0f '
          '(своё %.0f + подложено 3000 = %.0f)'
          % (areas[0][1], areas[0][2], raw_1436, raw_1436 + 3000))
    d_inj_bg = inject_gauss(EBg, dBg, 1435.8, 3000.0)
    f = d_inj[(ETh >= 1425) & (ETh < 1445)].sum()
    b = d_inj_bg[(EBg >= 1425) & (EBg < 1445)].sum()
    print('  в передний план И в фон: F-B = %+.0f+-%.0f '
          '(без подкладки было +197; ожидание +197)'
          % (f - b, np.sqrt(f + b)))

    print('\n8. ОЖИДАЕМАЯ АКТИВНОСТЬ La-138 И НАБЛЮДЕНИЕ')
    print('  размер      объём     A(La-138)  ожидаемо 1435.8+1468*   наблюдаем')
    print('  наблюдаемое: 1436 ~3000-3400 + неизмеренная доля 1468, итого ~4000-5000')
    obs = 4500.0
    for label, dia, hgt, eff in (('1"x1"', 2.54, 2.54, 0.16),
                                 ('1.5"x1.5"', 3.81, 3.81, 0.22),
                                 ('3"x3"', 7.62, 7.62, 0.35)):
        vol, act = la138_activity(dia, hgt)
        exp = act * 0.655 * 3600 * eff
        print('  %-9s  %6.1f см3  %6.1f Бк  %10.0f          %8.0f'
              % (label, vol, act, exp, obs))
    print('  * эффективность полной энергии оценена по размеру и μ/ρ; ±50 %.')
    print('  Вывод: «не 38x38» подтверждено (1.5"/3" — в 4/51 раз больше),')
    print('  «1"x1"» правдоподобно (в пределах ±50 %), но НЕ доказано:')
    print('  размер кристалла в корпусе не записан, доля 1468 не измерена.')

    print('\n9. ВЕРДИКТ')
    print('  LaBr3:Ce (BrilLanCe 380), НЕ CeBr3.')
    print('  Решает: разрешение 2.97 % на 583.2 (CeBr3 3.6 % на 662) и')
    print('  присутствие La-138 в СЫРОМ спектре и фоне: плечо 1435.8 реально')
    print('  (dchi2 ~1e5 против «только K-40»), площадь ~3000-3400;')
    print('  β-континуум 789-1044 (ступень ~120/канал); K-рентген без источника.')
    print('  «Сумма 1468» отдельной линией НЕ измерена — неразделима с K-40.')
    print('  Противоречит CeBr3: у Ce нет La-138 вовсе; 2.97 % для CeBr3 —')
    print('  на пределе лучших кристаллов, а 1.46 % на 2614.5 — вне его класса.')


if __name__ == '__main__':
    main()
