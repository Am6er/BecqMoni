# -*- coding: utf-8 -*-
r"""Сверка Q_k МАТРИЦЫ (формат 9, блок ANGK) с САЙДКАРОМ `.qk` П83 (снимок склада) — контроль (б)
П87: на каждом узле сайдкара (24 узла 30…3000 кэВ) матрица интерполируется по ln E, и
|Δ| судится в σ обеих: σ = sqrt(dQ_сайдкара² + dQ_матрицы²). Ожидание — все узлы в 3σ.
Заодно — Geant4 П85 на G1S_point5 (контроль (в)): Q2/Q4 при 1173/1332 против 0.8832/0.6504 и
0.8848/0.6549, отклонение в %.

  python cmp_qk_sidecar.py <матрица.rmx> <сайдкар.qk> [--g4]
Код 0 — все узлы в 3σ; 1 — есть узел вне 3σ. Разделитель дробной части — точка.
"""
import io
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rmx_qk  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

G4 = {1173.0: (0.8832, 0.6504), 1332.0: (0.8848, 0.6549)}


def read_qk(path):
    rows = []
    head = {}
    for raw in io.open(path, encoding='utf-8'):
        line = raw.strip()
        if not line or line[0] == '#':
            continue
        if '=' in line and not line[0].isdigit():
            k, v = line.split('=', 1)
            head[k] = v
            continue
        p = line.split()
        rows.append([float(x) for x in p])
    return head, rows


def main(argv):
    mpath, spath = argv[0], argv[1]
    g4 = '--g4' in argv
    m = rmx_qk.read(mpath)
    assert m['qk'] is not None, 'у матрицы нет блока ANGK'
    es = m['energies']
    mq = {k: [r[i] for r in m['qk']] for k, i in (('q2', 0), ('q4', 1), ('dq2', 2), ('dq4', 3), ('q2t', 4), ('q4t', 5), ('dq2t', 6), ('dq4t', 7), ('eps', 8))}
    head, rows = read_qk(spath)
    print('матрица %s (формат %d, %d узлов); сайдкар %s (%s, historiй %s, %d узлов)' % (
        os.path.basename(mpath), m['format'], m['nodes'], os.path.basename(spath), head.get('matrix', '')[:20], head.get('histories', '?'), len(rows)))
    print('  %8s | %8s %8s %8s %6s | %8s %8s %8s %6s | %8s %8s %6s | %8s %8s %6s | %9s %9s %7s' % (
        'E', 'Q2 side', 'Q2 matr', 'dQ2 s', 'z', 'Q4 side', 'Q4 matr', 'dQ4 s', 'z', 'Q2T side', 'Q2T matr', 'z', 'Q4T side', 'Q4T matr', 'z', 'eps side', 'eps matr', 'd%'))
    worst = 0.0
    worst_e = 0.0
    bad = 0
    for r in rows:
        e, q2, q4, d2, d4, eps = r[0], r[1], r[2], r[3], r[4], r[5]
        q2t, q4t, d2t, d4t = (r[6], r[7], r[8], r[9]) if len(r) > 10 else (q2, q4, d2, d4)
        zs = []
        for side, dside, key, dkey in ((q2, d2, 'q2', 'dq2'), (q4, d4, 'q4', 'dq4'), (q2t, d2t, 'q2t', 'dq2t'), (q4t, d4t, 'q4t', 'dq4t')):
            mv = rmx_qk.interp(es, mq[key], e)
            md = rmx_qk.interp(es, mq[dkey], e)
            s = math.sqrt(dside * dside + md * md)
            z = abs(mv - side) / s if s > 0 else (0.0 if mv == side else float('inf'))
            zs.append((mv, z))
            if z > worst:
                worst, worst_e = z, e
        me = rmx_qk.interp(es, mq['eps'], e)
        print('  %8.2f | %8.4f %8.4f %8.4f %6.2f | %8.4f %8.4f %8.4f %6.2f | %8.4f %8.4f %6.2f | %8.4f %8.4f %6.2f | %9.4g %9.4g %7.2f' % (
            e, q2, zs[0][0], d2, zs[0][1], q4, zs[1][0], d4, zs[1][1], q2t, zs[2][0], zs[2][1], q4t, zs[3][0], zs[3][1], eps, me, 100.0 * (me - eps) / eps if eps else 0.0))
        if max(z for _, z in zs) > 3.0:
            bad += 1
    print('узлов сайдкара %d, вне 3σ %d; худшее %.2f σ при %.1f кэВ' % (len(rows), bad, worst, worst_e))
    if g4:
        for e, (g2, g4v) in sorted(G4.items()):
            q2 = rmx_qk.interp(es, mq['q2'], e)
            q4 = rmx_qk.interp(es, mq['q4'], e)
            print('Geant4 П85 при %.0f кэВ: Q2 матрица %.4f против %.4f (%+.2f %%), Q4 %.4f против %.4f (%+.2f %%)' % (
                e, q2, g2, 100.0 * (q2 - g2) / g2, q4, g4v, 100.0 * (q4 - g4v) / g4v))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
