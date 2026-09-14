# -*- coding: utf-8 -*-
# bqp12_tail.py — п.1 рецензента: коэффициенты lowTail на РЕАЛЬНЫХ данных, плечи А (старый склад)
# и Б (mini_a16), 42 спектра понятной части малой базы. Кто поглощает сдвиг образа по полосам:
# образ / хвост (tail:*) / сплайн — в отсчётах, Σ по спектрам (как П9 §3(г), но хвост ОТДЕЛЬНО).
import csv, io, os, sys, glob
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
OUT = r'C:\Users\moroz\bqp12_out'
BANDS = [(-1e9, 45.0), (45.0, 100.0), (100.0, 300.0), (300.0, 1000.0), (1000.0, 1e9)]
BN = ['<45', '45-100', '100-300', '300-1000', '>1000']

def load(arm, key):
    d = os.path.join(OUT, arm + '_dump')
    amps = list(csv.DictReader(io.open(os.path.join(d, key + '_amps.csv'), encoding='utf-8-sig')))
    rows = list(csv.DictReader(io.open(os.path.join(d, key + '_cols.csv'), encoding='utf-8-sig')))
    kev = np.array([float(r['keV']) for r in rows])
    y = np.array([float(r['y']) for r in rows])
    resid = np.array([float(r['resid']) for r in rows])
    wrep = np.array([float(r['w_rep']) for r in rows])
    cols = {}
    for a in amps:
        cols[a['name']] = np.array([float(r[a['name']]) for r in rows])
    lo, hi = int(amps[0]['first']), int(amps[0]['last'])
    return amps, cols, kev, y, resid, wrep, lo, hi

def known():
    ks = []
    for f in glob.glob(os.path.join(OUT, 'a', '*_runs.csv')):
        for r in csv.DictReader(io.open(f, encoding='utf-8-sig')):
            if r['part'] == 'known' and r['chi2ndf'] not in ('', 'ERROR'):
                ks.append(r['spectrum'])
    return sorted(ks)

def main():
    keys = known()
    print('спектров понятной части: %d' % len(keys))
    # 1. коэффициенты хвостов
    print('\n== коэффициенты lowTail (tail:*), плечо А → Б, реальные данные ==')
    print('%-22s %-22s %14s %14s %9s %8s %8s' % ('спектр', 'хвост', 'amp A', 'amp B', 'Б/А-1 %', 'z A', 'z B'))
    ratios = []; nA0 = nB0 = 0; n = 0
    sums = {arm: {k: np.zeros(len(BANDS)) for k in ('image', 'tail', 'spline', 'data', 'absres', 'chi2')} for arm in 'ab'}
    for key in keys:
        A = load('a', key); B = load('b', key)
        ampsA = {a['name']: a for a in A[0]}; ampsB = {a['name']: a for a in B[0]}
        for name in ampsA:
            if not name.startswith('tail:'):
                continue
            if name not in ampsB:
                print('%-22s %-22s есть только в А' % (key, name)); continue
            xa = float(ampsA[name]['amp']); xb = float(ampsB[name]['amp'])
            n += 1
            if xa <= 0: nA0 += 1
            if xb <= 0: nB0 += 1
            rr = (xb / xa - 1.0) * 100.0 if xa > 0 else float('nan')
            if xa > 0 and xb > 0: ratios.append(rr)
            print('%-22s %-22s %14.6g %14.6g %9.2f %8.2f %8.2f' % (key, name, xa, xb, rr, float(ampsA[name]['z']), float(ampsB[name]['z'])))
        for arm, D in (('a', A), ('b', B)):
            amps, cols, kev, y, resid, wrep, lo, hi = D
            win = np.zeros(len(kev), bool); win[lo:hi + 1] = True
            for bi, (e0, e1) in enumerate(BANDS):
                m = win & (kev >= e0) & (kev < e1)
                for a in amps:
                    x = float(a['amp'])
                    if x <= 0: continue
                    sums[arm][a['kind']][bi] += x * cols[a['name']][m].sum()
                sums[arm]['data'][bi] += y[m].sum()
                sums[arm]['absres'][bi] += np.abs(resid[m]).sum()
                sums[arm]['chi2'][bi] += (resid[m] ** 2 * wrep[m]).sum()
    ratios = np.array(ratios)
    print('\nхвостов %d; нулевых в А %d, в Б %d; Б/А−1: медиана %.2f %%, размах %.2f … %.2f %%, медиана |·| %.2f %%'
          % (n, nA0, nB0, np.median(ratios), ratios.min(), ratios.max(), np.median(np.abs(ratios))))
    print('\n== кто поглощает сдвиг (Б − А), отсчёты, Σ по %d спектрам ==' % len(keys))
    print('%-9s %14s %14s %12s %12s %12s %12s %12s %12s' % ('полоса', 'Σ данных A', 'Σ|невязка| A', 'образ A', 'хвост A', 'сплайн A', 'Δобраз', 'Δхвост', 'Δсплайн'))
    for bi in range(len(BANDS)):
        a = sums['a']; b = sums['b']
        print('%-9s %14.0f %14.0f %12.0f %12.0f %12.0f %12.0f %12.0f %12.0f' % (
            BN[bi], a['data'][bi], a['absres'][bi], a['image'][bi], a['tail'][bi], a['spline'][bi],
            b['image'][bi] - a['image'][bi], b['tail'][bi] - a['tail'][bi], b['spline'][bi] - a['spline'][bi]))
    print('\nχ² пуассоновский по полосам (w_rep, Σ по спектрам): A %s ; B %s ; Б−А %s' % (
        ' / '.join('%.1f' % v for v in sums['a']['chi2']), ' / '.join('%.1f' % v for v in sums['b']['chi2']),
        ' / '.join('%+.1f' % (v2 - v1) for v1, v2 in zip(sums['a']['chi2'], sums['b']['chi2']))))

if __name__ == '__main__':
    main()
