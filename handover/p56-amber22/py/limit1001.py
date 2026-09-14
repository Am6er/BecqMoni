# -*- coding: utf-8 -*-
"""П56: предел Pa-234m 1001 в диске (2025-08, 52 742 с) при разных допущениях подгонки + положительный контроль впрыском 20 000."""
import sys, math, numpy as np
sys.path.insert(0, r'D:\BqMoni_Claude\p56\py')
from spec import Spec
from eff import Eff
import recal
from ufit import Comp, fit_region
S = Spec(r'D:\BqMoni_Claude\p56\spectra\Th-232_medal_2025-08.xml', r'D:\BqMoni_Claude\p56\spectra\Fon_dom.xml'); eff = Eff(r'D:\BqMoni_Claude\p56\spectra\AS80_Th232Medal_corpus.xml')
coef, ab, pts, fws = recal.recal(S, verbose=False)
PA = Comp('Pa-234m 1001', [(1001.03, 0.842)]); TL860 = Comp('Tl-208 860', [(860.56, 12.5)]); BI1079 = Comp('Bi-212 1079', [(1078.6, 0.56)])
AC911 = Comp('Ac-228 911', [(911.2, 25.8), (904.2, 0.77)], ref=911.2); AC969 = Comp('Ac-228 965/969', [(964.77, 4.99), (968.97, 15.8)], ref=968.97)
AC_hi = Comp('Ac-228 911/965/969', [(911.2, 25.8), (964.77, 4.99), (968.97, 15.8), (904.2, 0.77)], ref=911.2)
fit_region(S, ab, eff, 840, 1060, [TL860, AC911, AC969, PA, BI1079], deg=2, kfree=False, label='1001: 969 отвязана от 911, ширина зажата')
fit_region(S, ab, eff, 840, 1060, [TL860, AC911, AC969, PA, BI1079], deg=2, kfree=True, label='1001: 969 отвязана, ширина свободна')
fit_region(S, ab, eff, 840, 1060, [TL860, AC_hi, PA, BI1079], deg=2, kfree=True, label='1001: связка, ширина свободна')
fit_region(S, ab, eff, 840, 1120, [TL860, AC_hi, PA, BI1079, Comp('Bi-214 1120', [(1120.3, 14.9)])], deg=3, kfree=True, label='1001: связка, ширина свободна, подложка 3, 840-1120')
S2F = 2 * math.sqrt(2 * math.log(2)); inj = 20000.0; s = math.sqrt(ab[0] + ab[1] * 1001) / S2F
S.counts = S.counts + inj * np.exp(-0.5 * ((S.keV - 1001.03) / s) ** 2) / (s * math.sqrt(2 * math.pi)) * np.diff(S.keVb)
print('контроль: впрыснуто 20000 в 1001')
fit_region(S, ab, eff, 840, 1060, [TL860, AC911, AC969, PA, BI1079], deg=2, kfree=False, label='1001 free969 +20k')
fit_region(S, ab, eff, 840, 1060, [TL860, AC_hi, PA, BI1079], deg=2, kfree=True, label='1001 kfree +20k')
