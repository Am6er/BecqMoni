# -*- coding: utf-8 -*-
u"""Как шум матрицы переходит в невязку разбора (`T35`).

Зачем. Дешёвый выигрыш №3 — «считать матрицу до порога шума, а не фиксированным
числом историй» — обещает десятикратное ускорение, но порог 5 % НАЗНАЧЕН, а не
выведен: никто не мерил, как шум матрицы переходит в χ²/ndf и невязку разбора.
Резать историй вдесятеро до этого замера нельзя.

Что делает. Берёт журнал счёта матриц (в нём у каждой сцены напечатан шум
континуума), сводит его со спектрами через `geometries/index.csv` и печатает,
что случилось с КАЖДЫМ спектром понятной части при переходе с базовых матриц на
дешёвые. Мерило — не среднее по корпусу: шум у сцен разный, и вопрос ровно в
том, растёт ли цена вместе с ним.

    python tools/CORPUS/scripts/mx_noise.py --log=<build-nXXk.log>
        --base=tools/pie/out_v2 --test=tools/pie/out_mx30k
"""
import argparse
import csv
import io
import os
import re
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.abspath(os.path.join(HERE, os.pardir, 'corpus'))


def noise_by_key(log_path):
    u"""{ключ сцены: шум континуума, %} из журнала счёта матриц."""
    out = {}
    key = None
    with io.open(log_path, encoding='utf-8', errors='replace') as fh:
        for line in fh:
            m = re.match(r'^== (\S+) ==', line)
            if m:
                key = m.group(1)
                continue
            m = re.search(r'шум конт\.:\s*взвешенная\s+([\d.]+)\s*%', line)
            if m and key:
                out[key] = float(m.group(1))
                key = None
    return out


def spectra_by_key():
    out = {}
    with io.open(os.path.join(CORPUS, 'geometries', 'index.csv'),
                 encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            out.setdefault(row['geometry'], []).append(row['spectrum'])
    return out


def runs(out_dir):
    u"""{спектр: (chi2ndf, невязка %)} по всем группам прогона."""
    res = {}
    for name in os.listdir(out_dir):
        if not name.endswith('_spline_runs.csv'):
            continue
        with io.open(os.path.join(out_dir, name), encoding='utf-8-sig', newline='') as fh:
            for row in csv.DictReader(fh):
                key = list(row.values())[0]
                try:
                    res[key] = (float(row['chi2ndf']),
                                float(row.get('model_residual_pct') or 'nan'))
                except (TypeError, ValueError):
                    continue
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--log', required=True, help=u'журнал счёта дешёвых матриц')
    ap.add_argument('--base', required=True, help=u'каталог прогона на базовых матрицах')
    ap.add_argument('--test', required=True, help=u'каталог прогона на дешёвых')
    args = ap.parse_args()

    noise = noise_by_key(args.log)
    bykey = spectra_by_key()
    base, test = runs(args.base), runs(args.test)
    print(u'шум матрицы против цены разбора (T35)')
    print(u'сцен с напечатанным шумом: %d' % len(noise))
    print()
    print(u'%-24s %-22s %7s %9s %9s %8s %8s'
          % (u'спектр', u'сцена', u'шум,%', u'chi2 база', u'chi2 деш.',
             u'Δchi2,%', u'Δнев,пп'))

    rows = []
    for key in sorted(noise):
        for spectrum in bykey.get(key, []):
            if spectrum not in base or spectrum not in test:
                continue
            b, t = base[spectrum], test[spectrum]
            d_chi = 100.0 * (t[0] - b[0]) / max(b[0], 1e-9)
            d_res = t[1] - b[1]
            rows.append((noise[key], d_chi, d_res, spectrum, key))
            print(u'%-24s %-22s %7.2f %9.2f %9.2f %8.1f %8.1f'
                  % (spectrum, key, noise[key], b[0], t[0], d_chi, d_res))

    if not rows:
        print(u'нет общих спектров — проверьте каталоги')
        return

    n = np.array([r[0] for r in rows])
    dchi = np.array([r[1] for r in rows])
    dres = np.array([r[2] for r in rows])
    print()
    print(u'спектров: %d, шум %.2f…%.2f %% (медиана %.2f)'
          % (len(rows), n.min(), n.max(), np.median(n)))
    print(u'Δchi2: медиана %+.1f %%, худший %+.1f %%' % (np.median(dchi), dchi.max()))
    print(u'Δневязки: медиана %+.1f пп, худшая %+.1f пп' % (np.median(dres), dres.max()))
    if len(rows) > 3:
        print(u'корреляция шум — Δchi2: %.2f' % float(np.corrcoef(n, dchi)[0, 1]))
        print(u'корреляция шум — Δневязки: %.2f' % float(np.corrcoef(n, dres)[0, 1]))

    print()
    print(u'по полосам шума:')
    print(u'%-14s %6s %12s %12s' % (u'шум, %', u'спектров', u'медиана Δchi2', u'медиана Δнев'))
    for lo, hi in ((0.0, 2.0), (2.0, 3.0), (3.0, 5.0), (5.0, 8.0), (8.0, 100.0)):
        m = (n >= lo) & (n < hi)
        if m.sum() == 0:
            continue
        print(u'%-14s %6d %11.1f %% %11.1f пп'
              % (u'%.0f…%.0f' % (lo, hi), int(m.sum()),
                 float(np.median(dchi[m])), float(np.median(dres[m]))))


if __name__ == '__main__':
    main()
