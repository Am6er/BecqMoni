# -*- coding: utf-8 -*-
"""П148, AMBER76: Монте-Карло цепочки импульсов — какая колонка наложений
((h⊗h)/N − m·h, m = 1 или 2) возвращает ИСТИННУЮ скорость при данном правиле
живого времени.

Модель тракта. Пуассоновский поток скорости R, энергия каждого события — из
игрушечного спектра s. Событие, пришедшее в живое время, ЗАПУСКАЕТ импульс;
тракт занят τ_d от запуска (непродлевающееся) или от последнего события
(продлевающееся). События в окне τ_p от запуска СКЛАДЫВАЮТСЯ с импульсом
(наложение, сумма энергий); прочие события в занятое время теряются.
τ_p ≤ τ_d — физически: наложение возможно, пока идёт импульс.

Живое время спектра — по правилу:
  clock        — часы живого времени, стоящие, пока тракт занят (Gedcke-Hale);
  app(τc)      — правило приложения LiveTime.Calculate: T − n·τc (непродл.);
  full         — живого времени нет, знаменатель — полное T.

Разбор: взвешенный МНК записанной гистограммы h по двум столбцам —
образ нуклида (истинное s, известное точно) и колонка наложений из самой h.
Оценка скорости R̂ = A / LT. Печатается R̂/R − 1 для m = 1 и m = 2.
"""
import sys
import numpy as np

BIN = 4.0
NB = 1000  # 0..4000 кэВ


def toy_pdf():
    e = (np.arange(NB) + 0.5) * BIN
    def g(mu, sig):
        p = np.exp(-0.5 * ((e - mu) / sig) ** 2)
        return p / p.sum()
    cont = np.where((e > 20) & (e < 600), np.exp(-e / 400.0), 0.0)
    cont /= cont.sum()
    p = 0.35 * g(662.0, 25.0) + 0.15 * g(1173.0, 35.0) + 0.5 * cont
    return p / p.sum()


def simulate(rng, R, T, tau_p, tau_d, paralyzable, pdf):
    n_ev = rng.poisson(R * T)
    t = np.sort(rng.uniform(0.0, T, n_ev))
    cdf = np.cumsum(pdf)
    ebin = np.searchsorted(cdf, rng.uniform(0.0, 1.0, n_ev))
    # энергия внутри бина равномерно
    en = (ebin + rng.uniform(0.0, 1.0, n_ev)) * BIN
    rec = []
    busy_until = -1.0
    busy_total = 0.0
    trig = -1.0
    cur = 0.0
    tl = t.tolist(); el = en.tolist()
    have = False
    for i in range(n_ev):
        ti = tl[i]
        if ti >= busy_until:
            if have:
                rec.append(cur)
            busy_total += busy_until - trig if have else 0.0
            trig = ti
            busy_until = ti + tau_d
            cur = el[i]
            have = True
        else:
            if ti - trig < tau_p:
                cur += el[i]
            if paralyzable:
                busy_until = ti + tau_d
    if have:
        rec.append(cur)
        busy_total += min(busy_until, T) - trig
    rec = np.array(rec)
    h = np.bincount(np.minimum((rec / BIN).astype(int), NB - 1), minlength=NB).astype(float)
    return h, len(rec), T - busy_total


def pile_column(h, m):
    N = h.sum()
    idx = np.nonzero(h)[0]
    col = np.zeros(NB)
    for a in idx:
        va = h[a] / N
        s = a + idx
        ok = s < NB
        half = 0.5 * va * h[idx[ok]]
        np.add.at(col, s[ok], half)
        s1 = s[ok] + 1
        ok1 = s1 < NB
        np.add.at(col, s1[ok1], half[ok1])
    return (col - m * h) / N


def fit(h, pdf, m):
    col = pile_column(h, m)
    w = 1.0 / np.maximum(h, 1.0)
    X = np.vstack([pdf, col]).T
    Xw = X * np.sqrt(w)[:, None]
    yw = h * np.sqrt(w)
    coef, *_ = np.linalg.lstsq(Xw, yw, rcond=None)
    return coef  # A (событий на время спектра), a (пар)


def main():
    rng = np.random.default_rng(int(sys.argv[1]) if len(sys.argv) > 1 else 1)
    pdf = toy_pdf()
    R = 10000.0
    T = 300.0
    cases = [
        # (подпись, tau_p, tau_d, paralyzable, правило LT)
        ("контроль: без наложений, мёртвое 10 мкс, clock", 0.0, 10e-6, False, "clock"),
        ("непродл., tau_p=2 < tau_d=10, clock", 2e-6, 10e-6, False, "clock"),
        ("непродл., tau_p=2 < tau_d=10, app(10)", 2e-6, 10e-6, False, "app:10e-6"),
        ("непродл., tau_p=2 < tau_d=10, full", 2e-6, 10e-6, False, "full"),
        ("непродл., tau_p=2 = tau_d=2, clock", 2e-6, 2e-6, False, "clock"),
        ("непродл., tau_p=2 = tau_d=2, full", 2e-6, 2e-6, False, "full"),
        ("продл., tau_p=2 < tau_d=10, clock", 2e-6, 10e-6, True, "clock"),
        ("продл., tau_p=2 < tau_d=10, app(10)", 2e-6, 10e-6, True, "app:10e-6"),
        ("непродл., tau_p=2, tau_d=10, app(1): LT короче окна наложений", 2e-6, 10e-6, False, "app:1e-6"),
        ("непродл., tau_p=2 = tau_d=2, app(1)", 2e-6, 2e-6, False, "app:1e-6"),
    ]
    print("R = %.0f 1/с, T = %.0f с; колонка m=1: (h⊗h)/N − h; m=2: (h⊗h)/N − 2h" % (R, T))
    print("%-62s %9s %9s %9s %9s %9s" % ("случай", "R·tau_p", "dead", "m=1", "m=2", "m*"))
    for label, tp, td, par, rule in cases:
        h, n, lt_clock = simulate(rng, R, T, tp, td, par, pdf)
        if rule == "clock":
            lt = lt_clock
        elif rule == "full":
            lt = T
        else:
            tc = float(rule.split(":")[1])
            lt = T - n * tc
        res = []
        for m in (1, 2):
            A, a = fit(h, pdf, m)
            res.append(A / lt / R - 1.0)
        # m, при котором смещение ноль (линейно между двумя)
        mstar = 1.0 + res[0] / (res[0] - res[1]) if res[0] != res[1] else float("nan")
        print("%-62s %9.4f %9.4f %+9.4f %+9.4f %9.2f" % (label, R * tp, 1 - lt_clock / T, res[0], res[1], mstar))


if __name__ == "__main__":
    main()
