# -*- coding: utf-8 -*-
# (`AMBER272`, полоса `dbdata2`, 02.11.2026) Оснастка подгонки кривой света GAGG — журнал
# `handover/handover-2026-11-02-amber274-dbdata2.md` §4. Рабочий каталог (выводы проб, якобианы) — переменная
# окружения A272_WORK; сцены GAGG — A272_WORK/scenes; данные Kole — tools/nucdb/data/photon_light/.
# Пункт 3: коэффициенты света пика по узлам кривой (LightFitP200 --jac) для двух сцен GAGG.
# Поток историй задан энергией узла (ResetStream), раскладка энергий по процессам результата не меняет.
import os as _os
WORK = _os.environ.get('A272_WORK', '.').replace('\\', '/').rstrip('/') + '/'
HERE = _os.path.dirname(_os.path.abspath(__file__))
TREE = _os.path.dirname(_os.path.dirname(_os.path.dirname(HERE)))
import os
import subprocess
import sys

P = _os.path.join(sys.argv[2], 'LightFitP200.exe')   # каталог собранных проб
S = WORK + 'scenes'
O = WORK + 'jac'
N = int(sys.argv[1]) if len(sys.argv) > 1 else 200000
os.makedirs(O, exist_ok=True)
npr = [10, 10.5, 11, 12, 13, 14, 15, 16, 18, 20, 22, 25, 28, 30, 33, 36, 40, 43, 46, 48, 50, 50.2, 50.4, 50.9,
       51.8, 53.5, 55, 60, 70, 81, 100, 122, 150, 200, 250, 300, 356, 450, 550, 661.657]
kole = [25.7, 30.7, 35.7, 40.7, 42.7, 43.7, 44.7, 45.7, 46.7, 47.7, 48.7, 49.7, 50.7, 51.7, 52.7, 55.7, 60.7,
        65.7, 70.7, 75.7]
jobs = []


def split(name, scene, energies, k):
    # раскладка по убыванию цены (число узлов растёт с энергией): змейкой
    order = sorted(energies, reverse=True)
    parts = [[] for _ in range(k)]
    for i, e in enumerate(order):
        r = i % (2 * k)
        parts[r if r < k else 2 * k - 1 - r].append(e)
    for i, part in enumerate(parts):
        if part:
            jobs.append(('%s_%d' % (name, i), scene, sorted(part)))


split('npr', 'GAGG_38x38_point5.in', npr, 4)
split('kole', 'GAGG_6x6x15_point5.in', kole, 2)
procs = []
for name, scene, part in jobs:
    out = open(os.path.join(O, name + '.txt'), 'w')
    args = [P, '--geometry=' + os.path.join(S, scene), '--n=%d' % N, '--energies=' + ','.join('%g' % e for e in part),
            '--jac=' + os.path.join(O, name + '.csv')]
    procs.append((name, subprocess.Popen(args, stdout=out, stderr=subprocess.STDOUT, cwd=os.path.dirname(P)), out))
for name, p, out in procs:
    rc = p.wait()
    out.close()
    print(name, 'rc', rc, flush=True)
