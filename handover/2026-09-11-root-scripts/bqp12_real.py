# -*- coding: utf-8 -*-
u"""bqp12_real.py — формула рецензента на РЕАЛЬНЫХ данных, 42 спектра малой базы (П12, 11.09.2026).

Плечо А — старые матрицы, Б — mini_a16 (оба с переносом по каналам, = плечи Г/В П9).
Для каждого спектра, ОТЧЁТНЫМИ (пуассоновскими) весами w_rep плеча А:

  Δm0 = C_B·a_A − C_A·a_A           — сдвиг модели ОТ ОДНОЙ ЗАМЕНЫ матрицы, без переподгонки
  Δχ²_raw = ‖Δm0‖²_W − 2⟨r_A, Δm0⟩_W  — что даёт замена матрицы САМА ПО СЕБЕ (< 0 — выигрыш есть)
  Δχ²_app = χ²_B − χ²_A              — что видит приложение (оба плеча переподогнаны Хубером)
  Δχ²_nnls = χ²_B3 − χ²_A3           — оба плеча переподогнаны NNLS ОБЩИМИ весами w_rep, без Хубера
                                       (шаг 3 Asimov-теста на данных)

Печать: по полосам <45 / 45–100 / 100–300 / >300 и всего, Σ по спектрам; сырой χ² и χ²/ndf_rep.
"""
import csv, io, os, sys, glob
import numpy as np
from scipy.optimize import nnls
sys.stdout.reconfigure(encoding='utf-8')
OUT = r'C:\Users\moroz\bqp12_out'
BANDS = [(-1e9, 45.0), (45.0, 100.0), (100.0, 300.0), (300.0, 1e9)]
BN = ['<45', '45-100', '100-300', '>300', 'всего']


def load(arm, key):
    d = os.path.join(OUT, arm + '_dump')
    amps = list(csv.DictReader(io.open(os.path.join(d, key + '_amps.csv'), encoding='utf-8-sig')))
    rows = list(csv.DictReader(io.open(os.path.join(d, key + '_cols.csv'), encoding='utf-8-sig')))
    chi = list(csv.DictReader(io.open(os.path.join(d, key + '_chi.csv'), encoding='utf-8-sig')))
    names = [a['name'] for a in amps]
    amp = np.array([float(a['amp']) for a in amps])
    C = np.array([[float(r[n]) for n in names] for r in rows])
    kev = np.array([float(r['keV']) for r in rows])
    y = np.array([float(r['y']) for r in rows])
    w = np.array([float(r['w_rep']) for r in rows])
    res = np.array([float(r['resid']) for r in rows])
    lo, hi = int(amps[0]['first']), int(amps[0]['last'])
    ndf = float(chi[0]['ndf_rep'])
    return names, amp, C, kev, y, w, res, lo, hi, ndf


def known():
    ks = []
    for f in glob.glob(os.path.join(OUT, 'a', '*_runs.csv')):
        for r in csv.DictReader(io.open(f, encoding='utf-8-sig')):
            if r['part'] == 'known' and r['chi2ndf'] not in ('', 'ERROR'):
                ks.append(r['spectrum'])
    return sorted(ks)


def bands(v, w, kev, win):
    out = [float((v[m] ** 2 * w[m]).sum()) for m in [win & (kev >= e0) & (kev < e1) for e0, e1 in BANDS]]
    return np.array(out + [sum(out)])


def bands_dot(a, b, w, kev, win):
    out = [float((a[m] * b[m] * w[m]).sum()) for m in [win & (kev >= e0) & (kev < e1) for e0, e1 in BANDS]]
    return np.array(out + [sum(out)])


