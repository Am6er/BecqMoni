# -*- coding: utf-8 -*-
"""Ввоз паспортов эталонов и аттестованных кривых ЛСРМ в таблицы корпуса.

Что это за данные и почему они важны. До 16.08.2026 у сосудов поверки ЛСРМ в
корпусе стояло ПРЕДПОЛОЖЕНИЕ: вещество пробы `Silicon dioxide`, высота слоя
выведена из объёма, — и так и записано в поле `Assumed` каждой геометрии. На
самом деле всё названо в поставке самой ЛСРМ, и лежало рядом:

* `Паспорт эталонов\\*.src` — INI: сосуд, объём, МАССА набивки, вещество
  (ОИСН-06/10/16, РИСН-379), ТОЛЩИНА слоя, дата аттестации и активности
  нуклидов по каждому эталону;
* `Эффективность\\*.efa` — шапка с той же геометрией плюс СОСТАВ вещества
  массовыми долями по Z (`Material={"Compound":[{"1":0.022},…]}`), плотность,
  обвязка сосуда (`ContLaiers`) и расстояние; дальше — измеренные точки
  эффективности с погрешностью и нуклидом-источником;
* `Эффективность\\*.efr` — та же кривая в виде подгонки.

⚠ Оказалось, что вещество эталонов — НЕ песок: ОИСН-16 это 71.4 % железа по
массе при ρ = 1.6 г/см³. Самопоглощение у него и у SiO₂ разное в разы, а
геометрии корпуса до сегодня считались с SiO₂.

Файлы поставки читаются ТОЛЬКО НА ЧТЕНИЕ. Пишем свои таблицы:

    data/lsrm_standards.csv   строка на эталон: сосуд, объём, масса, вещество,
                              плотность, толщина, дата
    data/lsrm_materials.csv   строка на вещество: Z и массовая доля
    data/lsrm_eff_points.csv  измеренные точки кривых: геометрия, энергия,
                              эффективность, погрешность, нуклид

Запуск:  python tools/CORPUS/scripts/import_lsrm_passports.py [--apply]
"""
import argparse
import configparser
import csv
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.path.dirname(HERE), 'data')
SRC = r'C:\Users\moroz\YandexDisk\Спектры\Спектры источники эталоны'

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def num(text):
    """Число из поставки: разделитель дробной части у них запятая."""
    if text is None:
        return None
    text = text.strip().replace(',', '.')
    if not text:
        return None
    try:
        return float(text)
    except ValueError:
        return None


def read_ini(path):
    cp = configparser.RawConfigParser(strict=False)
    cp.read_string(io.open(path, encoding='cp1251', errors='replace').read())
    return cp


def passports():
    """Строка на эталон из всех `.src`."""
    rows = []
    folder = os.path.join(SRC, 'Паспорт эталонов')
    for name in sorted(os.listdir(folder)):
        if not name.lower().endswith('.src'):
            continue
        cp = read_ini(os.path.join(folder, name))
        for sec in cp.sections():
            if sec in ('General', 'Sets') or ',' in sec:
                continue
            d = {k.lower(): v for k, v in cp.items(sec)}
            geometry = (d.get('geometry') or '').strip()
            if not geometry:
                continue
            mass = num(d.get('mass,g'))
            volume = num(d.get('volume,ml'))
            # Плотность набивки — из массы и объёма, а не из воздуха. У
            # точечных источников и того, и другого ноль: у них вещества нет.
            density = (mass / volume) if (mass and volume) else None
            rows.append(dict(
                файл=name, эталон=sec, сосуд=geometry,
                объём_мл=volume if volume is not None else '',
                масса_г=mass if mass is not None else '',
                плотность=round(density, 4) if density else '',
                вещество=(d.get('material') or '').strip(),
                толщина_мм=num(d.get('thick,mm')) or '',
                дата=(d.get('date') or '').strip()))
    return rows


def efficiency():
    """Шапки и точки кривых из всех `.efa`/`.efr`."""
    heads, points, materials = [], [], {}
    folder = os.path.join(SRC, 'Эффективность')
    for name in sorted(os.listdir(folder)):
        path = os.path.join(folder, name)
        if not os.path.isfile(path):
            continue
        text = io.open(path, encoding='cp1251', errors='replace').read()
        head = {}
        for line in text.splitlines():
            m = re.match(r'^([A-Za-z][\w,./]*)=(.*)$', line.strip())
            if m:
                head.setdefault(m.group(1), m.group(2).strip())
            # Точка кривой: `238.632=6.48E-02,3.613,Th-232,…`
            m = re.match(r'^([0-9]+(?:\.[0-9]+)?)=([0-9.eE+-]+),([0-9.]+),([^,]+)', line.strip())
            if m:
                points.append(dict(
                    файл=name, геометрия=head.get('Geometry', ''),
                    энергия_кэВ=float(m.group(1)), эффективность=float(m.group(2)),
                    погрешность_проц=float(m.group(3)), нуклид=m.group(4).strip()))

        if not head.get('Geometry'):
            continue

        material = {}
        raw = head.get('Material')
        if raw:
            try:
                material = json.loads(raw)
            except ValueError:
                material = {}

        if material.get('Name'):
            materials[material['Name']] = material

        heads.append(dict(
            файл=name, детектор=head.get('Detector', ''),
            геометрия=head.get('Geometry', ''),
            объём_мл=head.get('Volume,ml', ''),
            вещество=material.get('Name', ''),
            плотность=head.get('Density,g/cm3', ''),
            толщина_мм=head.get('Thick,mm', ''),
            расстояние_см=head.get('Distance,cm', ''),
            обвязка=head.get('ContLaiers', '')[:200],
            дата=head.get('Date', '')))
    return heads, points, materials


def write(path, rows, apply_it):
    if not rows:
        return
    print('  %-28s %4d строк%s' % (os.path.basename(path), len(rows),
                                   '' if apply_it else '  (--apply не задан)'))
    if not apply_it:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--apply', action='store_true')
    args = ap.parse_args()

    if not os.path.isdir(SRC):
        sys.exit('нет каталога поставки: %s' % SRC)

    std = passports()
    heads, points, materials = efficiency()

    mat_rows = []
    for name, material in sorted(materials.items()):
        for pair in material.get('Compound', []):
            for z, frac in pair.items():
                mat_rows.append(dict(вещество=name, Z=int(z), доля=frac,
                                     плотность=material.get('Ro', '')))

    print('ввоз паспортов и кривых ЛСРМ:')
    write(os.path.join(DATA, 'lsrm_standards.csv'), std, args.apply)
    write(os.path.join(DATA, 'lsrm_geometries.csv'), heads, args.apply)
    write(os.path.join(DATA, 'lsrm_materials.csv'), mat_rows, args.apply)
    write(os.path.join(DATA, 'lsrm_eff_points.csv'), points, args.apply)

    print()
    print('сосуды паспортов: %s' % ', '.join(sorted({r['сосуд'] for r in std})))
    print('вещества с составом: %s' % ', '.join(sorted(materials)))
    for name, material in sorted(materials.items()):
        z = {int(k): v for pair in material.get('Compound', []) for k, v in pair.items()}
        top = sorted(z.items(), key=lambda kv: -kv[1])[:4]
        print('   %-10s ρ=%-5s  %s' % (name, material.get('Ro', '?'),
                                       ', '.join('Z=%d %.3f' % t for t in top)))


if __name__ == '__main__':
    main()
