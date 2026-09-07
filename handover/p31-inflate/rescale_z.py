#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""СНЯТЬ `inflate` С z, НЕ ТРОГАЯ КОД — оснастка полосы П31 (`A281`).

В `FsaAnalyzer.FitOnce` погрешность строится как
`sigma[k] = sqrt(inv(Gram)[k,k]) * inflate`, `inflate = sqrt(max(1, chi2/ndf))`,
и `z[k] = x[k]/sigma[k]`. Множитель входит В ЗНАМЕНАТЕЛЬ ЛИНЕЙНО и ОДИН И ТОТ ЖЕ
у всех компонентов спектра. Значит z без него равен ровно `z * inflate`, и
опыт «а что, если убрать» ставится ПЕРЕСЧЁТОМ КОЛОНКИ, без единой правки кода и
без переобъявления базы.

⛔ Годится это ТОЛЬКО при `--refit-z=0`: при живом пороге модели компонент с
   малым z удаляется и переподгоняется ДО записи, и в csv его уже нет — умножать
   было бы нечего, а вывод «фантомов нет» оказался бы следствием отсева.

⛔ Чего опыт НЕ даёт: настоящая уборка `inflate` изменила бы и сам отсев (порог
   модели пропускал бы больше), то есть подгонку. Здесь мерится ровно вопрос
   «кто пересекает порог», а не «какой станет модель».

  python handover/p31-inflate/rescale_z.py --src=<прогон> --dst=<копия> [--mode=off|on]
"""

import argparse
import csv
import glob
import io
import math
import os
import shutil
import sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', required=True)
    ap.add_argument('--dst', required=True)
    a = ap.parse_args()

    if os.path.isdir(a.dst):
        shutil.rmtree(a.dst)
    shutil.copytree(a.src, a.dst)

    inflate = {}
    for p in glob.glob(os.path.join(a.dst, '*_runs.csv')):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig')):
            try:
                inflate[r['spectrum']] = math.sqrt(max(1.0, float(r['chi2ndf'])))
            except Exception:
                inflate[r['spectrum']] = 1.0

    touched = 0
    for p in glob.glob(os.path.join(a.dst, '*_components.csv')):
        rows = list(csv.DictReader(io.open(p, encoding='utf-8-sig')))
        if not rows:
            continue
        head = list(rows[0].keys())
        with io.open(p, 'w', encoding='utf-8', newline='') as f:
            w = csv.DictWriter(f, fieldnames=head, lineterminator='\n')
            w.writeheader()
            for r in rows:
                k = inflate.get(r['spectrum'], 1.0)
                try:
                    r['z'] = '%.2f' % (float(r['z']) * k)
                    touched += 1
                except Exception:
                    pass
                w.writerow(r)
    print(u'спектров с множителем: %d, строк состава пересчитано: %d'
          % (len(inflate), touched))
    print(u'множитель: мин %.4f, медиана %.4f, макс %.4f'
          % (min(inflate.values()),
             sorted(inflate.values())[len(inflate) // 2],
             max(inflate.values())))
    return 0


if __name__ == '__main__':
    sys.exit(main())