def main():
    keys = known()
    acc = {k: np.zeros(5) for k in ('t1', 't2', 'raw', 'app', 'nnls', 'chiA', 'chiA3')}
    accn = {k: np.zeros(5) for k in acc}
    per = []
    print('%-20s %10s %10s %10s %10s %10s   %s' % ('спектр', '‖Δm0‖²', '−2⟨r,Δm0⟩', 'Δχ²_raw', 'Δχ²_app', 'Δχ²_nnls', 'χ²_A (сырой, w_rep)'))
    for key in keys:
        nA, aA, CA, kev, yA, wA, rA, lo, hi, ndfA = load('a', key)
        nB, aB, CB, kev2, yB, wB, rB, lo2, hi2, ndfB = load('b', key)
        assert (lo, hi) == (lo2, hi2) and np.abs(yA - yB).max() == 0.0
        win = np.zeros(len(kev), bool); win[lo:hi + 1] = True
        w = np.where(win, wA, 0.0)
        # контроль дампа: остаток = y − C·a
        assert np.abs((yA - CA @ aA) - rA)[win].max() < 1e-6 * max(1.0, np.abs(yA).max()), key
        # Δm0: те же амплитуды А на колонках Б; колонка, которой нет у Б, — считаем её у Б нулевой
        mB0 = np.zeros(len(kev))
        for j, n in enumerate(nA):
            if aA[j] <= 0: continue
            if n in nB:
                mB0 += aA[j] * CB[:, nB.index(n)]
            else:
                print('  ⚠ %s: колонка %s (amp %.4g) есть только у А — в Δm0 идёт как исчезнувшая' % (key, n, aA[j]))
        mA = CA @ aA
        dm0 = mB0 - mA
        t1 = bands(dm0, w, kev, win)
        t2 = -2.0 * bands_dot(rA, dm0, w, kev, win)
        chiA = bands(rA, w, kev, win); chiB = bands(rB, w, kev, win)
        # NNLS общими весами, оба плеча, полная свобода
        sw = np.sqrt(w)
        xA, _ = nnls(CA * sw[:, None], yA * sw, maxiter=50 * CA.shape[1])
        xB, _ = nnls(CB * sw[:, None], yA * sw, maxiter=50 * CB.shape[1])
        chiA3 = bands(yA - CA @ xA, w, kev, win); chiB3 = bands(yA - CB @ xB, w, kev, win)
        row = dict(t1=t1, t2=t2, raw=t1 + t2, app=chiB - chiA, nnls=chiB3 - chiA3, chiA=chiA, chiA3=chiA3)
        for k in acc:
            acc[k] += row[k]; accn[k] += row[k] / ndfA
        per.append((key, row, ndfA))
        print('%-20s %10.1f %10.1f %10.1f %10.1f %10.1f   %.0f' % (key, t1[4], t2[4], t1[4] + t2[4], (chiB - chiA)[4], (chiB3 - chiA3)[4], chiA[4]))
    print('\n=== Σ по %d спектрам, СЫРОЙ χ² (w_rep) по полосам ===' % len(keys))
    print('%-34s %s' % ('', ' '.join('%12s' % b for b in BN)))
    for k, lab in (('chiA', 'χ²_A приложения (Хубер)'), ('chiA3', 'χ²_A3 NNLS общими весами'), ('t1', '‖Δm0‖²_W'), ('t2', '−2⟨r_A,Δm0⟩_W'),
                   ('raw', 'Δχ²_raw = замена матрицы без переподгонки'), ('app', 'Δχ²_app = Б−А приложения'), ('nnls', 'Δχ²_nnls = Б−А NNLS общими весами')):
        print('%-34s %s' % (lab, ' '.join('%12.1f' % v for v in acc[k])))
    print('\n=== то же в единицах χ²/ndf_rep (как П9 §2.2), Σ по спектрам ===')
    print('%-34s %s' % ('', ' '.join('%12s' % b for b in BN)))
    for k, lab in (('chiA', 'χ²/ndf A приложения'), ('chiA3', 'χ²/ndf A3 NNLS'), ('t1', '‖Δm0‖²'), ('t2', '−2⟨r,Δm0⟩'), ('raw', 'Δ raw'), ('app', 'Δ app (Б−А)'), ('nnls', 'Δ nnls (Б−А)')):
        print('%-34s %s' % (lab, ' '.join('%12.3f' % v for v in accn[k])))
    # знак: у скольких спектров замена без переподгонки даёт выигрыш
    neg = sum(1 for key, row, n in per if row['raw'][4] < 0)
    negapp = sum(1 for key, row, n in per if row['app'][4] < 0)
    negn = sum(1 for key, row, n in per if row['nnls'][4] < 0)
    print('\nспектров с выигрышем (Δ<0): без переподгонки %d/%d, приложение %d/%d, NNLS общими весами %d/%d' % (neg, len(per), negapp, len(per), negn, len(per)))
    # где второй член отрицателен и больше первого — там замена матрицы уменьшает СУЩЕСТВУЮЩУЮ невязку
    print('полосы, где −2⟨r,Δm0⟩ < −‖Δm0‖² (замена сама по себе выигрывает): %s' % ', '.join(b for b, v in zip(BN, acc['raw']) if v < 0))
    with io.open(os.path.join(OUT, 'real_formula.csv'), 'w', encoding='utf-8', newline='') as f:
        f.write('spectrum,ndf_rep,' + ','.join('%s_%s' % (k, b) for k in ('t1', 't2', 'raw', 'app', 'nnls', 'chiA', 'chiA3') for b in ('lt45', '45_100', '100_300', 'gt300', 'all')) + '\n')
        for key, row, n in per:
            f.write(key + ',' + repr(n) + ',' + ','.join(repr(float(v)) for k in ('t1', 't2', 'raw', 'app', 'nnls', 'chiA', 'chiA3') for v in row[k]) + '\n')


if __name__ == '__main__':
    main()
