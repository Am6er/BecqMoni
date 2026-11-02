# -*- coding: utf-8 -*-
# (`AMBER272`, полоса `dbdata2`, 02.11.2026) Оснастка подгонки кривой света GAGG — журнал
# `handover/handover-2026-11-02-amber274-dbdata2.md` §4. Рабочий каталог (выводы проб, якобианы) — переменная
# окружения A272_WORK; сцены GAGG — A272_WORK/scenes; данные Kole — tools/nucdb/data/photon_light/.
# Независимая сверка решения уравнения замедления (delta_birks.py) розыгрышем: первичный электрон теряет энергию
# непрерывно ограниченной тормозной L_Δ (шаг ≤ 1 % энергии), δ-электроны выше Δ рождаются по Мёллеру с частотой
# Σ(T) и каждый — СВОЙ трек, разыгрываемый тем же способом (полная рекурсия, без подстановки G). Ниже 2Δ — CSDA.
import os as _os
WORK = _os.environ.get('A272_WORK', '.').replace('\\', '/').rstrip('/') + '/'
HERE = _os.path.dirname(_os.path.abspath(__file__))
TREE = _os.path.dirname(_os.path.dirname(_os.path.dirname(HERE)))
import math
import random
import sys

import numpy as np

sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, HERE)
import payne  # noqa: E402
import delta_birks as dbk  # noqa: E402

d = payne.load_dump()
m = dbk.DeltaModel(d, 1.0, 800)
BIRKS = 133.33333333333334
q = lambda s: 1.0 / (1.0 + s / BIRKS)
# таблицы по T
TT = np.exp(np.linspace(math.log(2.0001), math.log(3000.0), 600))
LD, SIG = [], []
for t in TT:
    eps, w, dens = m.kernel(t)
    S = m.st(t) * 1000.0
    LD.append(S - float(np.sum(w * dens * eps)))
    SIG.append(float(np.sum(w * dens)))
LD, SIG = np.array(LD), np.array(SIG)
Tc, Gc = m.solve(lambda s: q(s), e_max=2.0001)      # CSDA ниже 2Δ (на этом отрезке δ нет)
Tc, Gc = list(Tc), list(Gc)
lt = np.log(TT)


def tab(arr, t):
    return float(np.interp(math.log(t), lt, arr))


def sample_eps(T, rnd):
    lo, hi = 1.0, T / 2.0
    tau = T / dbk.MC2
    g = tau + 1.0
    while True:
        u = rnd.random()
        eps = 1.0 / (1.0 / lo - u * (1.0 / lo - 1.0 / hi))
        x = eps / T
        f = 1.0 + x * x / (1.0 - x) ** 2 + (tau / g) ** 2 * x * x - (2.0 * tau + 1.0) / g ** 2 * x / (1.0 - x)
        if rnd.random() * 2.3 < f:
            return eps


def track(T0, rnd):
    light = 0.0
    stack = [T0]
    while stack:
        T = stack.pop()
        while T > 2.0:
            L = tab(LD, T)
            sig = tab(SIG, T)
            smax = 0.01 * T / L
            s = -math.log(1.0 - rnd.random()) / sig
            if s < smax:
                dE = L * s
                light += dE * q(L / 1000.0)
                T -= dE
                if T > 2.0:
                    e = sample_eps(T, rnd)
                    T -= e
                    stack.append(e)
            else:
                dE = L * smax
                light += dE * q(L / 1000.0)
                T -= dE
        light += payne.loglog(Tc, Gc, T) if T > 0.01 else 0.0
    return light


if __name__ == '__main__':
    rnd = random.Random(12345)
    T, G = m.solve(lambda s: q(s))
    for T0, n in ((20.0, 3000), (100.0, 1500), (661.657, 400)):
        vals = [track(T0, rnd) / T0 for _ in range(n)]
        mean = sum(vals) / n
        sd = math.sqrt(sum((v - mean) ** 2 for v in vals) / (n - 1) / n)
        ode = float(np.interp(T0, T, G)) / T0
        print('T %.1f кэВ: розыгрыш L/E %.5f ± %.5f (n %d), уравнение %.5f, разность %+.2f σ' % (
            T0, mean, sd, n, ode, (mean - ode) / sd))
