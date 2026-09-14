# -*- coding: utf-8 -*-
"""П56: линии урана и радия в спектре диска — области и компоненты.
    python run_lines.py <спектр.xml> [<фон.xml>] [--kfix] [--quiet] [--tag=имя]"""
import sys
import pickle
sys.path.insert(0, r'D:\BqMoni_Claude\p56\py')
from spec import Spec
from eff import Eff
import recal
from ufit import Comp, fit_region

args = [a for a in sys.argv[1:] if not a.startswith('--')]
S = Spec(args[0], args[1] if len(args) > 1 else None)
eff = Eff(r'D:\BqMoni_Claude\p56\spectra\AS80_Th232Medal_corpus.xml')
print('== спектр', args[0], 'живое', round(S.live), 'фон', round(S.bg.live) if S.bg else None)
coef, ab, pts, fws = recal.recal(S, verbose='--quiet' not in sys.argv)
kfree = '--kfix' not in sys.argv
# --- компоненты (энергии/интенсивности nucdb; интенсивности рентгена — на распад родителя) ---
U235 = Comp('U-235', [(143.76, 10.96), (163.36, 5.08), (185.72, 57.0), (202.1, 1.08), (205.31, 5.02)], ref=185.72)
L186 = Comp('186 свободная', [(185.9, 100.0)])
AC_lo = Comp('Ac-228 (209/215)', [(209.25, 3.89), (214.85, 0.76), (199.4, 0.32)], ref=209.25)
PA = Comp('Pa-234m 1001', [(1001.03, 0.842)])
AC_hi = Comp('Ac-228 911/965/969', [(911.2, 25.8), (964.77, 4.99), (968.97, 15.8), (904.2, 0.77)], ref=911.2)
TL860 = Comp('Tl-208 860', [(860.56, 12.5)])
BI1079 = Comp('Bi-212 1079', [(1078.6, 0.56)])
BI1120 = Comp('Bi-214 1120', [(1120.3, 14.9), (1155.2, 1.63)], ref=1120.3)
SUM1094 = Comp('сумм 511+583', [(1094.0, 1.0)])
BI1764 = Comp('Bi-214 1764', [(1764.5, 15.29), (1729.6, 2.87), (1847.4, 2.03)], ref=1764.5)
AC1588 = Comp('Ac-228 1588/1631', [(1588.2, 3.22), (1630.6, 1.51), (1638.3, 0.47)], ref=1588.2)
BI1620 = Comp('Bi-212 1620', [(1620.5, 1.47)])
DE2614 = Comp('DE 2614 (1592)', [(1592.5, 1.0)])
TL583 = Comp('Tl-208 583', [(583.19, 85.0)])
BI609 = Comp('Bi-214 609', [(609.31, 45.44), (665.45, 1.54)], ref=609.31)
AC562 = Comp('Ac-228 562', [(562.5, 0.87), (572.1, 0.15)], ref=562.5)
PB214 = Comp('Pb-214 295/352', [(295.22, 18.47), (351.93, 35.72), (258.9, 0.53)], ref=351.93)
AC338 = Comp('Ac-228 338 группа', [(338.32, 11.27), (328.0, 2.95), (332.37, 0.4), (340.96, 0.37)], ref=338.32)
AC270 = Comp('Ac-228 270', [(270.24, 3.46)])
TL277 = Comp('Tl-208 277', [(277.36, 6.6)])
PB300 = Comp('Pb-212 300', [(300.09, 3.3)])
BI288 = Comp('Bi-212 288', [(288.2, 0.34)])
RA241 = Comp('Pb-212 238 + Ra-224 241', [(238.63, 43.6), (240.99, 4.1)], ref=238.63)
TH234 = Comp('Th-234 63', [(63.29, 3.67)])
TH234b = Comp('Th-234 92', [(92.38, 2.13), (92.8, 2.1)], ref=92.38)
XPB = Comp('Pb K (72.8/75.0/84.9)', [(72.80, 100.0), (74.97, 168.0), (84.94, 58.0), (87.3, 20.0)], ref=74.97)
XBI = Comp('Bi K (74.8/77.1/87.3)', [(74.815, 10.03), (77.108, 16.78), (87.388, 5.75), (88.458, 7.5), (89.784, 1.76)], ref=77.108)
XTH = Comp('Th K (90.0/93.4/105.6)', [(89.954, 3.49), (93.347, 5.65), (105.566, 2.01), (106.894, 2.69), (108.58, 0.68)], ref=93.347)
TH228_84 = Comp('Th-228 84.4', [(84.37, 1.19)])
AC99 = Comp('Ac-228 99.5', [(99.5, 1.26)])
AC129 = Comp('Ac-228 129', [(129.07, 2.42)])
PB115 = Comp('Pb-212 115', [(115.18, 0.6)])
TL2614 = Comp('Tl-208 2614', [(2614.51, 35.85)])
res = {}
print('\n### U-235 185.7 (+143.8/163.4/205.3, связка) — область 140–228')
res['A1'] = fit_region(S, ab, eff, 140, 228, [U235, AC_lo], deg=3, kfree=kfree, label='U-235 связкой')
res['A2'] = fit_region(S, ab, eff, 150, 228, [L186, AC_lo], deg=3, kfree=kfree, label='186 свободная')
res['A3'] = fit_region(S, ab, eff, 150, 228, [L186, AC_lo], deg=2, kfree=kfree, label='186 свободная, подложка 2')
print('\n### Pa-234m 1001 — область 840–1180')
res['B1'] = fit_region(S, ab, eff, 840, 1180, [TL860, AC_hi, PA, BI1079, SUM1094, BI1120], deg=3, kfree=kfree, label='1001 с Ac-228 связкой')
res['B2'] = fit_region(S, ab, eff, 840, 1060, [TL860, AC_hi, PA, BI1079], deg=2, kfree=kfree, label='1001, узко')
print('\n### Bi-214 1764 — область 1640–1960')
res['C1'] = fit_region(S, ab, eff, 1640, 1960, [BI1764], deg=2, kfree=kfree, label='1764')
res['C2'] = fit_region(S, ab, eff, 1500, 1960, [AC1588, BI1620, DE2614, BI1764], deg=3, kfree=kfree, label='1764 с 1588/1620/DE')
print('\n### Bi-214 1120 — область 1040–1250')
res['D1'] = fit_region(S, ab, eff, 1040, 1250, [BI1079, SUM1094, BI1120], deg=2, kfree=kfree, label='1120')
print('\n### Bi-214 609 — область 520–700')
res['E1'] = fit_region(S, ab, eff, 520, 700, [AC562, TL583, BI609], deg=3, kfree=kfree, label='609 на плече 583')
res['E2'] = fit_region(S, ab, eff, 520, 700, [AC562, TL583], deg=3, kfree=kfree, label='без 609 (контроль χ²)')
print('\n### Pb-214 295/352 — область 255–400')
res['F1'] = fit_region(S, ab, eff, 255, 400, [AC270, TL277, BI288, PB300, AC338, PB214], deg=3, kfree=kfree, label='352/295 связкой')
res['F2'] = fit_region(S, ab, eff, 255, 400, [AC270, TL277, BI288, PB300, AC338], deg=3, kfree=kfree, label='без Pb-214 (контроль χ²)')
print('\n### Th-234 63.3 / 92.6 — область 50–140 (рентген Pb/Bi/Th, Th-228 84, Ac-228 99/129)')
res['G1'] = fit_region(S, ab, eff, 50, 140, [TH234, TH234b, XPB, XBI, XTH, TH228_84, AC99, PB115, AC129], deg=2, kfree=kfree, label='низ')
res['G2'] = fit_region(S, ab, eff, 50, 140, [XPB, XBI, XTH, TH228_84, AC99, PB115, AC129], deg=2, kfree=kfree, label='низ без Th-234 (контроль χ²)')
print('\n### опоры Th-232: 911 (Ac-228), 2614 (Tl-208), 238 (Pb-212)')
res['H1'] = fit_region(S, ab, eff, 2440, 2740, [TL2614], deg=1, kfree=kfree, label='2614')
res['H2'] = fit_region(S, ab, eff, 200, 290, [RA241, AC_lo, TL277, AC270], deg=2, kfree=kfree, label='238')
tag = [a for a in sys.argv if a.startswith('--tag=')]
if tag:
    slim = {k: {kk: vv for kk, vv in v.items() if kk not in ('model', 'x', 'y')} for k, v in res.items()}
    slim['coef'] = coef
    slim['ab'] = ab
    slim['live'] = S.live
    pickle.dump(slim, open(r'D:\BqMoni_Claude\p56\lines_%s.pkl' % tag[0][6:], 'wb'))
