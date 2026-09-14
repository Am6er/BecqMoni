# -*- coding: utf-8 -*-
"""П64 (AMBER29): ПШПВ прибора G1S ПО САМИМ спектрам угля — сумма 30 равновесных съёмок
(432 000 с), пики ряда Ra-226. Модель `SqrtFwhmCalibration` BecqMoni считается В КАНАЛАХ:
FWHM_ch² = c0 + c1·ch + c2·ch².

Зачем мерить, а не взять готовое: модель группы G1S24 корпуса (`detectors.csv`,
res_c1 = 3.230, res_c2 = 4.84e-4 по кэВ) и полином ЛСРМ из шапки `.spe`
(`FWHM=3,…` по √E) расходятся на 352 кэВ на 10 % (34.6 против 30.3 кэВ), а ширина
образа в FSA не подгоняется (дрейф — только усиление/смещение).

    python handover/p64-amber29/fwhm_measure.py <каталог .spe> [--out=<csv>]

Печатает по каждому пику: центр (кэВ), ПШПВ (кэВ и каналы), σ подгонки; затем
коэффициенты c0,c1,c2 (каналы) и сравнение трёх моделей по опорным энергиям.
"""
import glob
import io
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                os.pardir, os.pardir, 'tools', 'CORPUS', 'scripts'))
from spe_import import read_spe  # noqa: E402

# опорные пики ряда Ra-226 (кэВ) и полуширина окна подгонки (в ПШПВ)
PEAKS = [(295.2, 1.1), (351.9, 1.1), (609.3, 1.4), (1120.3, 1.4), (1764.5, 1.6), (2204.1, 1.6)]


def ecal_of(head):
    v = [float(x) for x in head['ENERGY'].split(',')]
    return v[1:int(v[0]) + 2]


def energy(coef, ch):
    return sum(c * ch ** k for k, c in enumerate(coef))


def channel(coef, e):
    # обратная — Ньютоном
    ch = (e - coef[0]) / coef[1]
    for _ in range(20):
        f = energy(coef, ch) - e
        d = sum(k * c * ch ** (k - 1) for k, c in enumerate(coef) if k > 0)
        ch -= f / d
    return ch


def fit_gauss(x, y, c0, w0):
    """Гаусс + линейный фон, Гаусс–Ньютон по МНК с весами 1/y."""
    p = np.array([y.max() - y.min(), c0, w0 / 2.3548, y.min(), 0.0])
    wgt = 1.0 / np.maximum(y, 1.0)
    for _ in range(60):
        a, c, s, b0, b1 = p
        g = np.exp(-0.5 * ((x - c) / s) ** 2)
        model = a * g + b0 + b1 * (x - c0)
        J = np.vstack([g, a * g * (x - c) / s ** 2, a * g * (x - c) ** 2 / s ** 3,
                       np.ones_like(x), x - c0]).T
        r = y - model
        JW = J * wgt[:, None]
        try:
            dp = np.linalg.solve(J.T @ JW, JW.T @ r)
        except np.linalg.LinAlgError:
            break
        p = p + dp
        if np.max(np.abs(dp[:3]) / np.maximum(np.abs(p[:3]), 1e-9)) < 1e-7:
            break
    a, c, s, b0, b1 = p
    g = np.exp(-0.5 * ((x - c) / s) ** 2)
    model = a * g + b0 + b1 * (x - c0)
    chi2 = float(np.sum((y - model) ** 2 * wgt))
    J = np.vstack([g, a * g * (x - c) / s ** 2, a * g * (x - c) ** 2 / s ** 3,
                   np.ones_like(x), x - c0]).T
    cov = np.linalg.inv(J.T @ (J * wgt[:, None]))
    return p, np.sqrt(np.diag(cov)), chi2 / max(1, len(x) - 5)


def main():
    src = sys.argv[1]
    out = None
    for a in sys.argv[2:]:
        if a.startswith('--out='):
            out = a[6:]
    files = sorted(glob.glob(os.path.join(src, '*дпр_*.spe')))
    assert len(files) == 30, len(files)
    total = None
    coef = None
    for f in files:
        h, c = read_spe(f)
        if coef is None:
            coef = ecal_of(h)
        assert ecal_of(h) == coef
        total = np.array(c, dtype=float) if total is None else total + np.array(c, dtype=float)
    ch = np.arange(len(total), dtype=float)
    lines = ['peak_kev,center_kev,center_ch,fwhm_kev,fwhm_ch,fwhm_err_kev,chi2_ndf']
    pts = []
    for e0, hw in PEAKS:
        c0 = channel(coef, e0)
        dEdch = coef[1] + 2 * coef[2] * c0
        # стартовая ширина — модель корпуса G1S24 (кэВ) в каналах
        w0 = math.sqrt(3.2301 * e0 + 4.8428e-4 * e0 * e0) / dEdch
        lo, hi = int(c0 - hw * w0), int(c0 + hw * w0) + 1
        x, y = ch[lo:hi], total[lo:hi]
        p, err, chi = fit_gauss(x, y, c0, w0)
        fwhm_ch = 2.3548 * abs(p[2])
        fwhm_kev = fwhm_ch * dEdch
        cen = energy(coef, p[1])
        print('пик %7.1f: центр %8.2f кэВ (кан. %7.2f), ПШПВ %6.2f кэВ = %6.3f кан ± %.3f, %5.2f %%, chi2/ndf %.2f'
              % (e0, cen, p[1], fwhm_kev, fwhm_ch, 2.3548 * err[2], 100 * fwhm_kev / cen, chi))
        lines.append('%.1f,%.3f,%.3f,%.3f,%.4f,%.4f,%.3f' % (e0, cen, p[1], fwhm_kev, fwhm_ch, 2.3548 * err[2] * dEdch, chi))
        pts.append((p[1], fwhm_ch, 2.3548 * err[2]))
    # FWHM_ch² = c0 + c1·ch + c2·ch², взвешенный МНК (веса по ошибке ширины)
    A = np.array([[1.0, c, c * c] for c, _, _ in pts])
    b = np.array([w * w for _, w, _ in pts])
    wgt = np.array([1.0 / (2 * w * e) ** 2 for _, w, e in pts])
    sol = np.linalg.lstsq(A * np.sqrt(wgt)[:, None], b * np.sqrt(wgt), rcond=None)[0]
    print('SqrtFwhmCalibration (каналы): c0=%.6g c1=%.6g c2=%.6g' % tuple(sol))
    lines.append('#sqrtfwhm_ch,%r,%r,%r' % tuple(sol))
    # сравнение трёх моделей
    print('%8s %10s %10s %10s' % ('E, кэВ', 'измерено', 'G1S24', 'ЛСРМ'))
    lsrm = [-0.4464256905064, 1.0611014913718, 0.0366457534172, -0.0003184872005]
    for e in (242.0, 295.2, 351.9, 609.3, 1120.3, 1764.5, 2204.1, 2614.5):
        c = channel(coef, e)
        dEdch = coef[1] + 2 * coef[2] * c
        meas = math.sqrt(max(0.0, sol[0] + sol[1] * c + sol[2] * c * c)) * dEdch
        g1s = math.sqrt(3.2301122233508095 * e + 0.00048428222478535406 * e * e)
        x = math.sqrt(e)
        ls = sum(k * x ** i for i, k in enumerate(lsrm))
        print('%8.1f %10.2f %10.2f %10.2f' % (e, meas, g1s, ls))
        lines.append('#model,%.1f,%.3f,%.3f,%.3f' % (e, meas, g1s, ls))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
