# -*- coding: utf-8 -*-
"""П67 (AMBER22): нетто площадей опорных линий ряда Th-232 в трёх геометриях диска — грубая проверка статистики
и отношения 238 : 2614 по данным (независимо от FSA).

Шкала — по своим пикам (`recal.py` П56: группы в каналах, полином 3-й степени); фон — встроенный в файл, перебинирован
в шкалу пробы и приведён по живому времени; ⚠ шкала фона = шкала пробы (один прибор, та же неделя) — контроль: пики
фона 1461 (K-40) и 2614 в шкале пробы, сдвиг печатается. Площадь — сумма гауссиан группы (табличные энергии и
интенсивности nucdb, общая ширина по ПШПВ(E), квадратичная подложка) в окне; σ — из ковариации МНК с пуассоновскими
весами. Окна и группы — как `recal.GROUPS` (238 с 241; 583; 911 с 965/969; 2614).

    python lines67.py <спектр.xml> [<спектр.xml> …] [--csv=rates.csv]
"""
import io
import math
import sys

import numpy as np
from scipy.optimize import least_squares

sys.path.insert(0, r'D:\BqMoni_Claude\p67\py')
from spec import Spec, rebin   # noqa: E402
import recal                   # noqa: E402

S2F = 2 * math.sqrt(2 * math.log(2))
BG_GROUPS = [('352', 320, 390, [(351.93, 35.7), (338.32, 2.0)]),
             ('609', 560, 660, [(609.31, 45.4), (583.19, 9.0)]),
             ('1461', 1380, 1560, [(1460.8, 10.7)]),
             ('1764', 1690, 1850, [(1764.5, 15.3)]),
             ('2614', 2450, 2760, [(2614.511, 35.85)])]
PEAKS = [('238', 200, 280, [(238.632, 43.6), (240.986, 4.1)], 238.632),
         ('583', 520, 650, [(583.187, 30.5)], 583.187),
         ('911', 850, 1010, [(911.204, 25.8), (968.971, 15.8), (964.766, 4.99), (904.2, 0.77)], 911.204),
         ('2614', 2440, 2760, [(2614.511, 35.85)], 2614.511)]


def fit_peak(S, net, lo, hi, lines, E0, ab, deg=2):
    ids = np.where((S.keV >= lo) & (S.keV < hi) & (np.arange(S.n) < S.n - 100))[0]
    x = S.keV[ids]
    y = net[ids]
    dx = S.keVb[ids + 1] - S.keVb[ids]      # ширина канала, кэВ: гауссиана — плотность на кэВ × ширина канала
    # пуассоновские веса по СЫРОМУ (проба + фон·k), а не по нетто
    raw = S.counts[ids]
    k = S.live / S.bg.live if S.bg is not None else 0.0
    bgc = rebin(S.bg, S.keVb)[ids] * k if S.bg is not None else np.zeros_like(y)
    var = np.maximum(raw + bgc * k, 1.0)
    w = 1.0 / np.sqrt(var)
    # ширина — СВОБОДНАЯ у каждой группы (k × номинал 7.65 %·√(662·E)): калибровка ПШПВ по 4 пикам у слабых
    # спектров (edge93) давала на 238 6.8 кэВ вместо ~20 — площадь занижалась втрое
    fw0 = nominal_fwhm(E0)

    def model(p):
        A, sh, kf = p[0], p[1], p[2]
        poly = p[3:]
        m = np.zeros_like(x)
        for E, I in lines:
            s = kf * nominal_fwhm(E) / S2F
            m = m + A * I / 100.0 * dx * np.exp(-0.5 * ((x - (E + sh)) / s) ** 2) / (s * math.sqrt(2 * math.pi))
        u = (x - E0) / 100.0
        for i, c in enumerate(poly):
            m = m + c * u ** i
        return m

    def f(p):
        return (model(p) - y) * w

    p0 = [max(y.max(), 1.0) * fw0 * 2.0 / float(np.mean(dx)), 0.0, 1.0, max(y.min(), 0.0)] + [0.0] * deg
    lb = [0.0, -0.5 * fw0, 0.6] + [-np.inf] * (deg + 1)
    ub = [np.inf, 0.5 * fw0, 1.6] + [np.inf] * (deg + 1)
    r = least_squares(f, p0, bounds=(lb, ub))
    J = r.jac
    try:
        cov = np.linalg.inv(J.T @ J)
        sA = math.sqrt(max(cov[0, 0], 0.0))
    except np.linalg.LinAlgError:
        sA = float('nan')
    chi = (r.fun ** 2).sum() / max(len(x) - len(p0), 1)
    # площадь ГЛАВНОЙ линии группы = A·I0/100 (сумма гауссиан нормирована на A·ΣI/100)
    I0 = lines[0][1]
    return r.x[0] * I0 / 100.0, sA * I0 / 100.0, r.x[1], r.x[2], chi


def nominal_fwhm(E, p662=7.65):
    return p662 / 100.0 * math.sqrt(662.0 * E)


