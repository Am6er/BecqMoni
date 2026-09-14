# -*- coding: utf-8 -*-
"""П59: гипотеза «прокачка ПРОДОЛЖАЛАСЬ во время съёмки спектра 1» — среднее Bi-214/Pb-214 по окну.
    python handover/p59-amber27/pumping_hypothesis.py
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import radon_physics as rp

hl, br = rp.halflives()
lA, lPb, lBi = rp.lam(hl['218PO']), rp.lam(hl['214PB']), rp.lam(hl['214BI'])
print('# прокачка идёт и во время съёмки (окно 1 = %.0f с); накопление до старта tau' % rp.T1)
for tau_h in (0.0, 0.5, 1.0, 2.0):
    for fB, fC in ((1, 1), (0.7, 0.5), (0.5, 0.3)):
        dep = [1 / lA, fB / lPb, fC / lBi]
        n, _ = rp.integrate_chain([lA, lPb, lBi], dep, [0, 0, 0], 1.0, int(tau_h * 3600))
        n2, dec = rp.integrate_chain([lA, lPb, lBi], dep, n, 1.0, int(rp.T1))
        print('tau=%.1fh air 1:%.1f:%.1f  mean Bi/Pb over window = %.3f' % (tau_h, fB, fC, dec[2] / dec[1]))
