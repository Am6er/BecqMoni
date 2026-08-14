# -*- coding: utf-8 -*-
"""Сверка НАШИХ кривых эффективности понятной части (B4, физика 11) с
АТТЕСТАЦИЕЙ ЛСРМ того же экземпляра NaI 63x63 (E2/X2, 14.08.2026).

Аттестация — data/eff_curve_g1s.csv: 95 точек, 5 геометрий, вне корпусных
измерений (прислана Verter73, см. README «Внешняя кривая эффективности»).
Наши кривые — узлы <Efficiency> корпусных спектров, посчитанные CorpusEffProbe
при закрытии B4 (34 точки 40–3000 кэВ, 200 тыс. историй на узел, абсолют).

Правила честности:
  * сопоставляются только пары, где НАЧИНКА сосуда совпадает: у одного сосуда
    с разной начинкой разная плотность, а самопоглощение от неё зависит (B1);
    точечные геометрии совпадают целиком — плотности там нет;
  * за краями нашей сетки не экстраполируем (урок external_eff_check.py:
    один шумовой наклон раздул разброс набора с 0.5 до 31);
  * интерполяция лог-лог, как всюду в кривых.

Запуск: python tools/CORPUS/scripts/check_attested_eff.py
"""
import csv
import math
import os
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
ATTESTED = os.path.join(ROOT, 'data', 'eff_curve_g1s.csv')
SPECTRA = os.path.join(ROOT, 'corpus', 'spectra')

# (геометрия аттестации, нуклид) -> спектр корпуса, несущий нашу кривую той же
# постановки. Точечным начинка не нужна — годится любой нуклид.
PAIRS = {
    ('Дента-120мл', 'Th-232'): 'G1S24_Th232_Denta120_2',
    ('Дента-120мл', 'Ra-226'): 'G1S24_Ra226_Denta120',
    ('Дента-120мл', 'K-40'): 'G1S24_K40_Denta120',
    ('Петри-60', 'Th-232'): 'G1S24_Th232_Petri_2',
    ('Петри-60', 'Ra-226'): 'G1S24_Ra226_Petri',
    ('Маринелли', 'Th-232'): 'G1S24_Th232_Mar_2',
}
POINT = {
    'Точечная-5см': 'G1S16_Eu152_P5',
    'Точечная-25см': 'G1S16_Eu152_P25',
}


def load_curve(spectrum):
    path = os.path.join(SPECTRA, spectrum + '.xml')
    if not os.path.exists(path):
        raise SystemExit('нет спектра ' + path)
    tree = ET.parse(path)
    node = tree.getroot().find('.//Efficiency/Curve')
    if node is None:
        raise SystemExit('в ' + spectrum + ' нет узла Efficiency/Curve')
    points = []
    for p in node.findall('ROIEfficiencyData'):
        e = float(p.findtext('Energy'))
        eps = float(p.findtext('Efficiency'))
        err = float(p.findtext('ErrorPercent'))
        if e > 0.0 and eps > 0.0:
            points.append((e, eps, err))
    points.sort()
    return points


def interp(points, energy):
    """Лог-лог интерполяция; None за краями сетки."""
    if energy < points[0][0] or energy > points[-1][0]:
        return None, None
    for i in range(1, len(points)):
        if energy <= points[i][0]:
            e0, v0, u0 = points[i - 1]
            e1, v1, u1 = points[i]
            t = (math.log(energy) - math.log(e0)) / (math.log(e1) - math.log(e0))
            eps = math.exp(math.log(v0) * (1 - t) + math.log(v1) * t)
            err = u0 * (1 - t) + u1 * t
            return eps, err
    return None, None


def main():
    rows = list(csv.DictReader(open(ATTESTED, encoding='utf-8-sig')))
    curves = {}
    matched, skipped = [], {}
    for r in rows:
        geometry = r['geometry']
        nuclide = r['nuclide']
        spectrum = POINT.get(geometry) or PAIRS.get((geometry, nuclide))
        if spectrum is None:
            skipped[(geometry, nuclide)] = skipped.get((geometry, nuclide), 0) + 1
            continue
        if spectrum not in curves:
            curves[spectrum] = load_curve(spectrum)
        energy = float(r['E_keV'])
        attested = float(r['eps'])
        u_att = float(r['u_pct'])
        ours, u_ours = interp(curves[spectrum], energy)
        if ours is None:
            skipped[(geometry, 'вне сетки')] = skipped.get((geometry, 'вне сетки'), 0) + 1
            continue
        matched.append({
            'geometry': geometry, 'nuclide': nuclide, 'E': energy,
            'ratio': ours / attested,
            # суммарная погрешность пары: аттестация + наша статистика МК
            'u_pct': math.sqrt(u_att * u_att + (u_ours or 0.0) ** 2),
        })

    print('сопоставлено %d точек из %d; наши кривые — физика 11, прогон B4 12.08.2026'
          % (len(matched), len(rows)))
    print()
    print('%-14s %6s %8s %8s %8s   %s' % ('геометрия', 'точек', 'медиана',
                                          'мин', 'макс', 'наш/аттестация по энергии'))
    order = ['Точечная-5см', 'Точечная-25см', 'Дента-120мл', 'Петри-60', 'Маринелли']
    for geometry in order:
        of = [m for m in matched if m['geometry'] == geometry]
        if not of:
            continue
        of.sort(key=lambda m: m['E'])
        ratios = sorted(m['ratio'] for m in of)
        median = ratios[len(ratios) // 2]
        print('%-14s %6d %8.3f %8.3f %8.3f' % (geometry, len(of), median,
                                               ratios[0], ratios[-1]))
        for m in of:
            sigmas = (abs(m['ratio'] - 1.0) * 100.0 / m['u_pct']) if m['u_pct'] > 0 else 0.0
            print('    %8.1f кэВ %-8s %6.3f  ±%4.1f %%  %5.1f σ' %
                  (m['E'], m['nuclide'], m['ratio'], m['u_pct'], sigmas))

    if skipped:
        print()
        print('пропущено (начинка не совпадает или за сеткой):')
        for k, v in sorted(skipped.items()):
            print('   %-30s %d' % (' / '.join(k), v))


if __name__ == '__main__':
    sys.exit(main())
