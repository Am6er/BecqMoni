# -*- coding: utf-8 -*-
# (`AMBER272`, полоса `dbdata2`, 02.11.2026) Оснастка подгонки кривой света GAGG — журнал
# `handover/handover-2026-11-02-amber274-dbdata2.md` §4. Рабочий каталог (выводы проб, якобианы) — переменная
# окружения A272_WORK; сцены GAGG — A272_WORK/scenes; данные Kole — tools/nucdb/data/photon_light/.
# Пункт 3: фотонная кривая GAGG для базы — LightScaleProbe --n=4000000 на 46 узлах dbdata, сцена GAGG 38×38,
# умолчания склада. Поток историй задан энергией узла (ResetStream) — раскладка по процессам результата не меняет.
import os as _os
WORK = _os.environ.get('A272_WORK', '.').replace('\\', '/').rstrip('/') + '/'
HERE = _os.path.dirname(_os.path.abspath(__file__))
TREE = _os.path.dirname(_os.path.dirname(_os.path.dirname(HERE)))
import os
import subprocess
import sys

BUILD = sys.argv[1]
OUT = sys.argv[2]
K = int(sys.argv[3]) if len(sys.argv) > 3 else 8
N = int(sys.argv[4]) if len(sys.argv) > 4 else 4000000
SCENE = WORK + 'scenes/GAGG_38x38_point5.in'
P = os.path.join(BUILD, 'LightScaleProbe.exe')
E = [10, 10.3, 10.4, 10.5, 10.7, 11, 11.5, 12, 13, 14, 15, 16, 18, 20, 22, 25, 28, 30, 33, 36, 40, 43, 46, 48, 50,
     50.2, 50.3, 50.4, 50.6, 50.9, 51.3, 51.8, 52.5, 53.5, 55, 60, 81, 100, 122, 200, 356, 661.657, 1000, 1332.5,
     1764.5, 2614]
os.makedirs(OUT, exist_ok=True)
order = sorted(E, reverse=True)
parts = [[] for _ in range(K)]
for i, e in enumerate(order):
    r = i % (2 * K)
    parts[r if r < K else 2 * K - 1 - r].append(e)
procs = []
for i, part in enumerate(parts):
    out = open(os.path.join(OUT, 'part_%02d.txt' % i), 'w')
    args = [P, '--geometry=' + SCENE, '--n=%d' % N, '--energies=' + ','.join('%g' % e for e in sorted(part))]
    procs.append((i, subprocess.Popen(args, stdout=out, stderr=subprocess.STDOUT, cwd=BUILD), out))
rc_all = 0
for i, p, out in procs:
    rc = p.wait()
    out.close()
    rc_all |= rc
    print('part_%02d rc %d' % (i, rc), flush=True)
sys.exit(rc_all)
