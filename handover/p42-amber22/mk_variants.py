# -*- coding: utf-8 -*-
"""П42 13.09.2026, `AMBER22` — варианты СПЕКТРА сцены Amber для абляции формы пика (данные, не код):
коэффициент a калибровки ПШПВ (FWHM = a·ch^p) × k; тип пика 0 (гаусс без хвостов) вместо 1 (ExpGaussExp).

    python handover/p42-amber22/mk_variants.py <стенд>   → <стенд>/Th-232_amber_fw110.xml, _fw115, _fw120, _fw130, _gauss
"""
import os
import re
import sys

sb = sys.argv[1]
src = os.path.join(sb, 'Th-232_amber.xml')
txt = open(src, encoding='utf-8', newline='').read()
m = re.search(r'(<PowerFwhmCalibration>.*?<Coefficients>\s*<Coefficient>)([-0-9.E+]+)(</Coefficient>)', txt, re.S)
assert m, 'нет PowerFwhmCalibration'
a = float(m.group(2))
pt = re.search(r'(<PowerFwhmCalibration>.*?<PeakType>)(\d)(</PeakType>)', txt, re.S)
assert pt
print('a =', a, 'PeakType =', pt.group(2))
for k in (1.10, 1.15, 1.20, 1.30):
    out = txt[:m.start(2)] + repr(a * k) + txt[m.end(2):]
    p = os.path.join(sb, 'Th-232_amber_fw%d.xml' % round(k * 100))
    open(p, 'w', encoding='utf-8', newline='').write(out)
    print(p, 'a ->', a * k)
out = txt[:pt.start(2)] + '0' + txt[pt.end(2):]
p = os.path.join(sb, 'Th-232_amber_gauss.xml')
open(p, 'w', encoding='utf-8', newline='').write(out)
print(p, 'PeakType -> 0')