def bg_two_anchor(B):
    """шкала ФОНА линейно по двум своим опорам — 1461 (K-40) и 2614 (Tl-208): центроиды гауссианами в каналах
    (окна по шкале файла ±6 %), затем ch → кэВ = z + g·ch; полином 3-й степени по 5 группам фона расходился"""
    from scipy.ndimage import uniform_filter1d
    B.bg = None
    sm = uniform_filter1d(B.counts, 9)
    ch = np.arange(B.n, dtype=float)
    cents = []
    for E0 in (1460.8, 2614.511):
        ids = np.where((B.keV >= 0.9 * E0) & (B.keV < 1.1 * E0) & (ch < B.n - 100))[0]
        c0 = ids[np.argmax(sm[ids])]
        half = int(0.09 * c0)
        ids = np.arange(max(c0 - half, 0), min(c0 + half, B.n - 100))
        x = ids.astype(float)
        y = B.counts[ids]
        w = 1.0 / np.sqrt(np.maximum(y, 1.0))

        def f(p):
            A, c, s, a, b = p
            return (A * np.exp(-0.5 * ((x - c) / abs(s)) ** 2) + a + b * (x - c0) - y) * w
        r = least_squares(f, [max(y.max(), 1.0), float(c0), 0.03 * c0, max(y.min(), 0.0), 0.0])
        cents.append((r.x[1], E0))
    (c1, e1), (c2, e2) = cents
    g = (e2 - e1) / (c2 - c1)
    z = e1 - g * c1
    recal.apply(B, [z, g])
    return z, g


def window_net(S, net, E0, k=1.2):
    """площадь окном ±k·ПШПВ(ном.) с ЛИНЕЙНОЙ подложкой по боковым окнам той же ширины (грубая, независимая от формы)"""
    fw = nominal_fwhm(E0)
    lo, hi = E0 - k * fw, E0 + k * fw
    ids = np.where((S.keV >= lo) & (S.keV < hi))[0]
    left = np.where((S.keV >= lo - k * fw) & (S.keV < lo))[0]
    right = np.where((S.keV >= hi) & (S.keV < hi + k * fw))[0]
    base = 0.5 * (net[left].mean() + net[right].mean()) * len(ids)
    raw = S.counts[ids].sum() + (S.counts[left].sum() + S.counts[right].sum()) * 0.25 * (len(ids) / max(len(left), 1)) ** 2 * 0
    area = net[ids].sum() - base
    var = S.counts[ids].sum() + 0.25 * (len(ids) / max(len(left), 1)) ** 2 * (S.counts[left].sum() + S.counts[right].sum())
    return area, math.sqrt(max(var, 1.0))


def bg_check(S):
    """пики фона в шкале пробы: 1461 и 2614 — сдвиг центроида, кэВ"""
    if S.bg is None:
        return {}
    out = {}
    bgc = rebin(S.bg, S.keVb)
    for E0, lo, hi in ((1460.8, 1380, 1560), (2614.511, 2480, 2760)):
        ids = np.where((S.keV >= lo) & (S.keV < hi))[0]
        x = S.keV[ids]
        y = bgc[ids]
        w = 1.0 / np.sqrt(np.maximum(y, 1.0))
        fw0 = 0.07 * E0 * math.sqrt(math.sqrt(662 / E0))

        def f(p):
            A, c, fw, a, b = p
            s = abs(fw) / S2F
            return (A * np.exp(-0.5 * ((x - c) / s) ** 2) + a + b * (x - E0) - y) * w
        r = least_squares(f, [max(y.max(), 1.0), E0, fw0, max(y.min(), 0.0), 0.0])
        out[E0] = r.x[1] - E0
    return out


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    files = [a for a in sys.argv[1:] if not a.startswith('--')]
    csv = [a[6:] for a in sys.argv[1:] if a.startswith('--csv=')]
    rows = ['spectrum,live_s,bg_live_s,peak,area,sigma,cps,cps_sigma,shift_keV,kfwhm,chi2n,area_window,sigma_window']
    for path in files:
        S = Spec(path)
        print('== %s: живое %.0f с, фон %s' % (path, S.live, ('%.0f с' % S.bg.live) if S.bg else 'нет'))
        coef, ab, pts, fws = recal.recal(S, verbose=False)
        # ⚠ recal ставит фону шкалу пробы; у AS80 усиление фона другое (1461 уезжает на −37 кэВ у edge93) —
        # фон перекалибровывается ПО СВОИМ пикам (352/609/1461/1764/2614) и перебинируется в шкалу пробы
        if S.bg is not None:
            bg_two_anchor(S.bg)
        chk = bg_check(S)
        print('  шкала: %s; ПШПВ² = %.1f + %.4f·E; пики фона в шкале пробы: %s' % (
            ' '.join('%.6g' % c for c in coef), ab[0], ab[1],
            ', '.join('%.0f %+.1f кэВ' % (E, d) for E, d in chk.items())))
        net, bgc = S.net()
        tot = S.counts.sum()
        print('  всего %.0f отсчётов (%.1f cps), фон в пробе %.0f (%.1f cps), нетто %.1f cps' % (
            tot, tot / S.live, bgc.sum(), bgc.sum() / S.live, (tot - bgc.sum()) / S.live))
        for label, lo, hi, lines, E0 in PEAKS:
            A, sA, sh, kf, chi = fit_peak(S, net, lo, hi, lines, E0, ab)
            Aw, sAw = window_net(S, net, E0 + sh)
            print('  %-5s нетто %10.0f ± %6.0f  (%7.4f ± %6.4f cps)  сдвиг %+5.2f кэВ  k_ПШПВ %.3f  χ²/n %5.2f | окном ±1.2 ПШПВ, лин. подложка: %9.0f ± %6.0f' % (
                label, A, sA, A / S.live, sA / S.live, sh, kf, chi, Aw, sAw))
            rows.append('%s,%.1f,%s,%s,%.1f,%.1f,%.6f,%.6f,%.2f,%.3f,%.2f,%.1f,%.1f' % (
                path, S.live, ('%.1f' % S.bg.live) if S.bg else '', label, A, sA, A / S.live, sA / S.live, sh, kf, chi, Aw, sAw))
    if csv:
        with io.open(csv[0], 'w', encoding='utf-8', newline='') as fh:
            fh.write('\n'.join(rows) + '\n')


if __name__ == '__main__':
    main()
