#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Карта «спектр -> состав библиотеки» для харнесса pie.

Набор нуклидов выбирает оператор под задачу, а не универсальный список из
пятнадцати компонентов: на ториевом электроде Eu-152 и I-131 в библиотеке не
появляются, потому что искать их там незачем. Правило — по классу пробы:

* всегда — NORM: Th-232, Ra-226, U-238, U-235, K-40. Природные цепочки есть в
  любой пробе и в любой комнате, оператор их не отключает;
* плюс нуклид источника, если этот источник действительно ставили под детектор
  (колонка `nuclides` манифеста): оператор знает, что кладёт;
* мешающие образы (ХРИ W и Pb, пики вылета, обратное рассеяние) — не нуклиды и
  не выбираются: это часть модели отклика, они добавляются всегда самим
  харнессом.

Фантомы при таком наборе остаются измеримыми — но уже не «Ba-133 подобрал всё
подряд», а настоящие вырожденности внутри правдоподобного семейства: U-235
185.7 против Ra-226 186.2, обратное рассеяние 662 против U-235.

    python tools/pie/make_sets.py [--out tools/pie/component_map.csv]
"""
import argparse
import csv
import os

HERE = os.path.dirname(os.path.abspath(__file__))
MANIFEST = os.path.join(HERE, '..', 'CORPUS', 'corpus', 'manifest.csv')

NORM = ['Th-232', 'Ra-226', 'U-238', 'U-235', 'K-40']

# манифест -> компонент харнесса; None — компонента в библиотеке нет вовсе
NUCLIDE_MAP = {
    '40K': 'K-40', '137CS': 'Cs-137', '241AM': 'Am-241', '60CO': 'Co-60',
    '131I': 'I-131', '152EU': 'Eu-152', '133BA': 'Ba-133', '176LU': 'Lu-176',
    '88Y': None, '139CE': None,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(HERE, 'component_map.csv'))
    args = ap.parse_args()

    rows = []
    unknown = set()
    with open(MANIFEST, encoding='utf-8-sig') as fh:
        for row in csv.DictReader(fh):
            comps = list(NORM)
            for nu in (row['nuclides'] or '').split(';'):
                nu = nu.strip()
                if not nu:
                    continue
                if nu not in NUCLIDE_MAP:
                    unknown.add(nu)
                    continue
                comp = NUCLIDE_MAP[nu]
                if comp and comp not in comps:
                    comps.append(comp)
            # разделитель — точка с запятой, чтобы поле не пришлось закавычивать:
            # харнесс режет строку по ПЕРВОЙ запятой и не тащит парсер CSV
            rows.append((row['key'], ';'.join(comps)))

    if unknown:
        print('манифест: нуклиды без компонента: %s' % ', '.join(sorted(unknown)))

    with open(args.out, 'w', encoding='utf-8', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(['spectrum', 'components'])
        w.writerows(rows)
    print('%d спектров -> %s' % (len(rows), args.out))


if __name__ == '__main__':
    main()
