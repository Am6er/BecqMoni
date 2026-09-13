# -*- coding: utf-8 -*-
u"""bqp12_asimov.py — Asimov-тест (П12, 11.09.2026): истина = модель плеча T (без шума), разбор
плечами A и B при ОБЩИХ фиксированных весах w = 1/max(μ,1), освобождая параметры по шагам:

  шаг 0 — всё зафиксировано на значениях генерации (амплитуды истины на колонках плеча);
  шаг 1 — свободны амплитуды образов (NNLS), хвосты и сплайн — как в истине;
  шаг 2 — + хвосты `tail:*` (отвязанный низкоэнергетический континуум);
  шаг 3 — + сплайн (полная свобода, но общие веса, без Хубера и inflate).

Печатает на каждую истину: χ² плеч, Δ, слагаемые рецензента ‖Δm‖²_W и −2⟨r,Δm⟩_W (r — остаток
плеча-истины на том же шаге), и то же по полосам <45 / 45–100 / 100–300 / >300 кэВ.

Колонки плана берутся из дампов РЕАЛЬНЫХ прогонов (`a_dump`, `b_dump`): они не зависят от данных
(проверено: совпадают с колонками, построенными на синтетике, кроме `pile-up`, а `pile-up` в А и Б
один и тот же). Истина — `truth/<key>_truth.csv` (μ дробное, без округления).

  python C:\\Users\\moroz\\bqp12_asimov.py [--truth=b] [--keys=...]
"""
import argparse, csv, io, os, sys
import numpy as np
from scipy.optimize import nnls
sys.stdout.reconfigure(encoding='utf-8')
OUT = r'C:\Users\moroz\bqp12_out'
KEYS = ['G1S16_Cs137_P5', 'G1S16_Am241_P5', 'G1S16_Ba133_P5', 'G1S24_Ba133_P5', 'G1S24_Bi207_P5', 'G1S24_Am241_P5']
BANDS = [(-1e9, 45.0), (45.0, 100.0), (100.0, 300.0), (300.0, 1e9)]
BN = ['<45', '45-100', '100-300', '>300']
STEPS = ['0 всё фикс.', '1 +амплитуды', '2 +lowTail', '3 +сплайн']


def load_cols(arm, key):
    d = os.path.join(OUT, arm + '_dump')
    amps = list(csv.DictReader(io.open(os.path.join(d, key + '_amps.csv'), encoding='utf-8-sig')))
    rows = list(csv.DictReader(io.open(os.path.join(d, key + '_cols.csv'), encoding='utf-8-sig')))
    names = [a['name'] for a in amps]
    kinds = {a['name']: a['kind'] for a in amps}
    amp = {a['name']: float(a['amp']) for a in amps}
    C = np.array([[float(r[n]) for n in names] for r in rows])   # n × m
    kev = np.array([float(r['keV']) for r in rows])
    lo, hi = int(amps[0]['first']), int(amps[0]['last'])
    return names, kinds, amp, C, kev, lo, hi


def solve(C, y, w, free, x0):
    """NNLS по свободным колонкам при остальных, зажатых на x0."""
    x = x0.copy()
    fixed = ~free
    r = y - C[:, fixed] @ x0[fixed]
    sw = np.sqrt(w)
    xf, _ = nnls(C[:, free] * sw[:, None], r * sw, maxiter=50 * C.shape[1])
    x[free] = xf
    return x


def band_chi2(res, w, kev, win):
    out = []
    for e0, e1 in BANDS:
        m = win & (kev >= e0) & (kev < e1)
        out.append(float((res[m] ** 2 * w[m]).sum()))
    return out


