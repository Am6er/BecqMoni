# -*- coding: utf-8 -*-
"""П64 (AMBER29), п. 1: фит распада A(t) по 30 равновесным съёмкам.

Вход — csv со столбцами `t_mid_h` (середина окна от t₀, ч), `value`, `err` (любые
единицы: Бк из `--rates=` FSA или 1/с сырого окна). Три модели, все — взвешенный МНК
(χ² по σ), нелинейный по λ — сеткой + Гаусс–Ньютон:

  * `exp`   : A(t) = A₀·exp(−λt)                     — 2 параметра, T½ свободен;
  * `fixed` : A(t) = C + B·exp(−λ_Rn·t), λ_Rn = ln2/3.8235 сут — 2 параметра (постоянная
              подставка: Ra-226 в угле / подсос радона из воздуха / недовычтенный фон);
  * `free`  : A(t) = C + B·exp(−λt)                   — 3 параметра.

⚠ Средняя по окну активность ≠ активность в середине окна: при T = 4 ч и T½ = 3.82 сут
поправка exp-среднего к значению в середине — (sh(λT/2)/(λT/2)) = 1 + (λT)²/24 = 1.00024,
пренебрежимо; в фите берётся значение в середине окна.

Положительный контроль (`--selftest`): ряд, сгенерированный с T½ = 3.0 сут (и с подставкой
C = 20 % от A₀ при T½ = 3.8235), с пуассоновым шумом σ = 1 % — модель `exp` обязана вернуть
3.0 ± ошибка, а не 3.82; модель `fixed` на ряду с подставкой — вернуть C. Иначе фит не
принимается.

    python handover/p64-amber29/decay_fit.py <csv> [--col=value] [--label=Pb-214] [--out=<csv>]
    python handover/p64-amber29/decay_fit.py --selftest

Печатает по каждой модели: параметры ± σ, T½ ± σ (сут), χ²/ndf, остатки по съёмкам
(в σ) и наклон остатков по t (систематика с уровнем).
"""
import csv
import io
import math
import sys

import numpy as np

HL_RN = 3.8235 * 86400.0
LAM_RN_H = math.log(2.0) / (HL_RN / 3600.0)     # 1/ч


def load(path, col='value', ecol='err', tcol='t_mid_h'):
    t, v, e = [], [], []
    with io.open(path, encoding='utf-8-sig') as fh:
        for row in csv.DictReader(fh):
            if row.get(col, '') in ('', None):
                continue
            t.append(float(row[tcol]))
            v.append(float(row[col]))
            e.append(float(row[ecol]))
    return np.array(t), np.array(v), np.array(e)


def linfit(X, y, w):
    """Взвешенный МНК: y ≈ X·p; возвращает p, cov."""
    Xw = X * w[:, None]
    cov = np.linalg.inv(X.T @ (X * (w * w)[:, None]))
    p = cov @ (X.T @ (y * w * w))
    return p, cov


def fit_exp(t, v, e, with_const):
    """Профиль по λ: при каждом λ линейные параметры — МНК; минимум χ²; σ(λ) по кривизне."""
    w = 1.0 / e

    def chi2_at(lam):
        cols = [np.exp(-lam * t)]
        if with_const:
            cols.append(np.ones_like(t))
        X = np.vstack(cols).T
        p, cov = linfit(X, v, w)
        r = (v - X @ p) * w
        return float(r @ r), p, cov, X

    lams = np.exp(np.linspace(math.log(LAM_RN_H / 20), math.log(LAM_RN_H * 20), 800))
    chis = np.array([chi2_at(l)[0] for l in lams])
    i = int(np.argmin(chis))
    lam = lams[i]
    # уточнение — золотое сечение
    lo, hi = lams[max(0, i - 1)], lams[min(len(lams) - 1, i + 1)]
    for _ in range(80):
        m1, m2 = lo + (hi - lo) * 0.382, lo + (hi - lo) * 0.618
        if chi2_at(m1)[0] < chi2_at(m2)[0]:
            hi = m2
        else:
            lo = m1
    lam = 0.5 * (lo + hi)
    chi, p, cov, X = chi2_at(lam)
    # σ(λ) — по Δχ² = 1 численно (кривизна профиля)
    h = lam * 1e-3
    c2 = (chi2_at(lam + h)[0] - 2 * chi + chi2_at(lam - h)[0]) / (h * h)
    slam = math.sqrt(2.0 / c2) if c2 > 0 else float('nan')
    ndf = len(t) - (3 if with_const else 2)
    hl_h = math.log(2.0) / lam
    shl_h = hl_h * slam / lam
    return dict(lam=lam, slam=slam, hl_d=hl_h / 24.0, shl_d=shl_h / 24.0, chi2=chi, ndf=ndf,
                B=p[0], sB=math.sqrt(cov[0, 0]), C=(p[1] if with_const else 0.0),
                sC=(math.sqrt(cov[1, 1]) if with_const else 0.0), model=X @ p)


def fit_fixed(t, v, e):
    w = 1.0 / e
    X = np.vstack([np.exp(-LAM_RN_H * t), np.ones_like(t)]).T
    p, cov = linfit(X, v, w)
    r = (v - X @ p) * w
    return dict(lam=LAM_RN_H, slam=0.0, hl_d=3.8235, shl_d=0.0, chi2=float(r @ r), ndf=len(t) - 2,
                B=p[0], sB=math.sqrt(cov[0, 0]), C=p[1], sC=math.sqrt(cov[1, 1]), model=X @ p)


