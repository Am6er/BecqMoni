# -*- coding: utf-8 -*-
"""П56: положительный контроль — урановое стекло Amber (UGlass 28.08.2025, тот же AS80): 186 / 1001 / 766 / 63-92 / ряд Ra-226.
Шкала — линейная по двум своим пикам (186, 1001); кривая ДИСКА только как масштаб связок линий, не для активности."""
import sys, math, numpy as np
sys.path.insert(0, r'D:\BqMoni_Claude\p56\py')
from spec import Spec
from eff import Eff
from scipy.ndimage import uniform_filter1d
import recal
from ufit import Comp, fit_region
S = Spec(r'D:\BqMoni_Claude\p56\spectra\UGlass_28.08.2025.xml')
print('UGlass live', round(S.live), 'bg', round(S.bg.live), 'counts', S.counts.sum(), 'cps %.1f' % (S.counts.sum() / S.live))
net, _ = S.net(); sm = uniform_filter1d(net, 9)
def argmax_in(lo, hi):
    ids = np.where((S.keV >= lo) & (S.keV < hi) & (np.arange(S.n) < S.n - 100))[0]; return ids[np.argmax(sm[ids])]
c186 = argmax_in(150, 230); c1001 = argmax_in(880, 1100)
g = (1001.03 - 185.72) / (c1001 - c186); z = 185.72 - g * c186; recal.apply(S, [z, g]); recal.apply(S.bg, [z, g])
print('линейная шкала по 186/1001: %.4f кэВ/кан, ноль %.2f' % (g, z))
fw186 = recal.fwhm_narrow(S, net, 185.72, [(185.72, 1.0)]); fw1001 = recal.fwhm_narrow(S, net, 1001.03, [(1001.03, 1.0)])
print('ПШПВ 186: %.1f кэВ (%.1f %%), 1001: %.1f кэВ (%.1f %%)' % (fw186, 100 * fw186 / 185.72, fw1001, 100 * fw1001 / 1001))
ab = list(np.polyfit([185.72, 1001.03], [fw186 ** 2, fw1001 ** 2], 1)[::-1])
eff = Eff(r'D:\BqMoni_Claude\p56\spectra\AS80_Th232Medal_corpus.xml')
U235 = Comp('U-235', [(143.76, 10.96), (163.36, 5.08), (185.72, 57.0), (202.1, 1.08), (205.31, 5.02)], ref=185.72)
PA = Comp('Pa-234m 1001', [(1001.03, 0.842)]); PA766 = Comp('Pa-234m 766', [(766.42, 0.317)])
TH234 = Comp('Th-234 63', [(63.29, 3.67)]); TH234b = Comp('Th-234 92', [(92.38, 2.13), (92.8, 2.1)], ref=92.38)
BI1764 = Comp('Bi-214 1764', [(1764.5, 15.29)]); BI609 = Comp('Bi-214 609', [(609.31, 45.44)]); PB214 = Comp('Pb-214 352', [(295.22, 18.47), (351.93, 35.72)], ref=351.93)
r1 = fit_region(S, ab, eff, 130, 240, [U235], deg=2, kfree=True, label='U-235 186 связкой')
r2 = fit_region(S, ab, eff, 900, 1100, [PA], deg=2, kfree=True, label='Pa-234m 1001')
r3 = fit_region(S, ab, eff, 700, 830, [PA766], deg=2, kfree=False, label='Pa-234m 766')
r4 = fit_region(S, ab, eff, 50, 130, [TH234, TH234b, Comp('U K (94.7/98.4/111)', [(94.65, 100), (98.43, 160), (111.0, 55)], ref=98.43)], deg=2, kfree=False, label='Th-234 63/92 + U K')
r5 = fit_region(S, ab, eff, 1650, 1900, [BI1764], deg=2, kfree=False, label='Bi-214 1764')
r6 = fit_region(S, ab, eff, 540, 690, [BI609], deg=2, kfree=False, label='Bi-214 609')
r7 = fit_region(S, ab, eff, 260, 400, [PB214], deg=2, kfree=False, label='Pb-214 295/352')
N186 = r1['U-235'][0]; N1001 = r2['Pa-234m 1001'][0]
print('отношение N(186)/N(1001) = %.2f; ожидание для природного урана с кривой диска: %.2f' % (N186 / N1001, (0.046 * 0.57 * eff(185.72)) / (0.00842 * eff(1001.03))))
print('N1764/N1001 = %.3f (равновесный ряд дал бы %.2f) — радий из стекла удалён' % (r5['Bi-214 1764'][0] / N1001, (0.1529 * eff(1764.5)) / (0.00842 * eff(1001.03))))
print('в пересчёте на 52742 с диска: 1001 дало бы %.0f отсчётов (у диска 0 ± 4 300)' % (N1001 * 52742 / S.live))
