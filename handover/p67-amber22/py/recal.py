# -*- coding: utf-8 -*-
"""П56: шкала спектра ПО ЕГО ПИКАМ (по мотивам mk_recal.py П42): группы линий ряда Th-232 подгоняются В КАНАЛАХ
суммой гауссиан (табличные энергии/интенсивности, общий центроид E0, общая ширина, квадратичная подложка) к net;
из центроидов — полином ch→кэВ степени 3; ПШПВ(E)² = a + b·E по чистым пикам (238, 583, 911, 2614).
Начальная шкала — шкала файла, если 238 и 2614 стоят в ней в ±4 %, иначе две опоры по максимумам."""
import math, sys, numpy as np
from scipy.optimize import least_squares
from scipy.ndimage import uniform_filter1d
sys.path.insert(0, r'D:\BqMoni_Claude\p56\py')
from spec import Spec, rebin
S2F = 2*math.sqrt(2*math.log(2))
GROUPS = [('238', 205, 275, [(238.632, 43.6), (240.986, 4.1)]),
          ('338', 300, 380, [(338.32, 11.27), (328.0, 2.95), (332.37, 0.4), (340.96, 0.37)]),
          ('583', 530, 640, [(583.187, 30.5)]),
          ('911/969', 850, 1010, [(911.204, 25.8), (968.971, 15.8), (964.766, 4.99)]),
          ('1588', 1520, 1720, [(1588.2, 3.22), (1630.6, 1.51), (1620.5, 1.47)]),
          ('2614', 2450, 2740, [(2614.511, 35.85)])]
CLEAN = (238.632, 583.187, 911.204, 2614.511)
def scale_of(coef, ch): return sum(c*np.asarray(ch, float)**i for i, c in enumerate(coef))
def apply(S, coef):
    S.keV = scale_of(coef, np.arange(S.n)); S.keVb = scale_of(coef, np.arange(S.n+1)-0.5)
def fit_group_ch(net, coef0, lo, hi, lines):
    """подгонка группы в каналах; окно [lo,hi] кэВ по начальной шкале coef0; возвращает (канал E0, ширина кэВ, χ²/n, A)"""
    E0 = lines[0][0]
    kev0 = scale_of(coef0, np.arange(len(net)))
    ids = np.where((kev0 >= lo) & (kev0 < hi) & (np.arange(len(net)) < len(net)-100))[0]   # верх АЦП (переполнение) не брать
    x = ids.astype(float); yy = net[ids]
    w = 1/np.sqrt(np.maximum(yy, 1))
    c0 = float(np.interp(E0, kev0, np.arange(len(net))))
    g = (kev0[ids[-1]]-kev0[ids[0]])/(ids[-1]-ids[0])      # кэВ/канал в окне
    def f(p):
        A, cc, fwk, a, b, c = p     # fwk — ПШПВ в кэВ на E0
        m = a + b*(x-cc) + c*(x-cc)**2
        for E, I in lines:
            s = fwk*math.sqrt(E/E0)/S2F/g
            m = m + A*I*np.exp(-0.5*((x-(cc+(E-E0)/g))/s)**2)/s
        return (m-yy)*w
    lb = [0.0, x[0], 0.02*E0, -np.inf, -np.inf, -np.inf]; ub = [np.inf, x[-1], 0.14*E0, np.inf, np.inf, np.inf]
    c0 = min(max(c0, x[0]+1), x[-1]-1)
    r = least_squares(f, [max(yy.max(),1)*20/g, c0, 0.07*E0, max(yy.min(),0.0), 0.0, 0.0], bounds=(lb, ub))
    A, cc, fwk, a, b, c = r.x
    return cc, abs(fwk), (r.fun**2).sum()/max(len(x)-6,1), A, g
def fwhm(ab, E): return math.sqrt(max(ab[0]+ab[1]*E, 1.0))
def fwhm_narrow(S, net, E0, lines, half=1.3):
    """ПШПВ пика узким окном ±half·1.6·ПШПВ₀ с ЛИНЕЙНОЙ подложкой (как groupfit.py П42)"""
    fw0 = 0.07*E0*math.sqrt(math.sqrt(662/E0))
    lo, hi = E0-half*fw0*1.6, E0+half*fw0*1.6 + (60 if 900 < E0 < 1000 else 0)
    ids = np.where((S.keV >= lo) & (S.keV < hi) & (np.arange(S.n) < S.n-100))[0]
    x = S.keV[ids]; y = net[ids]; w = 1/np.sqrt(np.maximum(y, 1))
    def fm(p):
        A, sh, fw, a, b = p; m = a + b*(x-E0)
        for E, I in lines:
            s = fw*math.sqrt(E/E0)/S2F; m = m + A*I*np.exp(-0.5*((x-(E+sh))/s)**2)/s
        return (m-y)*w
    r = least_squares(fm, [max(y.max(), 1)*20, 0, fw0, max(y.min(), 0), 0])
    return abs(r.x[2])
