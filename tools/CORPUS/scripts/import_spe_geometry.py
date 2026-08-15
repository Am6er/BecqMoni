# -*- coding: utf-8 -*-
"""Геометрия съёмки из заголовков исходных `.spe` — то, что не доехало в корпус.

Заголовок каждого файла поверки ЛСРМ называет СВОЮ съёмку целиком:

    GEOMETRY=Дента-100
    MATERIAL={"Ro":1,"Compound":[{"1":0.041},{"6":0.39},…],"Name":"ОИСН-10"}
    SAMPLEMASS=100.0;1.0
    SAMPLEVOLUME=100.0;1.0
    DISTANCE=0.0
    DETECTOR=УДС-ГЦ-63х63-USB №0086-16
    CONFIGNAME=Гамма-1С №0221-16

⚠ **В корпус из этого доехали только имя источника и активность.** У рабочих
копий `SampleInfo.Weight` и `Volume` стоят ЗАГЛУШКОЙ 1, вещества нет вовсе — а
геометрии сосудов до сегодня строились с предположением `Silicon dioxide` и
выведенной из объёма высотой слоя. Поэтому таблица собирается ЗДЕСЬ, прямо из
поставки, и поставка при этом только читается.

Ключ корпуса берётся не по имени файла, а по `corpus_def` — там у каждого
спектра записан путь к исходнику, и это единственная связь, которая не
разъедется при переименовании.

Выход: `data/lsrm_spectrum_geometry.csv` — строка на спектр корпуса.

    python tools/CORPUS/scripts/import_spe_geometry.py [--apply]
"""
import argparse
import csv
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(os.path.dirname(HERE), 'data')
sys.path.insert(0, HERE)

import corpus_def                                        # noqa: E402
from corpus_paths import resolve                         # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

FIELDS = ('SHIFR', 'GEOMETRY', 'MATERIAL', 'SAMPLEMASS', 'SAMPLEVOLUME',
          'DISTANCE', 'DETECTOR', 'CONFIGNAME', 'MEASBEGIN')


def header(path):
    """Шапка `.spe` до `$DATA`. Кодировка поставки — cp1251."""
    text = io.open(path, encoding='cp1251', errors='replace').read(20000)
    cut = text.find('$DATA')
    if cut > 0:
        text = text[:cut]
    out = {}
    for line in text.splitlines():
        m = re.match(r'^([A-Z][A-Z0-9]*)=(.*)$', line.strip())
        if m and m.group(1) in FIELDS:
            out.setdefault(m.group(1), m.group(2).strip())
    return out


def first(value):
    """`100.0;1.0` — величина и её погрешность; берём величину."""
    if not value:
        return ''
    head = value.split(';')[0].strip()
    try:
        return float(head)
    except ValueError:
        return ''


#: Где лежат ИСХОДНЫЕ `.spe`. Корпус собран из конвертированных `.xml`
#: (`…\LSRM поверки\Поверка N\сосуд\имя.xml`), а заголовок со съёмкой есть
#: только у `.spe`, и лежат они в ДРУГОМ каталоге поставки — рядом с паспортами
#: и кривыми. Соответствие идёт по хвосту пути «Поверка N\сосуд\имя», а не по
#: одному имени файла: имена в разных поверках повторяются
#: (`Cs137_420-7-14_Маринелли_0cm` есть и в 2016, и в 2024).
SPE_ROOT = os.path.join(r'C:\Users\moroz\YandexDisk\Спектры',
                        'Спектры источники эталоны', 'Spe - поверки')


def spe_of(entry):
    """Исходный `.spe` той же съёмки — рядом с `.xml` либо в каталоге поставки."""
    path = resolve(entry['path'])
    if not path:
        return None

    near = os.path.splitext(path)[0] + '.spe'
    if os.path.isfile(near):
        return near

    parts = path.replace('/', os.sep).split(os.sep)
    for i, part in enumerate(parts):
        if part.startswith(u'Поверка '):
            cand = os.path.join(SPE_ROOT, *parts[i:])
            cand = os.path.splitext(cand)[0] + '.spe'
            if os.path.isfile(cand):
                return cand
            break
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--apply', action='store_true')
    args = ap.parse_args()

    rows, no_spe, no_material = [], [], []
    for entry in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON:
        spe = spe_of(entry)
        if spe is None:
            continue
        head = header(spe)
        if not head.get('GEOMETRY'):
            no_spe.append(entry['key'])
            continue

        material, density, compound = '', '', ''
        raw = head.get('MATERIAL')
        if raw:
            try:
                parsed = json.loads(raw)
                material = parsed.get('Name', '')
                density = parsed.get('Ro', '')
                compound = ' '.join('%s:%s' % (z, f) for pair in parsed.get('Compound', [])
                                    for z, f in pair.items())
            except ValueError:
                pass

        if not material:
            no_material.append(entry['key'])

        mass, volume = first(head.get('SAMPLEMASS')), first(head.get('SAMPLEVOLUME'))
        rows.append(dict(
            спектр=entry['key'], сосуд=head.get('GEOMETRY', ''),
            источник=head.get('SHIFR', ''),
            вещество=material, плотность_паспорт=density,
            масса_г=mass, объём_мл=volume,
            плотность=round(mass / volume, 4) if (mass and volume) else '',
            расстояние_см=first(head.get('DISTANCE')),
            состав_Z_доля=compound,
            детектор=head.get('DETECTOR', ''), конфиг=head.get('CONFIGNAME', ''),
            снято=head.get('MEASBEGIN', ''), файл=os.path.basename(spe)))

    rows.sort(key=lambda r: r['спектр'])
    print('спектров с заголовком `.spe`: %d' % len(rows))
    print('без вещества в заголовке: %d%s'
          % (len(no_material), (' — ' + ', '.join(no_material[:6])) if no_material else ''))

    vessels = {}
    for r in rows:
        vessels.setdefault(r['сосуд'], []).append(r)
    print()
    print('%-16s %-6s %-12s %-22s %s' % ('сосуд', 'спектров', 'вещества', 'плотности', 'расстояние'))
    for vessel, group in sorted(vessels.items()):
        mats = sorted({r['вещество'] for r in group if r['вещество']})
        dens = sorted({r['плотность'] for r in group if r['плотность'] != ''})
        dist = sorted({str(r['расстояние_см']) for r in group})
        print('%-16s %-8d %-12s %-22s %s'
              % (vessel[:16], len(group), ','.join(mats)[:12],
                 ', '.join('%.2f' % d for d in dens)[:22], dist))

    if args.apply and rows:
        path = os.path.join(DATA, 'lsrm_spectrum_geometry.csv')
        os.makedirs(DATA, exist_ok=True)
        with io.open(path, 'w', encoding='utf-8-sig', newline='') as fh:
            w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print()
        print('записано: %s (%d строк)' % (path, len(rows)))
    elif rows:
        print()
        print('(--apply не задан, файл не записан)')


if __name__ == '__main__':
    main()
