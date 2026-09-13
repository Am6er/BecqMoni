# -*- coding: utf-8 -*-
"""bqp15_edge.py — край порога по СЫРЫМ спектрам, по каналам, на прибор. Печать таблицы raw[ch] у низа."""
import sys, io, csv, os, glob
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
DUMP = r'C:\Users\moroz\bqp12_out\a_dump'
keys = sorted(os.path.basename(f)[:-8] for f in glob.glob(os.path.join(DUMP, '*_chi.csv')))
def load(key):
    rows = list(csv.DictReader(io.open(os.path.join(DUMP, key + '_chi.csv'), encoding='utf-8-sig')))
    return (np.array([int(r['raw']) for r in rows]), np.array([float(r['keV']) for r in rows]),
            np.array([float(r['bg']) for r in rows]), np.array([float(r['model']) for r in rows]))
inst = {}
for k in keys:
    inst.setdefault(k.split('_')[0], []).append(k)
RANGES = {'G1S16': range(3, 22), 'G1S24': range(3, 22), 'AS80': range(48, 84, 2), 'ASN16': range(4, 68, 4)}
for det, ks in inst.items():
    rng = list(RANGES[det])
    print('\n=== %s: raw по каналам (первая строка — канал), keV канала по калибровке спектра в скобках у первого и последнего' % det)
    print('%-18s ' % 'спектр' + ' '.join('%7d' % c for c in rng))
    for k in ks:
        raw, kev, bg, model = load(k)
        print('%-18s ' % k[len(det) + 1:] + ' '.join('%7d' % raw[c] for c in rng) + '   (%.1f … %.1f кэВ)' % (kev[rng[0]], kev[rng[-1]]))
    print('первый ненулевой канал:', ', '.join('%s:%d' % (k[len(det) + 1:], int(np.argmax(load(k)[0] > 0))) for k in ks))
    # гейн: кэВ/канал по калибровке в середине шкалы и канал 662 кэВ
    print('кэВ/канал (ch 100→101) и канал, где калибровка даёт 662 кэВ:', ', '.join('%s: %.3f/%d' % (k[len(det) + 1:], load(k)[1][101] - load(k)[1][100], int(np.argmin(np.abs(load(k)[1] - 662.0)))) for k in ks))
