# -*- coding: utf-8 -*-
"""П31 12.09.2026, `A308` — читатель дампа `FsaNnlsDumpProbe` (`calls.bin`) + арбитры.

    python handover/p31-a308-nnls/read_calls.py <каталог с calls.bin> [номера вызовов]

На каждый вызов: m, итерации, наш x; арбитры на ТОМ ЖЕ Граме — (1) scipy.optimize.nnls на
квадратном корне Грама (G = VΛVᵀ, A = Λ^½Vᵀ, b = Λ^-½Vᵀc), (2) наш алгоритм, переписанный
дословно (порог 1e-10·maxdiag, бюджет 30·m, Гаусс с порогом 1e-30) — контроль, что читаем тот же
Грам и тот же x, (3) KKT на нашем x. Печатает разность целей f = ½xᵀGx − cᵀx (Δχ² = 2Δf).
"""
import os
import struct
import sys

import numpy as np
from scipy.optimize import nnls

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def read_calls(path):
    data = open(path, 'rb').read()
    pos = 0
    calls = []
    while pos < len(data):
        m = struct.unpack_from('<i', data, pos)[0]
        pos += 4
        gram = np.frombuffer(data, dtype='<f8', count=m * m, offset=pos).reshape(m, m).copy()
        pos += 8 * m * m
        c = np.frombuffer(data, dtype='<f8', count=m, offset=pos).copy()
        pos += 8 * m
        x = np.frombuffer(data, dtype='<f8', count=m, offset=pos).copy()
        pos += 8 * m
        active = np.frombuffer(data, dtype='u1', count=m, offset=pos).astype(bool)
        pos += m
        banned = np.frombuffer(data, dtype='u1', count=m, offset=pos).astype(bool)
        pos += m
        tol, iterations, budget, drops = struct.unpack_from('<diii', data, pos)
        pos += 8 + 12
        calls.append(dict(m=m, gram=gram, c=c, x=x, active=active, banned=banned, tol=tol,
                          iterations=iterations, budget=budget, drops=drops))
    return calls


def objective(gram, c, x):
    return 0.5 * x @ gram @ x - c @ x


def kkt(gram, c, x, active):
    w = c - gram @ x
    inactive = ~active
    return (w[inactive].max() if inactive.any() else 0.0,
            np.abs(w[active]).max() if active.any() else 0.0)


def sqrt_factor(gram, c, rel=1e-14):
    lam, v = np.linalg.eigh(gram)
    keep = lam > rel * lam.max()
    a = (np.sqrt(lam[keep])[:, None] * v[:, keep].T)
    b = (v[:, keep].T @ c) / np.sqrt(lam[keep])
    return a, b, int(keep.sum())


def ours(gram, c, tol_factor=1e-10, budget_factor=30, pivot=1e-30, guard=False):
    """Дословный перевод `FsaAnalyzer.NnlsSolve` + `SolveActive`/`GaussSolve`."""
    m = len(c)
    x = np.zeros(m)
    active = np.zeros(m, bool)
    banned = np.zeros(m, bool)
    tol = tol_factor * max(gram.diagonal().max(), 0.0) if gram.diagonal().max() > 0 else tol_factor
    w = c.copy()
    it = 0
    drops = 0
    budget = budget_factor * m
    while it < budget:
        cand = np.where(~active & ~banned & (w > tol))[0]
        if len(cand) == 0:
            break
        j = cand[np.argmax(w[cand])]
        active[j] = True
        while True:
            idx = np.where(active)[0]
            a = np.zeros((len(idx), len(idx) + 1))
            a[:, :-1] = gram[np.ix_(idx, idx)]
            a[:, -1] = c[idx]
            ok = gauss(a, len(idx), pivot)
            if not ok:
                active[j] = False
                banned[j] = True
                break
            z = np.zeros(m)
            z[idx] = a[:, -1]
            neg = active & (z <= 0.0)
            if not neg.any():
                x = np.where(active, z, 0.0)
                break
            alpha = min(1.0, np.min(x[neg] / (x[neg] - z[neg])))
            for k in idx:
                x[k] += alpha * (z[k] - x[k])
                if x[k] <= tol:
                    x[k] = 0.0
                    active[k] = False
            if not active[j]:
                drops += 1
                if guard:
                    banned[j] = True
                    break
        w = c - gram @ x
        it += 1
    return x, active, banned, it, drops


def gauss(a, n, pivot):
    for col in range(n):
        p = col + np.argmax(np.abs(a[col:, col]))
        if abs(a[p, col]) < pivot:
            return False
        if p != col:
            a[[col, p], col:] = a[[p, col], col:]
        for r in range(n):
            if r == col:
                continue
            f = a[r, col] / a[col, col]
            if f == 0.0:
                continue
            a[r, col:] -= f * a[col, col:]
    for r in range(n):
        a[r, n] /= a[r, r]
    return True


def main(argv):
    folder = argv[0]
    calls = read_calls(os.path.join(folder, 'calls.bin'))
    which = [int(s) for s in argv[1:]] or list(range(1, len(calls) + 1))
    print('%4s %3s %4s %5s %12s %12s %12s %10s %10s %8s %8s' % (
        'call', 'm', 'iter', 'act', 'f(ours)', 'f(scipy)', 'f(re-ours)', 'Δf sci', 'KKT max w', '|w|act', 'rank'))
    for n in which:
        cl = calls[n - 1]
        g, c, x = cl['gram'], cl['c'], cl['x']
        f_ours = objective(g, c, x)
        a, b, rank = sqrt_factor(g, c)
        xs, _ = nnls(a, b, maxiter=50 * cl['m'])
        f_sci = objective(g, c, xs)
        xr, ar, br, it, drops = ours(g, c)
        f_re = objective(g, c, xr)
        kmax, kact = kkt(g, c, x, cl['active'])
        print('%4d %3d %4d %5d %12.3f %12.3f %12.3f %10.4f %10.2e %8.1e %8d' % (
            n, cl['m'], cl['iterations'], cl['active'].sum(), f_ours, f_sci, f_re, f_ours - f_sci, kmax, kact, rank))
        if it != cl['iterations'] or np.abs(xr - x).max() > 1e-6 * max(1.0, np.abs(x).max()):
            print('     ⚠ перевод разошёлся с решателем: итераций %d против %d, max|Δx| %.3g' % (
                it, cl['iterations'], np.abs(xr - x).max()))


if __name__ == '__main__':
    main(sys.argv[1:])
