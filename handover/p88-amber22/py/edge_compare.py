# -*- coding: utf-8 -*-
r"""П88 (AMBER22): две съёмки ребром (14.09 и 17.09, диск повёрнут на 90° вокруг оси детектора) — сравнение НА ДАННЫХ,
без модели: нетто-площади линий ряда Th-232 (238, 338, 583, 911, 2614) в cps после вычитания фона (фон перебинирован по
своей — теперь перекалиброванной — шкале в шкалу пробы, нормировка по живому времени), плюс контроль фона: K-40 1461
(в диске нет — после вычитания должен быть ~0), Bi-214 609/1764 и Pb-214 352 (радон комнаты: показывают, тот ли фон).
Площадь — окно ±1.2 ПШПВ вокруг центроида (гауссиана + линейная подложка в каналах пробы), подложка — линейная по боковым
окнам (метод «окно» lines67.py П67). Отношения 17.09/14.09 по линиям: при слое тория на поверхности и той же постановке
все отношения равны (в пределах статистики), при смещении/наклоне диска — 238 и 2614 расходятся.

    python edge_compare.py <edge93.xml> <edge1709.xml> [<edge93_bg0.xml> <edge1709_bg0.xml>]
"""
import io
import math
import re
import sys

import numpy as np
from scipy.optimize import least_squares
from scipy.ndimage import uniform_filter1d

