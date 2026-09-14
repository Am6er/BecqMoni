# -*- coding: utf-8 -*-
"""П59 (AMBER27): отношения активностей членов рядов из `FsaStackShot --rates=` против ожиданий.

    python handover/p59-amber27/ratios.py <каталог с rates_*.csv> [--out=<csv>]

Имена файлов: rates_<спектр>_<сцена>_<режим>.csv, например rates_radon2_side_noeq.csv,
rates_as80_th_noeq.csv. Скорость счёта `count_rate` компонента — распадов/с (амплитуда ×
образ, эффективность внутри отклика); её σ берётся как count_rate / z (z — амплитуда / σ
амплитуды), корреляции между членами НЕ учитываются — это оценка снизу.

⚠ Член ряда, объявленного `--chain=` без равновесия, стоит в единицах распадов РОДИТЕЛЯ ряда:
линии образа взвешены накопленной долей ветвления (`FsaSampleLibrary`, `line[1] * member.Value`),
у Tl-208 это 0.3594. Поэтому «Tl-208/Bi-212 (FSA)» ожидается 1.005, физическое — ×0.3594.
"""
import csv
import glob
import io
import math
import os
import sys

BR_TL = 0.3594


def load(path):
    meta, comp, lim = {}, {}, {}
    with io.open(path, encoding='utf-8', newline='') as fh:
        for row in csv.DictReader(fh):
            if row['section'] == 'meta':
                meta[row['name']] = float(row['count_rate']) if row['count_rate'] else float('nan')
            elif row['section'] == 'component':
                comp[row['name']] = row
            elif row['section'] == 'limit':
                lim[row['name']] = row
    return meta, comp, lim


def rate(comp, lim, name):
    """(скорость, σ, статус): статус 'det' / 'lim' / 'none'."""
    if name in comp:
        r = float(comp[name]['count_rate'])
        z = float(comp[name]['z']) if comp[name]['z'] else float('nan')
        return r, (r / z if z and z > 0 else float('nan')), 'det'
    if name in lim:
        L = lim[name]
        if L['detected'] == '1' and L['count_rate']:
            r = float(L['count_rate'])
            return r, float('nan'), 'det?'
        dl = float(L['detection_limit_rate']) if L['detection_limit_rate'] else float('nan')
        return dl, float('nan'), 'lim'
    return float('nan'), float('nan'), 'none'


def ratio(a, b):
    r, sr, st = a
    q, sq, st2 = b
    if st != 'det' or st2 != 'det' or not (q > 0):
        return float('nan'), float('nan')
    v = r / q
    rel = math.sqrt((sr / r) ** 2 + (sq / q) ** 2) if r > 0 and not math.isnan(sr) and not math.isnan(sq) else float('nan')
    return v, v * rel


def fmt(a):
    r, s, st = a
    if st == 'det':
        return '%.4g ± %.2g' % (r, s) if not math.isnan(s) else '%.4g' % r
    if st == 'lim':
        return '< %.3g' % r
    if st == 'det?':
        return '%.4g (?)' % r
    return '-'


def fr(v):
    a, s = v
    if math.isnan(a):
        return '-'
    return '%.3f ± %.3f' % (a, s) if not math.isnan(s) else '%.3f' % a


def main():
    d = sys.argv[1]
    out = None
    for a in sys.argv[2:]:
        if a.startswith('--out='):
            out = a[6:]
    files = sorted(glob.glob(os.path.join(d, 'rates_*.csv')))
    runs = {}
    lines = []

    def say(s):
        print(s)
        lines.append(s)

    say('run,chi2ndf,residual_pct,Pb-212,Bi-212,Tl-208,Pb-214,Bi-214,Ra-226,Rn-222,Pb-210,Ac-228,Ra-224,Ra-228,Th-228,K-40,'
        'Bi212/Pb212,Tl208/Bi212(FSA),Tl208/Bi212(phys),Bi214/Pb214')
    for f in files:
        key = os.path.basename(f)[6:-4]
        meta, comp, lim = load(f)
        runs[key] = (meta, comp, lim)
        names = ['Pb-212', 'Bi-212', 'Tl-208', 'Pb-214', 'Bi-214', 'Ra-226', 'Rn-222', 'Pb-210', 'Ac-228',
                 'Ra-224', 'Ra-228', 'Th-228', 'K-40']
        vals = [rate(comp, lim, n) for n in names]
        bp = ratio(vals[1], vals[0])
        tb = ratio(vals[2], vals[1])
        tbp = (tb[0] * BR_TL, tb[1] * BR_TL)
        bipb = ratio(vals[4], vals[3])
        say('%s,%.3f,%.1f,%s,%s,%s,%s,%s' % (
            key, meta.get('chi2ndf', float('nan')), 100.0 * meta.get('model_residual', float('nan')),
            ','.join(fmt(v) for v in vals), fr(bp), fr(tb), fr(tbp), fr(bipb)))

    # Pb-212 между съёмками: спектр1/спектр2 по одной сцене и режиму
    say('')
    say('scene_mode,Pb212_spec1/spec2,Bi212_spec1/spec2,expected_Pb212')
    for key in sorted(runs):
        if not key.startswith('radon1_'):
            continue
        key2 = 'radon2_' + key[7:]
        if key2 not in runs:
            continue
        m1, c1, l1 = runs[key]
        m2, c2, l2 = runs[key2]
        p = ratio(rate(c1, l1, 'Pb-212'), rate(c2, l2, 'Pb-212'))
        b = ratio(rate(c1, l1, 'Bi-212'), rate(c2, l2, 'Bi-212'))
        say('%s,%s,%s,2.329' % (key[7:], fr(p), fr(b)))

    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
