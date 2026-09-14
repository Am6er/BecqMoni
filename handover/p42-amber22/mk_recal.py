# -*- coding: utf-8 -*-
"""П42 13.09.2026, `AMBER22` — вариант СПЕКТРА сцены Amber «шкала и ширина по ДАННЫМ» (абляция данных, не код):
энергетическая калибровка — полином 3-й степени через центроиды пиков ДАННЫХ (238.6 / 338.3 / 583.2 / 911.2 / 2614.5,
`groupfit.py`) плюс прежние точки низа (29.0 / 56.8 / 59.2 / 88.8 / 201.5 кэВ); калибровка ПШПВ — `SqrtFwhmCalibration`
(FWHM² = c + b·ch + a·ch²) через измеренные ширины тех же пиков. Меняются ОБЕ калибровки (проба и фон: один прибор).

    python handover/p42-amber22/mk_recal.py <стенд> [<dump.csv>]
Пишет три варианта: _recal (обе калибровки), _escale (только шкала), _sqrtfw (только ПШПВ).
Пишет три варианта: _recal (обе калибровки), _escale (только шкала), _sqrtfw (только ПШПВ).
"""
import csv
import math
import os
import re
import sys

import numpy as np
from scipy.optimize import least_squares

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
S2F = 2 * math.sqrt(2 * math.log(2))
sb = sys.argv[1]
dump = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'amber_p42', 'def', 'dump.csv')
src = os.path.join(sb, 'Th-232_amber.xml')
txt = open(src, encoding='utf-8', newline='').read()
coef = [float(v) for v in re.findall(r'<Coefficient>([-0-9.E+]+)</Coefficient>', re.search(r'<EnergyCalibration>.*?</EnergyCalibration>', txt, re.S).group(0))]
print('старая калибровка:', coef)


def E_old(ch):
    return sum(c * ch ** i for i, c in enumerate(coef))


rows = list(csv.DictReader(open(dump, encoding='utf-8-sig')))
kev = np.array([float(r['keV']) for r in rows])
net = np.array([float(r['net']) for r in rows])
ch = np.array([float(r['ch']) for r in rows])
GROUPS = [(210, 275, [(238.632, 43.6), (240.986, 4.1)]),
          (300, 380, [(338.32, 11.27), (328.0, 2.95), (332.37, 0.4), (340.96, 0.37)]),
          (530, 640, [(583.187, 30.5)]),
          (850, 1010, [(911.204, 25.8), (968.971, 15.8), (964.766, 4.99)]),
          (2450, 2800, [(2614.511, 35.85)])]
pts_e = [(109.0, 29.010134600044424), (191.0, 56.799547471261477), (198.0, 59.17779982447027), (285.0, 88.808420782843825),
         (613.0, 201.48810919008847)]
pts_f = []
for lo, hi, lines in GROUPS:
    ids = np.where((kev >= lo) & (kev < hi))[0]
    x, yy = kev[ids], net[ids]
    w = 1 / np.sqrt(np.maximum(yy, 1))
    E0 = lines[0][0]

    def f(p):
        A, sh, fw, a, b = p
        m = a + b * (x - E0)
        for E, I in lines:
            s = fw * math.sqrt(E / E0) / S2F
            m = m + A * I * np.exp(-0.5 * ((x - (E + sh * E / E0)) / s) ** 2) / s
        return (m - yy) * w

    r = least_squares(f, [yy.max() * 20, 0.0, 0.06 * E0, yy.min(), 0.0])
    A, sh, fw, a, b = r.x
    e_seen = E0 + sh                      # где линия E0 стоит в СТАРОЙ шкале
    c0 = float(np.interp(e_seen, kev, ch))  # канал этого места
    slope = (E_old(c0 + 1) - E_old(c0 - 1)) / 2.0
    if abs(E0 - 338.32) > 1.0:            # 338: центроид данных смещён правым плечом (346–362 кэВ), в шкалу не берётся
        pts_e.append((c0, E0))
    pts_f.append((c0, fw / slope))
    print('линия %8.3f: в старой шкале %.1f кэВ → канал %.1f; ПШПВ %.1f кэВ = %.1f кан' % (E0, e_seen, c0, fw, fw / slope))
X = np.array([p[0] for p in pts_e])
Y = np.array([p[1] for p in pts_e])
pe = np.polyfit(X, Y, 3)[::-1]          # c0..c3 (4-я степень без точек выше 2614 роняет наклон наверху до 0.22 кэВ/кан)
res = [(x, y, sum(c * x ** i for i, c in enumerate(pe)) - y) for x, y in pts_e]
print('новая калибровка (3-й степени):', list(pe))
for x, y, d in res:
    print('   ch %7.1f  E %8.3f  остаток %+.2f кэВ' % (x, y, d))
Xf = np.array([p[0] for p in pts_f])
Yf = np.array([p[1] for p in pts_f]) ** 2
pf = np.polyfit(Xf, Yf, 2)[::-1]        # c, b, a
print('новая ПШПВ (sqrt):', list(pf))
for x, y in pts_f:
    v = pf[0] + pf[1] * x + pf[2] * x * x
    print('   ch %7.1f  ПШПВ %6.1f кан → %6.1f (%+.1f %%)' % (x, y, math.sqrt(max(v, 0)), 100 * (math.sqrt(max(v, 0)) / y - 1)))
# запись: энергетическая калибровка — ОБА вхождения (проба и фон), порядок 4 сохранён (старший коэффициент 0)
new_e = '<PolynomialOrder>4</PolynomialOrder>\r\n          <Coefficients>\r\n' + ''.join(
    '            <Coefficient>%r</Coefficient>\r\n' % float(c) for c in list(pe) + [0.0]) + '          </Coefficients>'
def with_escale(t):
    out, n = re.subn(r'<PolynomialOrder>4</PolynomialOrder>\r?\n\s*<Coefficients>.*?</Coefficients>', lambda m: new_e, t, flags=re.S)
    print('энергетических калибровок заменено:', n)
    return out


def with_sqrtfw(t):
    m = re.search(r'<PowerFwhmCalibration>(.*?)</PowerFwhmCalibration>', t, re.S)
    body = m.group(1)
    body = re.sub(r'<Coefficients>.*?</Coefficients>', lambda mm: '<Coefficients>\r\n' + ''.join(
        '          <Coefficient>%r</Coefficient>\r\n' % float(c) for c in pf) + '        </Coefficients>', body, flags=re.S)
    return t[:m.start()] + '<SqrtFwhmCalibration>' + body + '</SqrtFwhmCalibration>' + t[m.end():]


for name, out in (('recal', with_sqrtfw(with_escale(txt))), ('escale', with_escale(txt)), ('sqrtfw', with_sqrtfw(txt))):
    p = os.path.join(sb, 'Th-232_amber_%s.xml' % name)
    open(p, 'w', encoding='utf-8', newline='').write(out)
    print('записано', p)
