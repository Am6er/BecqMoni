# -*- coding: utf-8 -*-
# bqp12_control2.py — контроль 2: χ² плеча А на шаге 0 (истина Б) двумя независимыми путями.
#   путь 1: Σ w·(y − m_A0)², y — из ФАЙЛА истины (truth/<key>_truth.csv, писал bqp12_synth.py),
#           m_A0 = Σ a_B·c_A — амплитуды истины на колонках дампа плеча А;
#   путь 2: ‖Δm‖²_W, Δm = m_A0 − m_B, где m_B — столбец `model` приложения из `_chi.csv` плеча Б
#           (печать F3, независимо от дампа колонок Б).
import csv, io, os, sys
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
OUT = r'C:\Users\moroz\bqp12_out'
KEYS = ['G1S16_Cs137_P5', 'G1S16_Am241_P5', 'G1S16_Ba133_P5', 'G1S24_Ba133_P5', 'G1S24_Bi207_P5', 'G1S24_Am241_P5']
for key in KEYS:
    tr = list(csv.DictReader(io.open(os.path.join(OUT, 'truth', key + '_truth.csv'), encoding='utf-8-sig')))
    y = np.array([float(r['mu_model']) for r in tr]); w = np.array([float(r['w']) for r in tr])
    win = np.array([r['in_window'] == '1' for r in tr]); w = np.where(win, w, 0.0)
    ampsB = {a['name']: float(a['amp']) for a in csv.DictReader(io.open(os.path.join(OUT, 'b_dump', key + '_amps.csv'), encoding='utf-8-sig'))}
    ampsA = list(csv.DictReader(io.open(os.path.join(OUT, 'a_dump', key + '_amps.csv'), encoding='utf-8-sig')))
    rowsA = list(csv.DictReader(io.open(os.path.join(OUT, 'a_dump', key + '_cols.csv'), encoding='utf-8-sig')))
    mA0 = np.zeros(len(y))
    for a in ampsA:
        x = ampsB.get(a['name'], 0.0)
        if x > 0:
            mA0 += x * np.array([float(r[a['name']]) for r in rowsA])
    chiB = list(csv.DictReader(io.open(os.path.join(OUT, 'b_dump', key + '_chi.csv'), encoding='utf-8-sig')))
    mB_app = np.array([float(r['model']) for r in chiB])
    p1 = float(((y - mA0) ** 2 * w).sum())
    p2 = float(((mA0 - mB_app) ** 2 * w).sum())
    print('%-16s путь 1 χ²_A(0) = %.4f   путь 2 ‖Δm‖²_W = %.4f   отн. расхождение %.2e   (max|y − model_app| = %.4g)'
          % (key, p1, p2, abs(p1 - p2) / p1, np.abs(y - mB_app).max()))