sys.path.insert(0, r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p67-amber22\py')
from spec import Spec, rebin   # noqa: E402

LINES = [(238.632, 'Pb-212 238', 'Th'), (338.32, 'Ac-228 338', 'Th'), (583.187, 'Tl-208 583', 'Th'), (911.204, 'Ac-228 911', 'Th'),
         (2614.511, 'Tl-208 2614', 'Th'), (351.932, 'Pb-214 352', 'Rn'), (609.312, 'Bi-214 609', 'Rn'), (1764.49, 'Bi-214 1764', 'Rn'),
         (1460.82, 'K-40 1461', 'bg')]
FW662 = 0.0765 * 661.657


def fwhm_kev(e):
    return FW662 * math.sqrt(e / 661.657)


def centroid_ch(y, keV, e0):
    """центроид линии в каналах: гауссиана + линейная подложка в окне ±1.0 ПШПВ вокруг положения по шкале файла"""
    n = len(y); ch = np.arange(n, dtype=float)
    c0 = float(np.interp(e0, keV, ch)); g = float(np.interp(c0 + 1, ch, keV) - np.interp(c0 - 1, ch, keV)) / 2.0
    fw = fwhm_kev(e0) / g
    lo = int(max(0, c0 - 1.0 * fw)); hi = int(min(n - 101, c0 + 1.0 * fw))
    x = ch[lo:hi + 1]; yy = y[lo:hi + 1]; w = 1 / np.sqrt(np.maximum(np.abs(yy), 1))
    s0 = fw / 2.3548
    def f(p):
        c, s, b0, b1, A = p
        return (b0 + b1 * (x - c0) / fw + A * np.exp(-0.5 * ((x - c) / s) ** 2) - yy) * w
    r = least_squares(f, [c0, s0, yy.min(), 0, max(yy.max() - yy.min(), 1)], bounds=([c0 - 0.6 * fw, 0.6 * s0, -np.inf, -np.inf, 0], [c0 + 0.6 * fw, 1.6 * s0, np.inf, np.inf, np.inf]))
    return r.x[0], fw, r.x[4], r.x[1] * 2.3548


def window_area(y, c, fw, k=1.2):
    """нетто в окне ±k·ПШПВ с линейной подложкой по боковым окнам ширины 0.6·ПШПВ; возвращает (нетто, σ)"""
    n = len(y)
    a, b = int(round(c - k * fw)), int(round(c + k * fw))
    la, lb = int(round(a - 0.6 * fw)), a - 1
    ra, rb = b + 1, int(round(b + 0.6 * fw))
    if la < 0 or rb >= n:
        return float('nan'), float('nan')
    L = y[la:lb + 1].mean(); R = y[ra:rb + 1].mean()
    nl = lb - la + 1; nr = rb - ra + 1; nw = b - a + 1
    gross = y[a:b + 1].sum()
    base = 0.5 * (L + R) * nw
    net = gross - base
    # σ: пуассон по брутто (в отсчётах пробы; фон уже вычтен — берём |y|) + подложка
    var = np.abs(y[a:b + 1]).sum() + (0.5 * nw) ** 2 * (np.abs(y[la:lb + 1]).sum() / nl ** 2 + np.abs(y[ra:rb + 1]).sum() / nr ** 2)
    return net, math.sqrt(max(var, 1.0))


def analyse(path):
    S = Spec(path)
    net, bgc = S.net()
    out = {'live': S.live, 'total_cps': S.counts.sum() / S.live, 'bg_cps': bgc.sum() / S.live, 'net_cps': net.sum() / S.live, 'bg_live': S.bg.live if S.bg else float('nan')}
    for e0, name, kind in LINES:
        try:
            c, fw, A, fwm = centroid_ch(net if kind != 'bg' else S.counts, S.keV, e0)
        except Exception:
            c, fw, A, fwm = float(np.interp(e0, S.keV, np.arange(S.n))), fwhm_kev(e0) / 0.35, 0.0, 0.0
        a, sa = window_area(net, c, fw)
        araw, saraw = window_area(S.counts, c, fw)
        abg, sabg = window_area(bgc, c, fw)
        out[name] = dict(ch=c, E=float(np.interp(c, np.arange(S.n), S.keV)), fwhm_ratio=fwm / fw if fw else float('nan'),
                         net=a, sig=sa, cps=a / S.live, cps_sig=sa / S.live, raw_cps=araw / S.live, bg_cps=abg / S.live)
    return out


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    paths = sys.argv[1:]
    res = [(p, analyse(p)) for p in paths]
    print('%-46s %10s %10s %10s %10s %12s' % ('файл', 'живое, с', 'всего cps', 'фон cps', 'нетто cps', 'фон живое, с'))
    for p, r in res:
        print('%-46s %10.1f %10.2f %10.2f %10.2f %12.1f' % (p.split('\\')[-1][:46], r['live'], r['total_cps'], r['bg_cps'], r['net_cps'], r['bg_live']))
    print()
    print('%-13s | %s' % ('линия', ' | '.join('%-38s' % p.split('\\')[-1][:38] for p, _ in res)))
    print('%-13s | %s' % ('', ' | '.join('%8s %8s %8s %6s %5s' % ('канал', 'E файл', 'нетто cps', '±', 'w/w0') for _ in res)))
    for e0, name, kind in LINES:
        cells = []
        for p, r in res:
            x = r[name]
            cells.append('%8.1f %8.1f %8.4f %6.4f %5.2f' % (x['ch'], x['E'], x['cps'], x['cps_sig'], x['fwhm_ratio']))
        print('%-13s | %s' % (name, ' | '.join(cells)))
    if len(res) >= 2:
        print()
        print('отношения нетто cps: %s / %s' % (paths[1].split('\\')[-1][:40], paths[0].split('\\')[-1][:40]))
        for e0, name, kind in LINES:
            a = res[0][1][name]; b = res[1][1][name]
            if a['cps'] > 0 and b['cps'] > 0:
                ratio = b['cps'] / a['cps']
                sig = ratio * math.sqrt((a['cps_sig'] / a['cps']) ** 2 + (b['cps_sig'] / b['cps']) ** 2)
                print('  %-13s %6.3f ± %.3f   (нетто %8.4f → %8.4f cps; доля фона в окне брутто %4.0f %% / %4.0f %%)' % (name, ratio, sig, a['cps'], b['cps'], 100 * a['bg_cps'] / max(a['raw_cps'], 1e-9), 100 * b['bg_cps'] / max(b['raw_cps'], 1e-9)))
            else:
                print('  %-13s нетто %8.4f → %8.4f cps (± %.4f / %.4f) — отношение не берётся' % (name, a['cps'], b['cps'], a['cps_sig'], b['cps_sig']))
        th = [n for e, n, k in LINES if k == 'Th']
        r238 = res[1][1][th[0]]['cps'] / res[0][1][th[0]]['cps']; r2614 = res[1][1][th[4]]['cps'] / res[0][1][th[4]]['cps']
        s238 = math.hypot(res[0][1][th[0]]['cps_sig'] / res[0][1][th[0]]['cps'], res[1][1][th[0]]['cps_sig'] / res[1][1][th[0]]['cps'])
        s2614 = math.hypot(res[0][1][th[4]]['cps_sig'] / res[0][1][th[4]]['cps'], res[1][1][th[4]]['cps_sig'] / res[1][1][th[4]]['cps'])
        dd = r238 / r2614
        print('  двойное отношение (238/2614)_17.09 / (238/2614)_14.09 = %.3f ± %.3f' % (dd, dd * math.hypot(s238, s2614)))


if __name__ == '__main__':
    main()
