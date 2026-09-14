# -*- coding: utf-8 -*-
"""Разбор файлов эффективности и градуировочных спектров LSRM.

Три формата, все — INI в кодировке cp1251, секции в квадратных скобках:

  *.efr  «сырой»: одна секция на ИСТОЧНИК. Активность источника, и под ней
         строки измеренных линий. Помеченные `, No` в фит не пошли.
  *.efa  «сводный»: одна секция на ГЕОМЕТРИЮ. Все линии всех источников вместе
         плюс подогнанная кривая — по зонам, ортогональными полиномами.
  *.etl  библиотека самих градуировочных спектров: активность, живое время,
         калибровка, отсчёты пробы (Data) и фона (FData).

Строка линии: `E=eps,unc%,нуклид,площадь,dплощадь,выход%[, No]`.

Кривая (проверено по точкам, см. --check):

    lg eps = SUM_k  Curve_N[k] * P_k(x),   x = lg(E/кэВ)

где P_k — полиномы из `Curve_N_k`, коэффициенты записаны от старшей степени.
`Zone_N = степень, x_min, x_max, погрешность КРИВОЙ (в единицах lg)`.

Последнее число — не разброс точек, а погрешность самой кривой: во всех четырёх
файлах оно примерно равно СКО остатков, умноженному на √(p/n), где p — число
коэффициентов зоны, n — число точек. Это та величина, которая идёт в
погрешность активности; разброс точек вдвое больше и виден по --check.

    python lsrm_eff.py --check <файлы .efa>
    python lsrm_eff.py --points <файлы .efa|.efr> [--out points.csv]
    python lsrm_eff.py --etl <файл .etl> [--export <секция> <spectrum.csv>]
"""
import argparse
import io
import re
import sys


def read(path):
    return io.open(path, 'rb').read().decode('cp1251')


def sections(text):
    """[(заголовок, {ключ: значение}, [сырые строки без '='])]"""
    out = []
    head, kv, plain = None, None, None
    for line in text.splitlines():
        line = line.strip()
        if line.startswith('[') and line.endswith(']'):
            if head is not None:
                out.append((head, kv, plain))
            head, kv, plain = line[1:-1], {}, []
            continue
        if head is None or not line:
            continue
        eq = line.find('=')
        if eq <= 0:
            continue
        key, value = line[:eq], line[eq + 1:]
        # линия спектра: ключ — число (энергия)
        try:
            float(key)
            plain.append((float(key), value))
        except ValueError:
            kv[key] = value
    if head is not None:
        out.append((head, kv, plain))
    return out


def lines_of(section):
    """Измеренные линии: (E, eps, unc%, нуклид, площадь, dплощадь, выход%, принята)."""
    out = []
    for energy, value in section[2]:
        parts = [p.strip() for p in value.split(',')]
        if len(parts) < 3:
            continue
        used = not (parts[-1].lower() == 'no')
        try:
            eps = float(parts[0])
            unc = float(parts[1])
        except ValueError:
            continue
        nuclide = parts[2] if len(parts) > 2 else ''
        area = float(parts[3]) if len(parts) > 4 else 0.0
        darea = float(parts[4]) if len(parts) > 5 else 0.0
        yield_pct = float(parts[5]) if len(parts) > 6 else 0.0
        out.append((energy, eps, unc, nuclide, area, darea, yield_pct, used))
    return out


def nums(value):
    return [float(v) for v in value.replace(' ', '').split(',') if v]


def curve_zones(kv):
    """[(степень, x_min, x_max, СКО, [базисные полиномы], [коэффициенты])]"""
    zones = []
    count = int(kv.get('Zones', '0'))
    for i in range(1, count + 1):
        head = nums(kv['Zone_%d' % i])
        degree = int(head[0])
        basis = [nums(kv['Curve_%d_%d' % (i, k)]) for k in range(1, degree + 2)]
        coefficients = nums(kv['Curve_%d' % i])
        zones.append((degree, head[1], head[2], head[3], basis, coefficients))
    return zones


def poly(coefficients, x):
    """Коэффициенты записаны от СТАРШЕЙ степени, схема Горнера."""
    value = 0.0
    for c in coefficients:
        value = value * x + c
    return value


