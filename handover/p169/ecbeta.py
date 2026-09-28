# -*- coding: utf-8 -*-
"""Теоретическая доля β⁺ разрешённого перехода (ε/β⁺) — прототип AMBER121-остатка, П169.

Единицы: энергия в mₑc², длина в ħ/(mₑc). Разрешённый переход, один и тот же ядерный
матричный элемент у β⁺ и захвата, поэтому он сокращается:

  λ_β+ ∝ f_β+ /(2π³),   f_β+ = ∫₁^{W0} F(−Z_d, W) p W (W0 − W)² dW,  W0 = E0 − 1
  λ_x  ∝ ρ_x(R) q_x² / π,  q_x = E0 − B_x  (энергия нейтрино захвата с оболочки x;
        два электрона оболочки ns, но захват идёт только с одной проекции спина)

  ε/β⁺ = 2π² Σ_x ρ_x(R) q_x² / f_β+   (сверено: ²²Na→1274 0.108, ¹⁸F 0.0326, ⁶⁴Cu 2.56)

F — функция Ферми с конечным радиусом (Бете–Бахер/Бехар):
  F = 2(1+γ)(2pR)^{2γ−2} e^{πη} |Γ(γ+iη)|² / Γ(2γ+1)²,  γ = √(1−(αZ)²),  η = αZ W/p (Z<0 для β⁺).
ρ_x(R) — плотность электрона ns (x = K, L1, M1) у ядра по водородоподобному
решению Дирака с экранированным зарядом:
  ρ_ns(r) ≈ (Zα)³/(π n³) · (1+γ)/Γ(2γ+1) · (2Zαr/n)^{2γ−2}   (для n = 1 — точно при e^{−2Zαr} → 1).
E0 = Q + E(родителя) − E(уровня), кэВ, B_x — энергии связи оболочек (EADL, matdb).
"""
import cmath
import math

ALPHA = 1.0 / 137.035999
MEC2 = 510.99895          # кэВ
HBARC_OVER_MEC_FM = 386.15926796  # ħ/(mₑc), фм

_LANCZOS = [0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7]


def lgamma_c(z):
    """ln Γ(z) для комплексного z (Ланцош, g = 7)."""
    if z.real < 0.5:
        return cmath.log(math.pi / cmath.sin(math.pi * z)) - lgamma_c(1 - z)
    z -= 1
    x = _LANCZOS[0]
    for i in range(1, 9):
        x += _LANCZOS[i] / (z + i)
    t = z + 7.5
    return 0.5 * math.log(2 * math.pi) + (z + 0.5) * cmath.log(t) - t + cmath.log(x)


def fermi(z_signed, w, radius):
    p = math.sqrt(max(w * w - 1.0, 1e-300))
    az = ALPHA * z_signed
    g = math.sqrt(1.0 - az * az)
    eta = az * w / p
    lg = lgamma_c(complex(g, eta)).real
    return (2.0 * (1.0 + g) * (2.0 * p * radius) ** (2.0 * g - 2.0)
            * math.exp(math.pi * eta + 2.0 * lg - 2.0 * math.lgamma(2.0 * g + 1.0)))


def f_beta_plus(z_daughter, w0, radius, n=400):
    if w0 <= 1.0:
        return 0.0
    # подстановка W = 1 + (W0−1)·t² гасит корневую особенность p у порога
    s = 0.0
    h = 1.0 / n
    for i in range(n + 1):
        t = i * h
        w = 1.0 + (w0 - 1.0) * t * t
        p = math.sqrt(max(w * w - 1.0, 0.0))
        val = 0.0 if p == 0.0 else fermi(-z_daughter, w, radius) * p * w * (w0 - w) ** 2 * 2.0 * (w0 - 1.0) * t
        coef = 1 if i in (0, n) else (4 if i % 2 else 2)
        s += coef * val
    return s * h / 3.0


# Экранирование по Слейтеру для ns: K — 0.30, L1 — 4.15, M1 — 11.25
SHELLS = ((1, 1, 0.30), (2, 3, 4.15), (3, 8, 11.25))   # (n, EADL shell_id, σ)


def rho_ns(z_eff, n, radius):
    az = ALPHA * z_eff
    g = math.sqrt(1.0 - az * az)
    return (az ** 3 / (math.pi * n ** 3) * (1.0 + g) / math.gamma(2.0 * g + 1.0)
            * (2.0 * az * radius / n) ** (2.0 * g - 2.0))


def ec_over_beta(z_daughter, mass, e0_kev, binding_kev):
    """ε/β⁺ разрешённого перехода; binding_kev — {shell_id: B, кэВ} атома дочери.
    None — если β⁺ запрещён (E0 ≤ 2mₑc²)."""
    e0 = e0_kev / MEC2
    w0 = e0 - 1.0
    if w0 <= 1.0:
        return None
    radius = 1.2 * mass ** (1.0 / 3.0) / HBARC_OVER_MEC_FM
    fb = f_beta_plus(z_daughter, w0, radius)
    z_parent = z_daughter + 1
    ec = 0.0
    for n, sid, sigma in SHELLS:
        b = binding_kev.get(sid)
        if b is None:
            continue
        q = e0 - b / MEC2
        if q <= 0:
            continue
        ec += rho_ns(z_parent - sigma, n, radius) * q * q
    return 2.0 * math.pi ** 2 * ec / fb


def beta_plus_share(z_daughter, mass, e0_kev, binding_kev):
    r = ec_over_beta(z_daughter, mass, e0_kev, binding_kev)
    if r is None:
        return 0.0
    return 1.0 / (1.0 + r)