def recal(S, deg=3, verbose=True, groups=GROUPS):
    net, _ = S.net()
    sm = uniform_filter1d(net, 9); ch = np.arange(S.n)
    def argmax_in(lo, hi):
        ids = np.where((S.keV >= lo) & (S.keV < hi) & (ch < S.n-100))[0]
        return ids[np.argmax(sm[ids])]
    c238 = argmax_in(190, 280); c2614 = argmax_in(2200, 3000)
    e238, e2614 = S.keV[c238], S.keV[c2614]
    if abs(e238/238.632-1) < 0.04 and abs(e2614/2614.511-1) < 0.04:
        coef0 = list(S.coef)
        if verbose: print('  начальная шкала — файла (238 на %.1f, 2614 на %.1f кэВ)' % (e238, e2614))
    else:
        g = (2614.511-238.632)/(c2614-c238); z = 238.632-g*c238; coef0 = [z, g]
        if verbose: print('  начальная шкала — две опоры: 238 в канале %d, 2614 в канале %d → %.4f кэВ/кан, ноль %.2f' % (c238, c2614, g, z))
    pts = []; fws = []
    for it in range(2):   # второй проход — окна по уже исправленной шкале
        pts = []; fws = []
        for label, lo, hi, lines in groups:
            try:
                cc, fwk, chi, A, g = fit_group_ch(net, coef0, lo, hi, lines)
            except Exception as e:
                if verbose: print('  группа', label, 'не подогнана:', e); continue
            pts.append((cc, lines[0][0])); fws.append((lines[0][0], fwk))
            if verbose and it == 1: print('  %-8s канал %8.2f  (по шкале файла %7.1f кэВ, %+5.2f %%)  ПШПВ %6.2f кэВ (%5.2f %%)  χ²/n %6.2f' % (label, cc, scale_of(S.coef, cc), 100*(scale_of(S.coef, cc)/lines[0][0]-1), fwk, 100*fwk/lines[0][0], chi))
        chs = np.array([p[0] for p in pts]); E = np.array([p[1] for p in pts])
        coef0 = list(np.polyfit(chs, E, deg)[::-1])
    coef = coef0
    res = E - scale_of(coef, chs)
    if verbose: print('  полином степени %d: %s; остатки, кэВ: %s' % (deg, ' '.join('%.6g' % c for c in coef), ' '.join('%+.2f' % r for r in res)))
    apply(S, coef)
    fws = [(E0, fwhm_narrow(S, net, E0, lines)) for E0, lines in [(238.632, [(238.632, 43.6), (240.986, 4.1)]), (583.187, [(583.187, 1.0)]), (911.204, [(911.204, 25.8), (968.971, 15.8), (964.766, 4.99)]), (2614.511, [(2614.511, 1.0)])]]
    if verbose: print('  ПШПВ узкими окнами (лин. подложка): ' + ', '.join('%.0f: %.1f кэВ (%.2f %%)' % (E0, fw, 100*fw/E0) for E0, fw in fws))
    Ef = np.array([f[0] for f in fws]); F = np.array([f[1] for f in fws])
    ab = list(np.polyfit(Ef, F**2, 1)[::-1])
    if verbose: print('  ПШПВ² = %.2f + %.4f·E; ПШПВ(662) = %.2f кэВ (%.2f %%); ПШПВ(186) = %.2f, (1001) = %.2f, (1764) = %.2f' % (ab[0], ab[1], fwhm(ab,662), 100*fwhm(ab,662)/662, fwhm(ab,186), fwhm(ab,1001), fwhm(ab,1764)))
    apply(S, coef)
    if S.bg is not None: apply(S.bg, coef)   # один прибор: фон в той же шкале (как mk_recal.py П42)
    return coef, ab, pts, fws
if __name__ == '__main__':
    S = Spec(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
    print('старая калибровка', S.coef)
    recal(S)
