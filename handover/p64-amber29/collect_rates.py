# -*- coding: utf-8 -*-
"""П64 (AMBER29): свод чисел разбора `FsaStackShot --rates=` по 33 съёмкам одного плеча в
одну таблицу + ряды для фита распада + отношение Bi-214/Pb-214.

    python handover/p64-amber29/collect_rates.py <каталог rates_*.csv> <плечо> <timeline.csv> <куда>

Плечо — суффикс имени файла: rates_<ключ съёмки>_<плечо>.csv (например `A_noeq`).
Пишет в <куда>:
  * `activities_<плечо>.csv` — по строке на съёмку: key, t_start_h (от t₀ = 01.11.2025
    12:52:52), t_mid_h, live_s, chi2ndf и по каждому нуклиду `<нуклид>_bq` (0, если предел),
    `_err` (count_rate / z; без корреляций — оценка снизу), `_z`, `_lim` (предел обнаружения,
    Бк), `_det` (1/0), `_tied` (партнёр привязки П60 или «-»);
  * `series_<нуклид>_<плечо>.csv` — ряд e01…e30 (t_mid_h, value, err) для `decay_fit.py`;
  * печатает Bi-214/Pb-214 по каждой съёмке и средневзвешенное по 30 равновесным.

⚠ Единицы: у члена ряда, объявленного `--chain=` без равновесия, count_rate — в распадах
РОДИТЕЛЯ ряда (линии образа взвешены накопленной долей ветвления); для Pb-214/Bi-214 это
Бк с точностью до 0.9998, у Tl-208 — A/0.3594 (П59 §3).
"""
import csv
import glob
import io
import math
import os
import sys
from datetime import datetime

T0 = datetime(2025, 11, 1, 12, 52, 52)
NUCLIDES = ['Pb-214', 'Bi-214', 'Ra-226', 'Rn-222', 'Pb-210', 'Pb-212', 'Bi-212', 'Tl-208', 'Ac-228',
            'Ra-224', 'Th-228', 'Rn-220', 'Th-232', 'Ra-228', 'K-40']
KEYS = ['coal_t20m', 'coal_t2h', 'coal_t3h'] + ['coal_e%02d' % i for i in range(1, 31)]


def load_rates(path):
    meta, comp, lim = {}, {}, {}
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            if row['section'] == 'meta':
                meta[row['name']] = float(row['count_rate']) if row['count_rate'] else float('nan')
            elif row['section'] == 'component':
                comp[row['name']] = row
            elif row['section'] == 'limit':
                lim[row['name']] = row
    return meta, comp, lim


def nuclide_row(comp, lim, name):
    """bq, err, z, lim, det, tied."""
    tied = '-'
    if name in comp:
        c = comp[name]
        r = float(c['count_rate'])
        z = float(c['z']) if c['z'] else float('nan')
        if 'tied_to' in c and c['tied_to']:
            tied = c['tied_to']
        L = lim.get(name)
        dl = float(L['detection_limit_rate']) if L and L['detection_limit_rate'] else float('nan')
        return r, (r / z if z and z > 0 else float('nan')), z, dl, 1, tied
    if name in lim:
        L = lim[name]
        dl = float(L['detection_limit_rate']) if L['detection_limit_rate'] else float('nan')
        return 0.0, float('nan'), 0.0, dl, 0, tied
    return float('nan'), float('nan'), float('nan'), float('nan'), -1, tied


def main():
    rdir, arm, timeline, out = sys.argv[1:5]
    os.makedirs(out, exist_ok=True)
    tl = {}
    with io.open(timeline, encoding='utf-8-sig') as fh:
        for r in csv.DictReader(fh):
            tl[r['key']] = r
    head = ['key', 't_start_h', 't_mid_h', 'live_s', 'chi2ndf']
    for n in NUCLIDES:
        head += [n + '_bq', n + '_err', n + '_z', n + '_lim', n + '_det', n + '_tied']
    table = []
    series = {n: [] for n in NUCLIDES}
    ratios = []
    for k in KEYS:
        path = os.path.join(rdir, 'rates_%s_%s.csv' % (k, arm))
        if not os.path.exists(path):
            print('нет ' + path)
            continue
        meta, comp, lim = load_rates(path)
        st = datetime.fromisoformat(tl[k]['start'])
        live = float(tl[k]['live_s'])
        t_start = (st - T0).total_seconds() / 3600.0
        t_mid = t_start + 0.5 * live / 3600.0
        row = [k, '%.5f' % t_start, '%.5f' % t_mid, '%.3f' % live, '%.4f' % meta.get('chi2ndf', float('nan'))]
        vals = {}
        for n in NUCLIDES:
            bq, err, z, dl, det, tied = nuclide_row(comp, lim, n)
            vals[n] = (bq, err, det)
            row += ['%.6g' % bq, '%.4g' % err, '%.3f' % z, '%.5g' % dl, str(det), tied]
            if k.startswith('coal_e') and det == 1:
                series[n].append((t_mid, bq, err))
        table.append(row)
        pb, bi = vals['Pb-214'], vals['Bi-214']
        if pb[2] == 1 and bi[2] == 1 and pb[0] > 0:
            q = bi[0] / pb[0]
            sq = q * math.sqrt((pb[1] / pb[0]) ** 2 + (bi[1] / bi[0]) ** 2)
            ratios.append((k, q, sq))
    with io.open(os.path.join(out, 'activities_%s.csv' % arm), 'w', encoding='utf-8', newline='') as fh:
        fh.write(','.join(head) + '\n')
        for r in table:
            fh.write(','.join(r) + '\n')
    for n in NUCLIDES:
        if len(series[n]) >= 3:
            with io.open(os.path.join(out, 'series_%s_%s.csv' % (n, arm)), 'w', encoding='utf-8', newline='') as fh:
                fh.write('t_mid_h,value,err\n')
                for t, v, e in series[n]:
                    fh.write('%.5f,%.6g,%.4g\n' % (t, v, e))
    print('плечо %s: съёмок %d; обнаружено (из 33): %s' % (
        arm, len(table), ', '.join('%s %d' % (n, sum(1 for r in table if r[5 + 6 * i + 4] == '1')) for i, n in enumerate(NUCLIDES))))
    print('Bi-214/Pb-214 по съёмкам:')
    eq = []
    for k, q, sq in ratios:
        print('  %-9s %.4f ± %.4f' % (k, q, sq))
        if k.startswith('coal_e'):
            eq.append((q, sq))
    if eq:
        w = [1.0 / s ** 2 for _, s in eq]
        m = sum(q * wi for (q, _), wi in zip(eq, w)) / sum(w)
        sm = math.sqrt(1.0 / sum(w))
        chi2 = sum(((q - m) / s) ** 2 for q, s in eq)
        print('средневзвешенное по %d равновесным: Bi-214/Pb-214 = %.4f ± %.4f (χ²/ndf разброса %.2f/%d)' % (len(eq), m, sm, chi2, len(eq) - 1))
        with io.open(os.path.join(out, 'ratio_BiPb_%s.csv' % arm), 'w', encoding='utf-8', newline='') as fh:
            fh.write('key,ratio,err\n')
            for k, q, sq in ratios:
                fh.write('%s,%.5f,%.5f\n' % (k, q, sq))
            fh.write('#weighted_mean_eq,%.5f,%.5f\n#chi2,%.3f,%d\n' % (m, sm, chi2, len(eq) - 1))


if __name__ == '__main__':
    main()