def report(name, t, v, e, f, label):
    r = (v - f['model']) / e
    # наклон остатков по t (систематика): линейный фит r ~ a + b·t
    X = np.vstack([np.ones_like(t), t]).T
    pb, cb = linfit(X, r, np.ones_like(t))
    print('%s [%s]: T½ = %.4f ± %.4f сут; B = %.4g ± %.2g; C = %.4g ± %.2g; χ²/ndf = %.2f/%d = %.2f; '
          'остатки: RMS %.2f σ, наклон %.4f ± %.4f σ/ч, крайние %+.2f (%s) … %+.2f (%s)'
          % (label, name, f['hl_d'], f['shl_d'], f['B'], f['sB'], f['C'], f['sC'], f['chi2'], f['ndf'],
             f['chi2'] / max(1, f['ndf']), math.sqrt(np.mean(r * r)), pb[1], math.sqrt(cb[1, 1]),
             r.min(), int(np.argmin(r)) + 1, r.max(), int(np.argmax(r)) + 1))
    return r


def selftest():
    rng = np.random.default_rng(20260914)
    t = 5.49 + 4.0 * np.arange(30)
    ok = True
    # (а) чистая экспонента T½ = 3.0 сут
    lam = math.log(2.0) / 72.0
    v0 = 100.0 * np.exp(-lam * t)
    e = 0.01 * v0
    v = v0 + rng.normal(0, 1, 30) * e
    f = fit_exp(t, v, e, False)
    print('контроль (а) T½ = 3.0 сут, чистая экспонента → exp: T½ = %.4f ± %.4f, χ²/ndf %.2f'
          % (f['hl_d'], f['shl_d'], f['chi2'] / f['ndf']))
    ok &= abs(f['hl_d'] - 3.0) < 3 * f['shl_d'] and abs(f['hl_d'] - 3.8235) > 5 * f['shl_d']
    # (б) 3.8235 сут + подставка 20 % → fixed обязана вернуть C = 20, free — T½ 3.82 и C 20
    v0 = 20.0 + 80.0 * np.exp(-LAM_RN_H * t)
    e = 0.01 * v0
    v = v0 + rng.normal(0, 1, 30) * e
    ff = fit_fixed(t, v, e)
    fr = fit_exp(t, v, e, True)
    fe = fit_exp(t, v, e, False)
    print('контроль (б) 3.8235 сут + C = 20 → fixed: C = %.3f ± %.3f, χ²/ndf %.2f; free: T½ = %.3f ± %.3f, C = %.2f ± %.2f; '
          'exp (без C, неверная модель): T½ = %.3f ± %.3f, χ²/ndf %.1f'
          % (ff['C'], ff['sC'], ff['chi2'] / ff['ndf'], fr['hl_d'], fr['shl_d'], fr['C'], fr['sC'],
             fe['hl_d'], fe['shl_d'], fe['chi2'] / fe['ndf']))
    ok &= abs(ff['C'] - 20.0) < 3 * ff['sC'] and abs(fr['hl_d'] - 3.8235) < 3 * fr['shl_d']
    # неверная модель (без C) видна по T½: уходит от 3.8235 на много σ (χ²/ndf при 1 % шуме — всего ~2, не признак)
    ok &= abs(fe['hl_d'] - 3.8235) > 5 * fe['shl_d'] and fe['chi2'] > 1.3 * fr['chi2']
    print('САМОПРОВЕРКА %s' % ('ПРОШЛА' if ok else 'НЕ ПРОШЛА'))
    return ok


def main():
    if '--selftest' in sys.argv:
        sys.exit(0 if selftest() else 1)
    path = sys.argv[1]
    col, ecol, label, out = 'value', 'err', '', None
    for a in sys.argv[2:]:
        if a.startswith('--col='):
            col = a[6:]
        elif a.startswith('--err='):
            ecol = a[6:]
        elif a.startswith('--label='):
            label = a[8:]
        elif a.startswith('--out='):
            out = a[6:]
    t, v, e = load(path, col, ecol)
    print('%s: точек %d, t = %.2f … %.2f ч, A = %.4g … %.4g' % (label or col, len(t), t.min(), t.max(), v[0], v[-1]))
    res = {}
    lines = ['model,hl_d,hl_err_d,B,B_err,C,C_err,chi2,ndf']
    for name, f in (('exp', fit_exp(t, v, e, False)), ('fixed', fit_fixed(t, v, e)), ('free', fit_exp(t, v, e, True))):
        r = report(name, t, v, e, f, label or col)
        res[name] = (f, r)
        lines.append('%s,%.5f,%.5f,%.6g,%.3g,%.6g,%.3g,%.3f,%d' % (name, f['hl_d'], f['shl_d'], f['B'], f['sB'], f['C'], f['sC'], f['chi2'], f['ndf']))
    print('остатки по съёмкам (σ), exp | fixed | free:')
    for i in range(len(t)):
        print('  %2d t=%6.2f ч  %8.4g ± %-7.3g  %+6.2f | %+6.2f | %+6.2f' % (i + 1, t[i], v[i], e[i], res['exp'][1][i], res['fixed'][1][i], res['free'][1][i]))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write('\n'.join(lines) + '\n')
            fh.write('#i,t_mid_h,value,err,res_exp,res_fixed,res_free\n')
            for i in range(len(t)):
                fh.write('#%d,%.4f,%.6g,%.4g,%.3f,%.3f,%.3f\n' % (i + 1, t[i], v[i], e[i], res['exp'][1][i], res['fixed'][1][i], res['free'][1][i]))


if __name__ == '__main__':
    main()