def run_key(key, truth_arm, other_arm):
    nT, kT, aT, CT, kev, lo, hi = load_cols(truth_arm, key)
    nO, kO, aO, CO, kev2, lo2, hi2 = load_cols(other_arm, key)
    assert (lo, hi) == (lo2, hi2), (lo, hi, lo2, hi2)
    # истина — из дампа плеча-истины: μ_model = C_T·a_T, фон — из `_chi.csv` того же плеча
    chi = list(csv.DictReader(io.open(os.path.join(OUT, truth_arm + '_dump', key + '_chi.csv'), encoding='utf-8-sig')))
    bg = np.array([float(r['bg']) for r in chi])
    mu_model = CT @ np.array([aT[n] for n in nT])
    mu_fore = np.maximum(mu_model + bg, 0.0)
    win = np.zeros(len(kev), bool); win[lo:hi + 1] = True
    w = np.where(win, 1.0 / np.maximum(mu_fore, 1.0), 0.0)
    y = mu_model
    ft = os.path.join(OUT, 'truth', key + '_truth.csv')
    if truth_arm == 'b' and os.path.exists(ft):
        tr = list(csv.DictReader(io.open(ft, encoding='utf-8-sig')))
        d = np.abs(mu_model - np.array([float(r['mu_model']) for r in tr])).max()
        assert d < 1e-6 * max(1.0, mu_model.max()), d   # истина файла = истина дампа
    # соответствие колонок по имени
    missing = [n for n in nT if n not in nO]
    extra = [n for n in nO if n not in nT]
    if missing or extra:
        print('  ⚠ колонки: только у истины %s, только у другого плеча %s' % (missing, extra))
    common = [n for n in nT if n in nO]
    x0 = np.array([aT[n] for n in common])
    Ct = CT[:, [nT.index(n) for n in common]]
    Co = CO[:, [nO.index(n) for n in common]]
    # положительный контроль истины: Σ a·C_T = y
    yt = Ct @ x0 + sum(aT[n] * CT[:, nT.index(n)] for n in missing) if missing else Ct @ x0
    assert np.abs(yt - y).max() < 1e-6 * max(1.0, y.max()), np.abs(yt - y).max()
    kinds = np.array([kT[n] for n in common])
    img = kinds == 'image'; tail = kinds == 'tail'; spl = kinds == 'spline'
    print('  колонок общих %d: образов %d, хвостов %d (в истине активных %d), шапок %d; окно %d..%d; Σμ %.0f'
          % (len(common), img.sum(), tail.sum(), int((x0[tail] > 0).sum()), spl.sum(), lo, hi, mu_model[win].sum()))

    frees = [np.zeros(len(common), bool), img.copy(), img | tail, img | tail | spl]
    table = []
    for s, free in enumerate(frees):
        xo = solve(Co, y, w, free, x0) if free.any() else x0.copy()
        xt = solve(Ct, y, w, free, x0) if free.any() else x0.copy()
        mo = Co @ xo; mt = Ct @ xt
        ro = y - mo; rt = y - mt
        chi_o = float((ro ** 2 * w).sum()); chi_t = float((rt ** 2 * w).sum())
        dm = mo - mt
        n1 = float((dm ** 2 * w).sum()); n2 = float(-2.0 * (rt * dm * w).sum())
        bo = band_chi2(ro, w, kev, win); bt = band_chi2(rt, w, kev, win)
        # амплитуды образов другого плеча против истины
        damp = []
        for j in np.where(img)[0]:
            if x0[j] > 0:
                damp.append((common[j], (xo[j] / x0[j] - 1.0) * 100.0))
        table.append((s, chi_o, chi_t, n1, n2, bo, bt, damp, xo))
    return common, kinds, x0, table


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--truth', default='b')
    ap.add_argument('--keys', default=','.join(KEYS))
    a = ap.parse_args()
    truth_arm = a.truth; other = 'a' if truth_arm == 'b' else 'b'
    label_t = 'новых (Б)' if truth_arm == 'b' else 'старых (А)'
    label_o = 'старых (А)' if truth_arm == 'b' else 'новых (Б)'
    print('ИСТИНА — модель %s матриц; разбор колонками %s; веса 1/max(μ,1), общие, без Хубера' % (label_t, label_o))
    tot = {}
    for key in a.keys.split(','):
        print('\n=== %s ===' % key)
        common, kinds, x0, table = run_key(key, truth_arm, other)
        print('  %-14s %12s %12s %12s %12s %12s   %s' % ('шаг', 'χ² ' + other.upper(), 'χ² ' + truth_arm.upper(), 'Δ', '‖Δm‖²_W', '−2⟨r,Δm⟩_W', 'амплитуды образов ' + other.upper() + ' − истина, %'))
        for s, chi_o, chi_t, n1, n2, bo, bt, damp, xo in table:
            print('  %-14s %12.3f %12.3f %12.3f %12.3f %12.3f   %s' % (STEPS[s], chi_o, chi_t, chi_o - chi_t, n1, n2,
                  '; '.join('%s %+.3f' % (n, d) for n, d in damp)))
        print('  по полосам, χ² %s (χ² %s):' % (other.upper(), truth_arm.upper()))
        print('  %-14s %s' % ('шаг', ' '.join('%22s' % b for b in BN)))
        for s, chi_o, chi_t, n1, n2, bo, bt, damp, xo in table:
            print('  %-14s %s' % (STEPS[s], ' '.join('%12.3f (%8.3f)' % (o, t) for o, t in zip(bo, bt))))
            tot.setdefault(s, np.zeros(len(BANDS)))
            tot[s] += np.array(bo)
        # кто съел: доля ‖Δm‖² шага 0, снятая каждым шагом
        c0 = table[0][1]
        print('  съедено от χ²(0)=%.3f: амплитуды %.1f %%, +lowTail %.1f %%, +сплайн %.1f %%; осталось %.3f (%.1f %%)' % (
            c0, 100 * (c0 - table[1][1]) / c0, 100 * (table[1][1] - table[2][1]) / c0, 100 * (table[2][1] - table[3][1]) / c0,
            table[3][1], 100 * table[3][1] / c0))
        # хвосты: амплитуды на шаге 2 и 3 против истины
        xo3 = table[3][8]; xo2 = table[2][8]
        for j in np.where(kinds == 'tail')[0]:
            if x0[j] > 0 or xo3[j] > 0 or xo2[j] > 0:
                print('    %-16s истина %12.6g  шаг2 %12.6g  шаг3 %12.6g' % (common[j], x0[j], xo2[j], xo3[j]))
    print('\nΣ по %d истинам, χ² %s по полосам:' % (len(a.keys.split(',')), other.upper()))
    for s in sorted(tot):
        print('  %-14s %s   всего %.3f' % (STEPS[s], ' '.join('%12.3f' % v for v in tot[s]), tot[s].sum()))


if __name__ == '__main__':
    main()
