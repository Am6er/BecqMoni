# -*- coding: utf-8 -*-
r"""П85: множитель «по рукам» 1 + Σ A_kk·Q_k(E1)·Q_k(E2) из таблицы .qk (интерполяция линейно по ln E).
    python by_hand.py <файл .qk>
"""
import io, math, sys

def load(path):
    rows = []
    for line in io.open(path, encoding='utf-8'):
        if line.startswith('#') or '=' in line: continue
        p = line.split()
        if len(p) >= 5: rows.append((float(p[0]), float(p[1]), float(p[2])))
    return rows

def q(rows, e, k):
    xs = [math.log(r[0]) for r in rows]; x = math.log(e)
    col = 1 if k == 2 else 2
    if x <= xs[0]: return rows[0][col]
    if x >= xs[-1]: return rows[-1][col]
    for i in range(len(xs) - 1):
        if xs[i] <= x <= xs[i + 1]:
            f = (x - xs[i]) / (xs[i + 1] - xs[i])
            return rows[i][col] + f * (rows[i + 1][col] - rows[i][col])

CASES = [
    ('Co-60  1173+1332 (4→2→0, E2/E2)', 1173.23, 1332.49, 0.1020, 0.0091),
    ('Cs-134  605+796 (4→2→0, E2/E2)', 604.72, 795.86, 0.1020, 0.0091),
    ('Y-88    898+1836 (3→2→0, E1/E2)', 898.04, 1836.06, -0.0714, 0.0),
]
rows = load(sys.argv[1])
sys.stdout.reconfigure(encoding='utf-8')
for name, e1, e2, a22, a44 in CASES:
    q21, q22, q41, q42 = q(rows, e1, 2), q(rows, e2, 2), q(rows, e1, 4), q(rows, e2, 4)
    w = 1 + a22 * q21 * q22 + a44 * q41 * q42
    print('%-36s Q2 %.4f/%.4f Q4 %.4f/%.4f  → 1 + %.4f·%.4f + %.4f·%.4f = %.4f' % (name, q21, q22, q41, q42, a22, q21 * q22, a44, q41 * q42, w))
