# -*- coding: utf-8 -*-
# (`AMBER272`, полоса `dbdata2`, 02.11.2026) Оснастка подгонки кривой света GAGG — журнал
# `handover/handover-2026-11-02-amber274-dbdata2.md` §4. Рабочий каталог (выводы проб, якобианы) — переменная
# окружения A272_WORK; сцены GAGG — A272_WORK/scenes; данные Kole — tools/nucdb/data/photon_light/.
# Пункт 3, проверка гипотезы причины: гашение Биркса ПО ТРЕКУ КАЖДОГО δ-электрона (как у Geant4 с отрезанием
# рождения Δ ≈ 1 кэВ: первичный теряет энергию непрерывно ограниченной тормозной L_Δ, δ-электроны выше Δ —
# отдельные треки со своим dE/dx) против модели базы (CSDA: гашение по средней тормозной S вдоль трека).
#
# Ожидаемый свет электрона G(T) (свет на кэВ — G(T)/T) — уравнение замедления с разрывными потерями:
#   L_Δ(T)·G'(T) = L_Δ(T)·q(L_Δ(T)) + ∫_Δ^{T/2} n_e·σ_M(T,ε)·[G(ε) + G(T−ε) − G(T)] dε ,   T > 2Δ,
#   G'(T) = q(S(T)),                                                                            T ≤ 2Δ,
# σ_M — сечение Мёллера (меньшая из двух энергий — δ), L_Δ = S − n_e∫ε σ_M dε, q(s) — местная форма Пейна базы.
# Решается неявной схемой вверх по T на густой сетке; линейная интерполяция G по T. Контроль схемы: при q ≡ 1
# обязано выйти G(T) = T (сохранение энергии), при Δ = T/2 — CSDA.
import os as _os
WORK = _os.environ.get('A272_WORK', '.').replace('\\', '/').rstrip('/') + '/'
HERE = _os.path.dirname(_os.path.abspath(__file__))
TREE = _os.path.dirname(_os.path.dirname(_os.path.dirname(HERE)))
import math
import sys

import numpy as np

sys.path.insert(0, HERE)
import payne  # noqa: E402

RE = 2.8179403262e-13      # см
MC2 = 510.99895            # кэВ
NA = 6.02214076e23
# GAGG Gd3Al2Ga3O12 (Ga:Al 3:2) — массовые доли как у сцены dbdata, атомные массы IUPAC
FRAC = {64: (0.508973, 157.25), 13: (0.058221, 26.9815385), 31: (0.225664, 69.723), 8: (0.207142, 15.999)}


def electron_density(rho):
    return rho * NA * sum(w * z / a for z, (w, a) in FRAC.items())


def moller_F(T, eps):
    tau = T / MC2
    g = tau + 1.0
    x = eps / T
    return 1.0 / x ** 2 + 1.0 / (1.0 - x) ** 2 + (tau / g) ** 2 - (2.0 * tau + 1.0) / g ** 2 / (x * (1.0 - x))


class DeltaModel(object):
    def __init__(self, dump, delta_kev=1.0, per_decade=1500, eps_points=400):
        self.d = dump
        self.st = payne.Stopping(dump, True)
        self.delta = delta_kev
        self.ne = electron_density(dump['density'])
        self.K = 2.0 * math.pi * RE ** 2 * MC2 * self.ne      # кэВ/см
        self.pd = per_decade
        self.ne_pts = eps_points

    def kernel(self, T):
        """Узлы ε на [Δ, T/2] (лог-сетка), веса квадратуры и n_e·dσ/dε (1/(см·кэВ))."""
        lo, hi = math.log(self.delta), math.log(T / 2.0)
        u = np.linspace(lo, hi, self.ne_pts)
        eps = np.exp(u)
        tau = T / MC2
        beta2 = 1.0 - 1.0 / (tau + 1.0) ** 2
        dens = self.K * moller_F(T, eps) / (beta2 * T * T)
        w = np.full(self.ne_pts, (hi - lo) / (self.ne_pts - 1)) * eps        # dε = ε du, трапеции
        w[0] *= 0.5
        w[-1] *= 0.5
        return eps, w, dens

    def solve(self, q, e_max=3000.0, unit_check=False):
        """Сетка T и G(T). q(s_МэВ/см) — местная форма (свет на кэВ потерь при dE/dx = s)."""
        e_lo = 0.01
        n = int(self.pd * math.log10(e_max / e_lo)) + 1
        T = e_lo * np.power(e_max / e_lo, np.arange(n) / (n - 1.0))
        G = np.zeros(n)
        qq = (lambda s: 1.0) if unit_check else q
        G[0] = qq(self.st(e_lo)) * e_lo
        two = 2.0 * self.delta
        for i in range(1, n):
            t, tp = T[i], T[i - 1]
            h = t - tp
            if t <= two:
                G[i] = G[i - 1] + 0.5 * h * (qq(self.st(tp)) + qq(self.st(t)))
                continue
            eps, w, dens = self.kernel(t)
            S = self.st(t) * 1000.0                     # кэВ/см
            W = float(np.sum(w * dens * eps))
            Ld = S - W
            Sig = float(np.sum(w * dens))
            # G(ε) — известно (ε ≤ T/2 < T); G(T−ε): при T−ε ≤ T_{i−1} — интерполяция по известным, иначе
            # линейно между G_{i−1} и неизвестным G_i: G(T−ε) = G_{i−1} + (G_i − G_{i−1})·(T−ε−T_{i−1})/h.
            Ge = np.interp(eps, T[:i], G[:i])
            te = t - eps
            known = te <= tp
            Gte_known = np.interp(te[known], T[:i], G[:i])
            a = np.zeros_like(eps)          # G(T−ε) = c + a·G_i
            c = np.zeros_like(eps)
            c[known] = Gte_known
            fr = (te[~known] - tp) / h
            a[~known] = fr
            c[~known] = G[i - 1] * (1.0 - fr)
            # неявно: L_Δ(G_i − G_{i−1})/h = L_Δ·q + Σ_ε w·dens·[G(ε) + c + a·G_i − G_i]
            B = float(np.sum(w * dens * (Ge + c)))
            A = float(np.sum(w * dens * (a - 1.0)))
            ql = qq(Ld / 1000.0)
            G[i] = (Ld * ql + Ld / h * G[i - 1] + B) / (Ld / h - A)
        return T, G


def delta_curve(dump, eta, ons, trap, birks, delta_kev=1.0, per_decade=1500):
    m = DeltaModel(dump, delta_kev, per_decade)

    def local(s):
        inner = (s / ons) * math.exp(-trap / s)
        return (1.0 - eta * math.exp(-inner)) / (1.0 + s / birks)
    T, G = m.solve(local)
    Tl, Gl = list(T), list(G)
    return payne.curve(dump, eta, ons, trap, birks, light_fn=lambda e: payne.loglog(Tl, Gl, e)), m


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    d = payne.load_dump()
    m = DeltaModel(d, 1.0, 800)
    T, G = m.solve(None, unit_check=True)
    err = np.max(np.abs(G / T - 1.0))
    print('контроль схемы q≡1: max |G/T − 1| = %.3e' % err)
    for t in (3.0, 10.0, 100.0, 661.657, 2000.0):
        eps, w, dens = m.kernel(t)
        S = m.st(t) * 1000.0
        W = float(np.sum(w * dens * eps))
        print('  T %7.1f кэВ: S %.4g МэВ/см, L_Δ/S %.3f, δ на см %.4g' % (t, S / 1000.0, (S - W) / S, float(np.sum(w * dens))))