def efficiency(zones, energy_kev):
    """Эффективность по кривой. Если зон несколько — берётся накрывающая."""
    import math
    x = math.log10(energy_kev)
    # Зоны перекрываются; в перекрытии берём ту, к чьей середине точка ближе,
    # иначе низ второй зоны считался бы полиномом, подогнанным по верху.
    covering = [z for z in zones if z[1] <= x <= z[2]]
    pool = covering if covering else zones
    chosen = min(pool, key=lambda z: abs(x - 0.5 * (z[1] + z[2])))
    value = sum(c * poly(p, x) for c, p in zip(chosen[5], chosen[4]))
    return 10.0 ** value


def cmd_check(paths):
    """Сверка расшифровки: кривая против точек, по которым она построена."""
    import math
    worst = 0.0
    for path in paths:
        text = read(path)
        for section in sections(text):
            kv = section[1]
            if 'Zones' not in kv:
                continue
            zones = curve_zones(kv)
            print('=== %s' % section[0])
            print('    зон %d, погрешность кривой %s' % (len(zones), ', '.join(
                '%.3f lg (%.1f %%)' % (z[3], 100.0 * (10.0 ** z[3] - 1.0)) for z in zones)))
            print('    %9s %12s %12s %8s %7s' % ('E, кэВ', 'точка', 'кривая', 'отн.', '± %'))
            deviations = []
            for energy, eps, unc, nuclide, _a, _d, _y, used in lines_of(section):
                model = efficiency(zones, energy)
                ratio = model / eps
                deviations.append(abs(math.log10(ratio)))
                print('    %9.3f %12.4E %12.4E %8.3f %7.2f%s'
                      % (energy, eps, model, ratio, unc, '' if used else '  (не в фите)'))
            if deviations:
                rms = math.sqrt(sum(d * d for d in deviations) / len(deviations))
                worst = max(worst, rms)
                print('    разброс точек вокруг кривой: %.4f lg = %.1f %% '
                      '(погрешность кривой в файле %.4f lg, отношение %.2f)'
                      % (rms, 100.0 * (10.0 ** rms - 1.0), zones[0][3],
                         rms / zones[0][3] if zones[0][3] else 0.0))
            print('')
    return worst


def cmd_points(paths, out):
    rows = ['file,section,geometry,E_keV,eps,unc_pct,nuclide,area,area_unc,yield_pct,used']
    import os
    for path in paths:
        text = read(path)
        for section in sections(text):
            geometry = section[1].get('Geometry', '')
            for energy, eps, unc, nuclide, area, darea, yield_pct, used in lines_of(section):
                rows.append('%s,%s,%s,%.3f,%.6E,%.3f,%s,%.3f,%.3f,%.4f,%d'
                            % (os.path.basename(path), section[0].replace(',', ';'),
                               geometry, energy, eps, unc, nuclide, area, darea,
                               yield_pct, 1 if used else 0))
    text = '\n'.join(rows)
    if out:
        io.open(out, 'w', encoding='utf-8').write(text + '\n')
        print('записано: %s (%d строк)' % (out, len(rows) - 1))
    else:
        print(text)


def cmd_etl(path, export):
    text = read(path)
    for section in sections(text):
        kv = section[1]
        nuclide = kv.get('Nuclid', '')
        activity = kv.get(nuclide, '')
        print('%-58s %-10s %-16s live %8s с, фон %8s с'
              % (kv.get('Geometry', ''), nuclide, activity,
                 kv.get('LiveTime', ''), kv.get('FonTime', '')))
        if export and export[0] in section[0]:
            data = nums(kv['Data'])
            fon = nums(kv['FData']) if 'FData' in kv else []
            energy = nums(kv['Energy'])[1:]
            rows = ['channel,E_keV,counts,background']
            for i, c in enumerate(data):
                e = poly(list(reversed(energy)), i)
                rows.append('%d,%.4f,%.0f,%.0f' % (i, e, c, fon[i] if i < len(fon) else 0))
            io.open(export[1], 'w', encoding='utf-8').write('\n'.join(rows) + '\n')
            print('  -> %s (%d каналов)' % (export[1], len(data)))


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--check', nargs='+')
    p.add_argument('--points', nargs='+')
    p.add_argument('--etl')
    p.add_argument('--export', nargs=2)
    p.add_argument('--out')
    a = p.parse_args()
    if a.check:
        cmd_check(a.check)
    elif a.points:
        cmd_points(a.points, a.out)
    elif a.etl:
        cmd_etl(a.etl, a.export)
    else:
        p.print_help()
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
