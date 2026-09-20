# -*- coding: utf-8 -*-
"""П84 (AMBER42 п.3): таблица четырёх плеч из логов SumPeakProbe — модель (подслой сумм-пиков целиком,
плечо 3 = ключ ВЫКЛ, плечо 4 = ВКЛ) против данных N_Σ по полной площади (sumfit.py П71: 2915 / 5310).
Запуск: python arms.py > arms.md"""
import io, re, sys, math
LOGS = 'D:/BqMoni_Claude/p84/logs'
NSIG = {'G1S16_Co60_P5': (2915.0, math.hypot(90.0, 80.0)), 'G1S24_Co60_P5': (5310.0, math.hypot(130.0, 140.0))}
def read(p):
    return io.open(p, encoding='utf-8', errors='replace').read().splitlines()
def parse(name, tag):
    d = {}
    lines = read('%s/sp_%s_%s.log' % (LOGS, name, tag))
    arm = None
    for l in lines:
        m = re.match(r'^матрица: .*клеймо (\S+)', l)
        if m: d['stamp'] = m.group(1)
        m = re.match(r'^\s+ε_p\(E1\) = ([\d.]+), ε_p\(E2\) = ([\d.]+);.*κ\(E1,E2\) = ([\d.]+)', l)
        if m: d['eps1'], d['eps2'], d['kappa'] = float(m.group(1)), float(m.group(2)), float(m.group(3))
        m = re.match(r'^\s+сумматор \((.*?)\):.*площадь ([\dE.+-]+)/распад.*?(множитель ([\d.]+))?$', l)
        if m: d['area_' + ('on' if 'ВКЛ' in m.group(1) else 'off')] = float(m.group(2)); 
        if m and m.group(4): d['W'] = float(m.group(4))
        m = re.match(r'^\s+(\+сумм-пики \(изотропно\)|\+угловые корреляции)\s+([\d.]+)\s+(\d+)\s+([\d.]+)\s+(\d+)\s+([\d.]+)\s+(\d+)\s+([\d.]+)\s+(-?\d+)\s+(\d+)\s+([\d.]+)\s+([\d.-]+)', l)
        if m:
            arm = 'off' if 'изотропно' in m.group(1) else 'on'
            d['chi2_' + arm] = float(m.group(2)); d['afit_' + arm] = float(m.group(4)); d['m1n1_' + arm] = float(m.group(6)); d['m2n2_' + arm] = float(m.group(8))
            d['sub_' + arm] = float(m.group(10)); d['p49_' + arm] = float(m.group(12))
        m = re.match(r'^\s+по паспорту \(полные\):.*матрица набл/паспорт: E1 ([\d.]+), E2 ([\d.]+)', l)
        if m: d['mp1'], d['mp2'] = float(m.group(1)), float(m.group(2))
    return d
print('| плечо склада | спектр | клеймо матрицы | ε_p 1173 / 1332 | κ | матрица/паспорт 1173 / 1332 | A_fit/пасп ВЫКЛ→ВКЛ | подслой Σ ВЫКЛ / ВКЛ | N_Σ данных | **M_Σ/N_Σ ВЫКЛ** | **M_Σ/N_Σ ВКЛ** | ВКЛ/ВЫКЛ | χ²/ndf ВЫКЛ→ВКЛ | мерка П49 (окно) ВЫКЛ→ВКЛ |')
print('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
for tag, label in (('a', 'живой `G1S_point5` (5 см)'), ('d7', '`G1S_point5_d7` (5.7 см)')):
    for name in ('G1S16_Co60_P5', 'G1S24_Co60_P5'):
        d = parse(name, tag); n, dn = NSIG[name]
        rel = dn / n
        roff, ron = d['sub_off'] / n, d['sub_on'] / n
        err = lambda r: r * math.hypot(rel, 0.02)
        print('| %s | %s | `%s…` | %.5f / %.5f | %.4f | %.3f / %.3f | %.3f → %.3f | %.0f / %.0f | %.0f ± %.0f | **%.3f ± %.3f** | **%.3f ± %.3f** | %.4f | %.3f → %.3f | %.3f → %.3f |'
              % (label, name, d['stamp'][:16], d['eps1'], d['eps2'], d['kappa'], d['mp1'], d['mp2'], d['afit_off'], d['afit_on'],
                 d['sub_off'], d['sub_on'], n, dn, roff, err(roff), ron, err(ron), d['sub_on'] / d['sub_off'], d['chi2_off'], d['chi2_on'], d['p49_off'], d['p49_on']))
print()
print('Погрешность M_Σ/N_Σ — N_Σ (стат ⊕ подложка: %.1f %% / %.1f %%) ⊕ 2 %% шума κ матрицы. Площадь сумм-пика на распад у сумматора: '
      % (100 * NSIG['G1S16_Co60_P5'][1] / 2915, 100 * NSIG['G1S24_Co60_P5'][1] / 5310), end='')
for tag in ('a', 'd7'):
    d = parse('G1S16_Co60_P5', tag)
    print('%s — ВЫКЛ %.4e, ВКЛ %.4e (×%.4f, множитель пробы %.4f); ' % (tag, d['area_off'], d['area_on'], d['area_on'] / d['area_off'], d['W']), end='')
print()
