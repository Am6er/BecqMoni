# -*- coding: utf-8 -*-
"""П47 (A310) — НЕЙТРАЛЬНАЯ метрика для плеч сцены Amber: пуассоновский девианс 2·Σ[μ − y + y·ln(y/μ)] по дампу
FsaStackShot (`net` = отсчёты после вычитания фона, `model` = модель), в 15–2800 кэВ и на всех каналах.
Ни одно из плеч его не минимизирует прямо: веса по данным минимизируют Σr²/y (≈ отчётный χ²), веса по модели — Σr²/μ̂.
    python handover/p47-a310/deviance.py handover/p47-a310/amber_p47 def wdata wmodel
"""
import csv
import math
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def dev(kev, net, model, lo, hi):
    d = 0.0
    n = 0
    for e, y, m in zip(kev, net, model):
        if not (lo <= e < hi):
            continue
        m = max(m, 1e-9)
        y = max(y, 0.0)
        d += 2.0 * (m - y + (y * math.log(y / m) if y > 0 else 0.0))
        n += 1
    return d / max(n, 1), n


def main(argv):
    root = argv[0]
    print('%-10s %10s %10s %6s' % ('плечо', 'dev/n 15+', 'dev/n все', 'n'))
    for arm in argv[1:]:
        p = os.path.join(root, arm, 'dump.csv')
        rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
        kev = [float(r['keV']) for r in rows]
        net = [float(r['net']) for r in rows]
        model = [float(r['model']) for r in rows]
        a, n1 = dev(kev, net, model, 15, 2800)
        b, n2 = dev(kev, net, model, -1e9, 1e9)
        print('%-10s %10.3f %10.3f %6d' % (arm, a, b, n1))


if __name__ == '__main__':
    main(sys.argv[1:])
