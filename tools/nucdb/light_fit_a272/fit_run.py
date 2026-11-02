# -*- coding: utf-8 -*-
# (`AMBER272`, полоса `dbdata2`, 02.11.2026) Оснастка подгонки кривой света GAGG — журнал
# `handover/handover-2026-11-02-amber274-dbdata2.md` §4. Рабочий каталог (выводы проб, якобианы) — переменная
# окружения A272_WORK; сцены GAGG — A272_WORK/scenes; данные Kole — tools/nucdb/data/photon_light/.
# Пункт 3: проверка гипотезы причины и подгонка (η, S_Birks; S_Trap/S_Ons — если понадобятся).
import os as _os
WORK = _os.environ.get('A272_WORK', '.').replace('\\', '/').rstrip('/') + '/'
HERE = _os.path.dirname(_os.path.abspath(__file__))
TREE = _os.path.dirname(_os.path.dirname(_os.path.dirname(HERE)))
import math
import sys

import numpy as np
from scipy.optimize import minimize

sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, HERE)
import payne  # noqa: E402
import fit_gagg as fg  # noqa: E402
import delta_birks as dbk  # noqa: E402

m = fg.Model()
d = m.d
cv = payne.curve(d, *fg.DB)
print('узлы якобиана = узлы кривой:', m.npr.check_nodes(cv), m.kole.check_nodes(cv))
print('Σ a_k·y_k против S0 пробы:', '%.2e' % m.npr.S0_check(cv), '%.2e' % m.kole.S0_check(cv))
print()
print('== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: параметры базы (Kole, kB 0.075 мм/МэВ, модель базы CSDA)')
res0 = m.evaluate(cv)
print('   ', fg.fmt(res0))
print()
print('== ГИПОТЕЗА: гашение по треку каждого δ-электрона (отрезание рождения Δ) при том же kB')
for delta in (0.5, 1.0, 2.0):
    dl, _ = dbk.delta_curve(d, 0.0, 36.4, 0.0, fg.DB[3], delta, 1000)
    print('   Δ %.1f кэВ, kB 0.075: %s' % (delta, fg.fmt(m.evaluate(dl))))
print('   δ-модель, kB — по одной форме Kole (наименьшее СКО):')
best = None
for B in np.arange(70.0, 260.0, 5.0):
    dl, _ = dbk.delta_curve(d, 0.0, 36.4, 0.0, B, 1.0, 600)
    r = m.evaluate(dl)
    if best is None or r['sko'] < best[1]['sko']:
        best = (B, r)
print('   S_Birks %.0f МэВ/см (kB %.3f мм/МэВ): %s' % (best[0], 10.0 / best[0], fg.fmt(best[1])))
print()
print('== МОДЕЛЬ БАЗЫ (CSDA): какой σ_nPR даёт одна форма Kole при η = 0 (одномерно по S_Birks)')
for B in (100.0, 133.333, 180.0, 250.0, 400.0, 700.0):
    print('   S_Birks %6.1f: %s' % (B, fg.fmt(m.params(0.0, 0.0, B))))
print()


def solve(free, sig_target=None, sig_lo=2.4, sig_hi=3.0, x0=None, ons=fg.ONS):
    """free: 'eb' (η, S_Birks; Trap 0) | 'etb' (η, S_Trap, S_Birks) | 'eob' (η, S_Ons, S_Birks; Trap 0).
    Цель: наименьшее СКО формы Kole при σ_nPR в [sig_lo, sig_hi] (штраф) либо σ_nPR = sig_target."""
    def unpack(x):
        if free == 'eb':
            return x[0], 0.0, math.exp(x[1]), ons
        if free == 'etb':
            return x[0], max(0.0, x[1]), math.exp(x[2]), ons
        return x[0], 0.0, math.exp(x[2]), math.exp(x[1])

    def f(x):
        eta, trap, birks, o = unpack(x)
        if not (0.0 <= eta < 0.95):
            return 1e6
        r = m.params(eta, trap, birks, o)
        pen = 0.0
        if sig_target is not None:
            pen = 50.0 * (r['sigma'] - sig_target) ** 2
        else:
            pen = 50.0 * (max(0.0, r['sigma'] - sig_hi) ** 2 + max(0.0, sig_lo - r['sigma']) ** 2)
        return r['sko'] ** 2 + pen
    res = minimize(f, x0, method='Nelder-Mead', options={'xatol': 1e-4, 'fatol': 1e-6, 'maxiter': 2000})
    eta, trap, birks, o = unpack(res.x)
    return (eta, trap, birks, o), m.params(eta, trap, birks, o)


print('== ПОДГОНКА в модели базы (S_Ons 36.4, S_Trap 0): η и S_Birks')
for st in (None, 2.4, 2.7, 3.0):
    best = None
    for x0 in ([0.2, math.log(150.0)], [0.35, math.log(120.0)], [0.5, math.log(90.0)], [0.1, math.log(250.0)]):
        p, r = solve('eb', st, x0=x0)
        obj = r['sko'] + (abs(r['sigma'] - st) * 10 if st else 0)
        if best is None or obj < best[0]:
            best = (obj, p, r)
    obj, p, r = best
    print('   %s: η %.4f, S_Birks %.2f (kB %.4f мм/МэВ) → %s' % (
        'σ_nPR в 2.4…3.0' if st is None else 'σ_nPR = %.1f' % st, p[0], p[2], 10.0 / p[2], fg.fmt(r)))
print()
print('== ПОДГОНКА с S_Trap: η, S_Trap, S_Birks')
for st in (None, 2.7):
    best = None
    for x0 in ([0.3, 10.0, math.log(130.0)], [0.4, 20.0, math.log(110.0)], [0.2, 5.0, math.log(200.0)]):
        p, r = solve('etb', st, x0=x0)
        obj = r['sko'] + (abs(r['sigma'] - st) * 10 if st else 0)
        if best is None or obj < best[0]:
            best = (obj, p, r)
    obj, p, r = best
    print('   %s: η %.4f, S_Trap %.3f, S_Birks %.2f → %s' % (
        'σ_nPR в 2.4…3.0' if st is None else 'σ_nPR = %.1f' % st, p[0], p[1], p[2], fg.fmt(r)))
