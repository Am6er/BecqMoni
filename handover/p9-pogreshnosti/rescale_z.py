# -*- coding: utf-8 -*-
u"""СНЯТЬ `inflate` С КОЛОНКИ z, не трогая кода (`A281`, вариант И2).

    python handover/p9-pogreshnosti/rescale_z.py <откуда> <куда>

`FsaAnalyzer.FitOnce` строит `sigma[k] = sqrt(inv(Gram)[k,k]) * inflate`, где
`inflate = sqrt(max(1, chi2ndf))` ОДИН НА ВЕСЬ СПЕКТР. Значит z без множителя
равен ровно `z * inflate`, и опыт «а что если убрать» ставится пересчётом
колонки, без единой правки приложения и без переобъявления базы.

⛔ ЧЕГО ОПЫТ НЕ ДАЁТ, и это надо помнить: настоящая уборка множителя изменила бы
   и отсев по значимости (`RefitZ`), то есть саму подгонку. Здесь мерится
   вопрос «кто пересекает порог», а не «какой станет модель».

⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ печатается: `inflate`, зажатый единицей, обязан
   оставить z без изменения; если таких спектров нет вовсе — множитель работает
   у всех, и это тоже число.
"""
import csv
import io
import math
import os
import shutil
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8')
except AttributeError:
    pass


def main():
    src, dst = sys.argv[1], sys.argv[2]
    if os.path.exists(dst):
        shutil.rmtree(dst)
    shutil.copytree(src, dst)

    infl = {}
    for n in sorted(os.listdir(dst)):
        if not n.endswith('_runs.csv'):
            continue
        with io.open(os.path.join(dst, n), encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                try:
                    infl[r['spectrum']] = math.sqrt(max(1.0, float(r['chi2ndf'])))
                except (ValueError, KeyError):
                    pass

    ones = sum(1 for v in infl.values() if v <= 1.0 + 1e-12)
    vals = sorted(infl.values())
    print(u'спектров с числом %d; inflate медиана %.3f, максимум %.3f,'
          u' зажат единицей %d'
          % (len(vals), vals[len(vals) // 2] if vals else 0.0,
             vals[-1] if vals else 0.0, ones))

    touched = 0
    for n in sorted(os.listdir(dst)):
        if not n.endswith('_components.csv'):
            continue
        p = os.path.join(dst, n)
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            rows = list(csv.DictReader(f))
            head = list(rows[0].keys()) if rows else []
        for r in rows:
            k = infl.get(r['spectrum'])
            if k is None or not r.get('z'):
                continue
            try:
                r['z'] = '%.4f' % (float(r['z']) * k)
                touched += 1
            except ValueError:
                pass
        with io.open(p, 'w', encoding='utf-8-sig', newline='') as f:
            w = csv.DictWriter(f, fieldnames=head, lineterminator='\n')
            w.writeheader()
            w.writerows(rows)
    print(u'колонка z пересчитана у %d строк; каталог %s' % (touched, dst))


if __name__ == '__main__':
    main()
