# -*- coding: utf-8 -*-
"""П84 (AMBER42 п.3): ожидание углового множителя сумм-пика Co-60 из сайдкара .qk —
как в FsaCascadeSummer.AngularFactor: 1 + A22*Q2(E1)*Q2(E2) + A44*Q4(E1)*Q4(E2),
Q_k — линейная интерполяция по ln E между узлами (AngularAttenuation.Interpolate).
Запуск: python qk_expect.py <file.qk> [E1 E2 A22 A44]"""
import sys, math, io, os


def load(p):
    E = []; Q2 = []; Q4 = []; Q2T = []; Q4T = []
    for l in io.open(p, encoding='utf-8'):
        l = l.strip()
        if not l or l.startswith('#') or '=' in l:
            continue
        f = l.split()
        E.append(float(f[0])); Q2.append(float(f[1])); Q4.append(float(f[2]))
        Q2T.append(float(f[6])); Q4T.append(float(f[7]))
    return E, Q2, Q4, Q2T, Q4T


def interp(E, V, e):
    if e <= E[0]:
        return V[0]
    if e >= E[-1]:
        return V[-1]
    i = 1
    while i < len(E) - 1 and E[i] < e:
        i += 1
    x0, x1 = math.log(E[i - 1]), math.log(E[i])
    t = (math.log(e) - x0) / (x1 - x0)
    return V[i - 1] + (V[i] - V[i - 1]) * t


p = sys.argv[1]
e1 = float(sys.argv[2]) if len(sys.argv) > 2 else 1173.228
e2 = float(sys.argv[3]) if len(sys.argv) > 3 else 1332.492
a22 = float(sys.argv[4]) if len(sys.argv) > 4 else 0.1020
a44 = float(sys.argv[5]) if len(sys.argv) > 5 else 0.0091
E, Q2, Q4, Q2T, Q4T = load(p)
q21, q22 = interp(E, Q2, e1), interp(E, Q2, e2)
q41, q42 = interp(E, Q4, e1), interp(E, Q4, e2)
w = 1 + a22 * q21 * q22 + a44 * q41 * q42
name = os.path.join(os.path.basename(os.path.dirname(os.path.abspath(p))), os.path.basename(p))
print('%s: Q2(%.0f)=%.4f Q2(%.0f)=%.4f Q4=%.4f/%.4f -> W = 1 + %.4f*%.4f*%.4f + %.4f*%.4f*%.4f = %.4f'
      % (name, e1, q21, e2, q22, q41, q42, a22, q21, q22, a44, q41, q42, w))
print('   вынос (CF): 1173 партнёром 1332: %.4f; 1332 партнёром 1173: %.4f'
      % (1 + a22 * q21 * interp(E, Q2T, e2) + a44 * q41 * interp(E, Q4T, e2),
         1 + a22 * q22 * interp(E, Q2T, e1) + a44 * q42 * interp(E, Q4T, e1)))
