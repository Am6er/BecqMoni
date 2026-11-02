# -*- coding: utf-8 -*-
u"""Таблица экранирования упругого рассеяния электрона и позитрона по DPWA (`AMBER274`, физика 29,
находка «Ф1» критика физики 29, полоса `screen29`, 02.11.2026).

ЗАЧЕМ. Смешанная схема переноса (`ElectronTransport.LayerElasticStep` / `LayerHardCollision`,
GPU `sim_electron.cuh`) берёт ядерную часть упругого сечения в виде

    dσ/dμ = C(T) · Z² · P(√μ) / (μ + a)²,     μ = 1 − cos θ ∈ [0, 2],
    C(T)  = 2π r_e² (m c²)² / (β² (pc)²),

где a = 2A — экранирование, P — поправка к экранированному Резерфорду. До этой таблицы a —
формула Мольера 2χ₀²(1.13 + 3.76 (αZ/β)²) без ограничения (у Cs при 10 кэВ множитель 17,
при 3 кэВ — 54), P — подгонка Лицзяня/Бошини отношения Мотта к Резерфорду для точечного ядра
(`MottData.cs`, Z > 30) или Мак-Кинли—Фешбах (Z ≤ 30). Против DPWA (ELSEPA) у Cs: σ_tr1 0.56
при 10 кэВ и 0.89 при 46 кэВ, мягкая часть (< 20°) — 0.07 и 0.56.

Решение Amber 02.11.2026 вопросником, дословно: «Сейчас, до склада (Рекомендую)» — параметр
экранирования, подогнанный к данным DPWA из поставки Geant4 (как в GS, Salvat 2005).

ЧТО В ТАБЛИЦЕ (на частицу e⁻/e⁺, Z = 1…92, узел T — сетка DPWA от 1 кэВ до 10 МэВ, 15 узлов на
декаду, 61 узел; энергию НЕ интерполируем при подгонке — узлы те, где ELSEPA считала):

  * `a_tr1` (только e⁻) — постановка «GS» в чистом виде: одно экранирование a, при котором
    НЫНЕШНИЙ вид P (Лицзянь при Z > 30, Мак-Кинли—Фешбах при Z ≤ 30) даёт полное первое
    транспортное сечение σ_tr1 DPWA. Так экранирование правит GS Geant4 (`G4GoudsmitSaundersonMscModel`,
    режим Мотта: «screening parameter … reproduce the first transport cross sections … from
    elsepa»). Форма выше отсечки при этом НЕ лечится: у Cs при 46 кэВ σ(>20°) 1.17, dσ/dΩ(150°)
    0.85 от DPWA (журнал полосы, §2).
  * `a`, `c0`…`c4` — постановка «экранирование + форма» (умолчание полосы): a и многочлен
    P(x) = c0 + c1 x + c2 x² + c3 x³ + c4 x⁴, x = √μ, подогнаны к DPWA вместе:
      - точно (равенства): σ(μ > μ₁₀), ∫_{μ₁₀}^{2} μ dσ и ∫_0^{μ₁₀} μ dσ при μ₁₀ = 1 − cos 10° —
        отсюда точное σ_tr1 и верные σ(>θ_c) и мягкая часть при любой отсечке θ_c ≥ 10°
        (умолчание переноса — 20°, проверки сходимости — 10° и 30°);
      - наименьшие квадраты относительной ошибки dσ/dΩ на θ = 10°, 12°, …, 180°;
      - P ≥ 0 на всём [0, √2] (иначе отбор угла и замкнутые формы разошлись бы);
      - a гладкая по T: второй проход берёт a, сглаженное по ln T, и пересчитывает только c
        (без этого a «гуляет» — форма вырождена с c — и линейная интерполяция по ln T между
        узлами портила бы сечения).
    Вид сечения — ТОТ ЖЕ, что у ветки `MottFit` (`MottMoments`, `MottMajorant`, отбор до
    принятия): меняется только источник a и c (таблица вместо формулы Мольера и `MottTerms`).
    ⚠ a здесь — параметр подгонки, а НЕ физическое экранирование атома: у тяжёлых Z ниже ~100 кэВ
    оно в 10…100 раз меньше мольеровского, а форму у малых углов несёт P (c0 < 1). Поэтому
    электронная часть множителя Z(Z+1) (e–e, «+Z») этой таблицы НЕ берёт: у неё своё
    экранирование (как было) — иначе её мягкая часть выросла бы вдвое (журнал, §2).
  * `fit_rms` — ско относительной ошибки dσ/dΩ подгонки на 10°…180°; `dpwa_tr1_cm2`,
    `dpwa_h20_cm2`, `dpwa_s20_cm2` — σ_tr1, σ(>20°) и ∫_0^{μ₂₀} μ dσ по DPWA, см² на атом
    (опора проверки: импорт и сторож пересчитывают модель и сверяют с ними).

Числа пишутся `repr` (кратчайшая запись, восстанавливающая тот же double; разделитель — точка).

ИСТОЧНИК ДАННЫХ И УСЛОВИЯ. Сечения — `G4EMLOW8.8/dpwa/dcss/{el,pos}` поставки Geant4 11.4
(M. Novak, CERN, 2020; ELSEPA: F. Salvat, A. Jablonski, C.J. Powell, Comput. Phys. Commun. 165
(2005) 157–190; потенциал — Salvat, Phys. Rev. A 68 (2003) 012708: Дирак—Фок, ядро Ферми, обмен
Фёрнесса—Маккарти для e⁻, корреляция-поляризация LDA, без поглощения). Сетка — `dpwa/grid.dat`.
⛔ Шапка `G4EMLOW8.8/README` говорит: «DATA ARE *NOT* FOR COMMERCIAL USE AND MUST BE USED
WITHIN GEANT4». Лицензия Geant4 (http://cern.ch/geant4/license) требует при распространении
сохранять уведомление и ссылку «This product includes software developed by Members of the Geant4
Collaboration». Решение Amber 02.11.2026, дословно: «BecqMoni - не коммерческий продукт. Просто возьми
готовые данные.» — таблица строится по G4EMLOW (свой расчёт ELSEPA полосы `screen29` — только сверка,
журнал `handover/handover-2026-11-02-amber274-screen29.md`, §11).
Коэффициенты Лицзяня (`a_tr1`) — `BecquerelMonitor/EfficiencyMaker/MottData.cs` (из `G4MottData.hh`).

Запуск (только чтение исходных данных; пишет `data/dpwa_screening/*.tsv` рядом со скриптом):

    python -I build_dpwa_screening.py [--g4emlow <каталог G4EMLOW>] [--out <каталог>]
    python -I build_dpwa_screening.py --check [--check-z 6,8,13,14,26,29,32,53,55,82,83]

`--check` — таблица «до/после» против DPWA (нынешний вид, `a_tr1`, `a`+`c`) на узлах и в
серединах между узлами (линейная интерполяция ln a и c по ln T, как в переносе); положительный
контроль — нынешний вид обязан разойтись с DPWA как у критика (Cs: σ_tr1 0.56 при 10 кэВ).
"""
from __future__ import print_function
import argparse
import io
import math
import os
import re
import sys
import zlib

import numpy as np
from scipy.interpolate import CubicSpline
from scipy.optimize import brentq, minimize_scalar

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
MOTT_CS = os.path.join(ROOT, 'BecquerelMonitor', 'EfficiencyMaker', 'MottData.cs')
# Поставка Geant4 рядом с деревом (`<repos>/GEANT4/G4EMLOW8.8`), иначе — `G4LEDATA` или `--g4emlow`.
DEFAULT_G4EMLOW = os.path.join(ROOT, '..', 'GEANT4', 'G4EMLOW8.8')

# Константы — как в `EfficiencySimulator` / `ScatteringData` (CODATA 2018).
ME = 510.99895                  # кэВ
RE = 2.8179403262e-13           # см
ALPHA = 7.2973525693e-3
ROOT2 = 1.4142135623730951

ZMAX = 92
IE_LO, IE_HI = 30, 90           # узлы DPWA: 1 кэВ … 10 МэВ
DEG_LO = 10.0                   # нижний край формы и точка «мягкой» привязки
FIT_THETA = np.arange(DEG_LO, 180.0001, 2.0)
DEG_CUT = 20.0                  # отсечка переноса по умолчанию (`LayerHardCutoffDeg`) — для опорных чисел
MOTT_MAX_Z = 30                 # `ElectronTransport.MottMaxZ`
MOTT_SHIFT = 0.7181228          # `MottData.BetaShift`

XGL, WGL = np.polynomial.legendre.leggauss(8)
XGL = 0.5 * (XGL + 1.0)
WGL = 0.5 * WGL


def mu_of(deg):
    return 1.0 - np.cos(np.radians(deg))


# ---------------------------------------------------------------- DPWA ----------------------------
class Dpwa(object):
    """Сечения DPWA одного Z и частицы на узлах сетки; dσ/dμ = 2π dσ/dΩ (μ = 1 − cos θ)."""

    def __init__(self, root):
        self.root = root
        g = io.open(os.path.join(root, 'dpwa', 'grid.dat'), encoding='ascii').read().split()
        ne, nl, nh = int(g[0]), int(g[1]), int(g[2])
        v = [float(x) for x in g[3:3 + ne + nl + nh]]
        self.energy = np.array(v[:ne]) * 1000.0       # МэВ → кэВ
        self.theta_low = np.array(v[ne:ne + nl])
        self.theta_high = np.array(v[ne + nl:])
        self.ilim = int(np.sum(self.energy < 2.0))     # как `G4eDPWAElasticDCS`: e⁻ ниже 2 кэВ — густая сетка
        self.cache = {}

    def _read(self, name, ntheta):
        with open(os.path.join(self.root, 'dpwa', 'dcss', name), 'rb') as f:
            t = zlib.decompress(f.read()).decode('ascii').split()
        a = np.array([float(x) for x in t]).reshape(ntheta, -1)
        return a[:, 1:]

    def table(self, z, kind):
        key = (z, kind)
        if key not in self.cache:
            if kind == 'el':
                self.cache[key] = (self._read('el/dcs_%d_l.z' % z, len(self.theta_low)),
                                   self._read('el/dcs_%d_h.z' % z, len(self.theta_high)))
            else:
                self.cache[key] = (None, self._read('pos/dcs_%d.z' % z, len(self.theta_high)))
        return self.cache[key]

    def column(self, z, kind, ie):
        lo, hi = self.table(z, kind)
        if kind == 'el' and ie < self.ilim:
            return self.theta_low, lo[:, ie]
        if kind == 'el':
            return self.theta_high, hi[:, ie - self.ilim]
        return self.theta_high, hi[:, ie]

    def node(self, z, kind, ie):
        th, d = self.column(z, kind, ie)
        return Dcs(self.energy[ie], th, safe_log(th, d))

    def between(self, z, kind, t):
        """Сечение между узлами: кубика Лагранжа по ln E от ln dσ/dΩ (как бикубика Geant4)."""
        le = np.log(self.energy)
        k = int(np.searchsorted(self.energy, t)) - 1
        ks = [k - 1, k, k + 1, k + 2]
        th = self.column(z, kind, ks[-1])[0]
        cols = []
        for i in ks:
            thi, d = self.column(z, kind, i)
            ld = safe_log(thi, d)
            if len(thi) != len(th):
                ld = CubicSpline(mu_of(thi), ld)(mu_of(th))
            cols.append(ld)
        x = math.log(t)
        ld = np.zeros_like(cols[0])
        for i in range(4):
            w = 1.0
            for j in range(4):
                if j != i:
                    w *= (x - le[ks[j]]) / (le[ks[i]] - le[ks[j]])
            ld += w * cols[i]
        return Dcs(t, th, ld)


SAFE_LOG_FIXES = []


def safe_log(theta, d):
    """ln dσ/dΩ; у лёгких Z ELSEPA местами даёт ≤ 0 у самого минимума (Li 180° при 4.6 кэВ: −6e-38) —
    такие точки заменяются интерполяцией ln по μ от соседей (учёт — `SAFE_LOG_FIXES`)."""
    d = np.asarray(d, dtype=float)
    good = d > 0.0
    if good.all():
        return np.log(d)
    mu = mu_of(theta)
    out = np.empty_like(d)
    out[good] = np.log(d[good])
    out[~good] = np.interp(mu[~good], mu[good], out[good])
    SAFE_LOG_FIXES.append(int(np.sum(~good)))
    return out


class Dcs(object):
    """dσ/dμ по ln dσ/dΩ на сетке θ: кубический сплайн по μ, Гаусс—Лежандр 8 на промежутке."""

    def __init__(self, t, theta, log_dcs):
        self.t = t
        self.mu = mu_of(theta)
        self.mu[-1] = 2.0
        self.spline = CubicSpline(self.mu, log_dcs)

    def dsdmu(self, mu):
        return 2.0 * math.pi * np.exp(self.spline(mu))

    def integ(self, power, lo=0.0, hi=2.0):
        pts = np.concatenate(([lo], self.mu[(self.mu > lo) & (self.mu < hi)], [hi]))
        a = pts[:-1]
        dl = pts[1:] - a
        x = (a[:, None] + dl[:, None] * XGL[None, :]).ravel()
        w = (dl[:, None] * WGL[None, :]).ravel()
        return float(np.sum(w * self.dsdmu(x) * x ** power))


# ---------------------------------------------------------------- модель --------------------------
def kinematics(t):
    gamma = 1.0 + t / ME
    beta2 = 1.0 - 1.0 / (gamma * gamma)
    return gamma, beta2, math.sqrt(beta2), t * (t + 2.0 * ME)


def common(t):
    """C(T) = 2π r_e² m² / (β² p²), см² (`LayerElasticStep`, `common`)."""
    gamma, beta2, beta, p2 = kinematics(t)
    return 2.0 * math.pi * RE * RE * ME * ME / (beta2 * p2)


def moliere_two_a(z, t):
    """`ElectronTransport.ScreeningTwoA`: 2χ₀²(1.13 + 3.76 (αZ/β)²), χ₀ = α Z^(1/3)/(1.77 βγ)."""
    gamma, beta2, beta, p2 = kinematics(t)
    chi = ALPHA * z ** (1.0 / 3.0) / (1.77 * beta * gamma)
    az = ALPHA * z
    return 2.0 * chi * chi * (1.13 + 3.76 * az * az / beta2)


def moments(a, muc):
    """Базисы `ElectronTransport.MottMoments`: h_j = ∫_{μc}^{2} μ^{j/2}/(μ+a)² dμ,
    s_j = ∫_0^{μc} μ·μ^{j/2}/(μ+a)² dμ, j = 0…4 — те же формулы, тем же порядком."""
    xc = math.sqrt(muc)
    s = math.sqrt(a)
    in_xa = 1.0 / (muc + a)
    in_2a = 1.0 / (2.0 + a)
    h0 = in_xa - in_2a
    lh = math.log((2.0 + a) * in_xa)
    h2 = lh - a * h0
    h4 = (2.0 - muc) - 2.0 * a * lh + a * a * h0
    d_atan = math.atan((ROOT2 - xc) * s / (a + ROOT2 * xc))
    h1 = d_atan / s - ROOT2 * in_2a + xc * in_xa
    h3 = 2.0 * (ROOT2 - xc) - 3.0 * s * d_atan + a * (ROOT2 * in_2a - xc * in_xa)
    ls = math.log((muc + a) / a)
    e = math.atan(xc / s)
    s0 = ls + a * in_xa - 1.0
    s1 = 2.0 * xc - 3.0 * s * e + a * xc * in_xa
    s2 = muc - 2.0 * a * ls + a * muc * in_xa
    s3 = (2.0 / 3.0) * xc * muc - 4.0 * a * xc + 5.0 * a * s * e - a * a * xc * in_xa
    s4 = 0.5 * muc * muc - 2.0 * a * muc + 3.0 * a * a * ls - a * a * muc * in_xa
    return np.array([h0, h1, h2, h3, h4]), np.array([s0, s1, s2, s3, s4])


def load_mott(path=MOTT_CS):
    txt = io.open(path, encoding='utf-8-sig').read()
    body = txt[txt.index('Coef ='):]
    body = body[body.index('{') + 1:body.index('};')]
    body = re.sub(r'//[^\n]*', '', body)
    nums = [float(x) for x in re.findall(r'[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?', body)]
    if len(nums) != 93 * 30:
        raise SystemExit(u'MottData.cs: ждали 2790 чисел, нашли %d' % len(nums))
    return np.array(nums).reshape(93, 30)


def current_poly(z, t, mott):
    """P ядерной части у НЫНЕШНЕГО кода: Лицзянь (`MottTerms`) при Z > 30, Мак-Кинли—Фешбах при Z ≤ 30
    (R = 1 − β²μ/2 + παZβ √(μ/2)(1 − √(μ/2)) по x = √μ)."""
    gamma, beta2, beta, p2 = kinematics(t)
    if z <= MOTT_MAX_Z:
        paz = math.pi * ALPHA * z * beta
        return np.array([1.0, paz / ROOT2, -0.5 * beta2 - 0.5 * paz, 0.0, 0.0])
    d = beta - MOTT_SHIFT
    row = mott[z]
    return np.array([sum(row[j * 6 + k] * d ** k for k in range(6)) for j in range(5)])


def poly(c, x):
    return c[0] + x * (c[1] + x * (c[2] + x * (c[3] + x * c[4])))


class Model(object):
    """Ядерная часть модели при (a, c) и энергии t: сечения, см² на атом."""

    def __init__(self, z, t, a, c, soft_r1=False):
        self.z, self.t, self.a, self.c = z, t, a, np.asarray(c, dtype=float)
        self.cz = common(t) * z * z
        self.soft_r1 = soft_r1          # нынешний код при Z ≤ 30: мягкая часть без поправки (R = 1)

    def hard(self, muc):
        return self.cz * float(self.c @ moments(self.a, muc)[0])

    def soft(self, muc):
        if self.soft_r1:
            return self.cz * float(moments(self.a, muc)[1][0])
        return self.cz * float(self.c @ moments(self.a, muc)[1])

    def tr1(self, muc=None):
        if self.soft_r1:
            muc = mu_of(DEG_CUT) if muc is None else muc
            tot = self.c @ moments(self.a, 2.0)[1]
            return self.soft(muc) + self.cz * float(tot - self.c @ moments(self.a, muc)[1])
        return self.cz * float(self.c @ moments(self.a, 2.0)[1])

    def dsdmu(self, mu):
        return self.cz * poly(self.c, math.sqrt(mu)) / (mu + self.a) ** 2


# ---------------------------------------------------------------- подгонка ------------------------
class NodeData(object):
    """Всё, что подгонке нужно от DPWA на узле (не зависит от a)."""

    def __init__(self, dcs, z):
        self.dcs = dcs
        self.t = dcs.t
        self.z = z
        self.cz = common(dcs.t) * z * z
        mlo = mu_of(DEG_LO)
        self.mlo = mlo
        self.mu_fit = mu_of(FIT_THETA)
        self.x_fit = np.sqrt(self.mu_fit)
        self.f_fit = dcs.dsdmu(self.mu_fit)
        self.tr1 = dcs.integ(1)
        self.h_lo = dcs.integ(0, mlo, 2.0)
        self.ht_lo = dcs.integ(1, mlo, 2.0)
        self.s_lo = dcs.integ(1, 0.0, mlo)
        mc = mu_of(DEG_CUT)
        self.h20 = dcs.integ(0, mc, 2.0)
        self.s20 = dcs.integ(1, 0.0, mc)


X_POS = np.linspace(0.0, ROOT2, 57)          # проверка P ≥ 0
POS_FLOOR = 1e-3


VANDER = np.stack([X_POS ** j for j in range(5)], 1)
POS_RELAXED = []                              # учёт узлов, где ради P ≥ 0 снята часть равенств


def kkt_solve(basis, target, eq, rhs):
    n, m = basis.shape[1], eq.shape[0]
    kkt = np.zeros((n + m, n + m))
    kkt[:n, :n] = 2.0 * basis.T @ basis
    kkt[:n, n:] = eq.T
    kkt[n:, :n] = eq
    b = np.concatenate([2.0 * basis.T @ target, rhs])
    try:
        sol = np.linalg.solve(kkt, b)
    except np.linalg.LinAlgError:
        sol = np.linalg.lstsq(kkt, b, rcond=None)[0]
    return sol[:n]


def solve_c(nd, a, positive=True):
    """Наименьшие квадраты относительной ошибки при равенствах σ(>10°), ∫_{10°}μdσ, ∫^{10°}μdσ.

    P ≥ 0 — активным набором: самая отрицательная точка проверки становится равенством P = POS_FLOOR,
    пока нарушений нет. Если пяти коэффициентов не хватает (три равенства + две точки), снимаются
    сперва раздельные равенства выше/ниже 10° (остаётся их сумма — полное σ_tr1 — и σ(>10°)), затем
    σ(>10°); полное σ_tr1 держится всегда. Такие узлы — только ниже ~3 кэВ (журнал полосы)."""
    mu, x = nd.mu_fit, nd.x_fit
    basis = np.stack([x ** j / (mu + a) ** 2 * nd.cz / nd.f_fit for j in range(5)], 1)
    target = np.ones(len(mu))
    hl, sl = moments(a, nd.mlo)
    st = moments(a, 2.0)[1]
    rows_all = [(hl * nd.cz / nd.h_lo, 1.0),                    # σ(>10°)
                ((st - sl) * nd.cz / nd.ht_lo, 1.0),            # ∫_{10°}^{180°} μ dσ
                (sl * nd.cz / nd.s_lo, 1.0)]                    # ∫_0^{10°} μ dσ
    tr1_row = (st * nd.cz / nd.tr1, 1.0)
    variants = [rows_all, [rows_all[0], tr1_row], [tr1_row]]
    c = None
    for level, rows in enumerate(variants):
        eq = np.array([r[0] for r in rows])
        rhs = np.array([r[1] for r in rows])
        active = []
        for _ in range(6):
            e2 = np.vstack([eq] + [VANDER[k][None, :] for k in active]) if active else eq
            r2 = np.concatenate([rhs, [POS_FLOOR] * len(active)])
            if e2.shape[0] > 5:
                break
            cand = kkt_solve(basis, target, e2, r2)
            p = poly(cand, X_POS)            # Горнером — как перенос и сторож импорта
            if not positive or np.min(p) >= 0.0:
                c = cand
                break
            k = int(np.argmin(p))
            if k in active:
                break
            active.append(k)
        if c is not None:
            if level > 0 and (nd.z, nd.t, level) not in POS_RELAXED:
                POS_RELAXED.append((nd.z, nd.t, level))
            break
    if c is None:
        raise RuntimeError(u'Z=%d T=%g: P ≥ 0 недостижимо даже при одном σ_tr1' % (nd.z, nd.t))
    r = basis @ c - target
    return c, float(np.sqrt(np.mean(r * r)))


def fit_a_tr1(nd, c_cur, soft_r1):
    """`a_tr1`: σ_tr1 нынешнего вида при экранировании a равно σ_tr1 DPWA."""
    def f(la):
        return Model(nd.z, nd.t, math.exp(la), c_cur, soft_r1).tr1() / nd.tr1 - 1.0
    return math.exp(brentq(f, math.log(1e-14), math.log(1e3), xtol=1e-13))


def fit_chain(nodes):
    """Проход 1: свободное a с продолжением сверху вниз по энергии; проход 2: гладкое a, только c."""
    order = sorted(nodes.keys(), reverse=True)
    la = {}
    prev = None
    for ie in order:
        nd = nodes[ie]
        if prev is None:
            la0 = math.log(fit_a_tr1(nd, np.array([1.0, 0, 0, 0, 0]), False))
            lo, hi = la0 - 6.0, la0 + 3.0
        else:
            lo, hi = prev - 0.7, prev + 0.7
        res = minimize_scalar(lambda v: solve_c(nd, math.exp(v), positive=False)[1], bounds=(lo, hi),
                              method='bounded', options={'xatol': 1e-8})
        la[ie] = res.x
        prev = res.x
    ies = np.array(sorted(nodes.keys()))
    lt = np.log(np.array([nodes[i].t for i in ies]))
    raw = np.array([la[i] for i in ies])
    smooth = smooth_series(lt, raw)
    out = {}
    for i, ie in enumerate(ies):
        a = math.exp(smooth[i])
        c, rms = solve_c(nodes[ie], a)
        _, rms_free = solve_c(nodes[ie], math.exp(raw[i]))
        out[ie] = (a, c, rms, rms_free)
    return out


def smooth_series(x, y, half=3):
    """Локальная квадратичная регрессия с весами Тьюки (окно ±half узлов) — сглаживание ln a(ln T)."""
    out = np.empty_like(y)
    n = len(y)
    span = (half + 1) * (x[1] - x[0])
    for i in range(n):
        lo, hi = max(0, i - half), min(n, i + half + 1)
        xx = x[lo:hi] - x[i]
        w = (1.0 - (np.abs(xx) / span) ** 3) ** 3
        coef = np.polyfit(xx, y[lo:hi], 2, w=np.sqrt(w))
        out[i] = coef[-1]
    return out


# ---------------------------------------------------------------- таблица -------------------------
HEADER_COLUMNS = ['z', 't_kev', 'a_tr1', 'a', 'c0', 'c1', 'c2', 'c3', 'c4', 'fit_rms',
                  'dpwa_tr1_cm2', 'dpwa_h20_cm2', 'dpwa_s20_cm2']


def build_one(args):
    """Один Z одной частицы (отдельным процессом: `--jobs`)."""
    root, kind, z = args
    del SAFE_LOG_FIXES[:]
    del POS_RELAXED[:]
    dpwa = Dpwa(root)
    mott = load_mott()
    nodes = {ie: NodeData(dpwa.node(z, kind, ie), z) for ie in range(IE_LO, IE_HI + 1)}
    chain = fit_chain(nodes)
    rows = []
    worst = worst_free = 0.0
    for ie in range(IE_LO, IE_HI + 1):
        nd = nodes[ie]
        a, c, rms, rms_free = chain[ie]
        a_tr1 = None
        if kind == 'el':
            a_tr1 = fit_a_tr1(nd, current_poly(z, nd.t, mott), z <= MOTT_MAX_Z)
        rows.append((z, nd.t, a_tr1, a, c, rms, nd.tr1, nd.h20, nd.s20))
        if nd.t >= 10.0:
            worst = max(worst, rms)
            worst_free = max(worst_free, rms_free)
    good_from = None
    for r in reversed(rows):
        if r[5] > 0.02:
            break
        good_from = r[1]
    note = u'  %s Z=%2d: худшая ско формы от 10 кэВ %.4f (свободное a %.4f); ско ≤ 0.02 от %s кэВ; правок ln dσ/dΩ ≤ 0: %d; узлов со снятыми равенствами ради P ≥ 0: %s' % (
        kind, z, worst, worst_free, '%.4g' % good_from if good_from else '—', sum(SAFE_LOG_FIXES),
        ', '.join('%.3g кэВ (ур. %d)' % (t, lv) for zz, t, lv in POS_RELAXED) or '0')
    return rows, note


def build(root, kind, zs, jobs, log):
    tasks = [(root, kind, z) for z in zs]
    if jobs > 1:
        from concurrent.futures import ProcessPoolExecutor
        with ProcessPoolExecutor(max_workers=jobs) as ex:
            results = list(ex.map(build_one, tasks))
    else:
        results = [build_one(t) for t in tasks]
    rows = []
    for part, note in results:
        rows.extend(part)
        log(note)
    return rows


def write_table(path, kind, rows, g4emlow):
    part = u'электрон (e⁻)' if kind == 'el' else u'позитрон (e⁺)'
    with io.open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(u'# Экранирование упругого рассеяния по DPWA — %s (AMBER274, физика 29, Ф1; полоса screen29, 02.11.2026).\n' % part)
        f.write(u'# Построено tools/nucdb/build_dpwa_screening.py из G4EMLOW8.8/dpwa (ELSEPA: Salvat, Jablonski, Powell,\n')
        f.write(u'#   CPC 165 (2005) 157; таблицы M. Novak, CERN 2020). Условия: README G4EMLOW — «DATA ARE *NOT* FOR\n')
        f.write(u'#   COMMERCIAL USE AND MUST BE USED WITHIN GEANT4»; Geant4 Software License — уведомление и ссылка\n')
        f.write(u'#   «This product includes software developed by Members of the Geant4 Collaboration». Таблица — подгонка\n')
        f.write(u'#   (производная). Решение Amber 02.11.2026, дословно: «BecqMoni - не коммерческий продукт. Просто возьми\n')
        f.write(u'#   готовые данные.» Сверка со своим расчётом ELSEPA — журнал полосы screen29, §11.\n')
        f.write(u'# Вид ядерной части: dσ/dμ = C(T) Z² P(√μ)/(μ + a)², μ = 1 − cos θ, C = 2π r_e² m²/(β² p²);\n')
        f.write(u'#   P(x) = c0 + c1 x + c2 x² + c3 x³ + c4 x⁴. a, c — подгонка к DPWA (равенства σ(>10°), ∫μdσ выше\n')
        f.write(u'#   и ниже 10°; МНК dσ/dΩ на 10…180°; P ≥ 0; a гладкая по ln T). Между узлами — линейно по ln T\n')
        f.write(u'#   (ln a и c_j); ниже 1 кэВ и выше 10 МэВ — крайний узел.\n')
        f.write(u'# a_tr1 (только e⁻): экранирование, при котором НЫНЕШНИЙ P (Лицзянь Z > 30, Мак-Кинли—Фешбах Z ≤ 30,\n')
        f.write(u'#   мягкая часть Z ≤ 30 без поправки, отсечка 20°) даёт σ_tr1 DPWA — постановка GS. Пусто у e⁺.\n')
        f.write(u'# fit_rms — ско относительной ошибки dσ/dΩ на 10…180°; dpwa_* — σ_tr1, σ(>20°), ∫_0^{20°} μ dσ\n')
        f.write(u'#   по DPWA, см² на атом. Единицы: t_kev — кэВ, a и c — безразмерные. Разделитель дробной части — точка.\n')
        f.write(u'\t'.join(HEADER_COLUMNS) + u'\n')
        for z, t, a_tr1, a, c, rms, tr1, h20, s20 in rows:
            vals = [str(z), repr(float(t)), '' if a_tr1 is None else repr(float(a_tr1)), repr(float(a))]
            vals += [repr(float(v)) for v in c]
            vals += [repr(float(rms)), repr(float(tr1)), repr(float(h20)), repr(float(s20))]
            f.write(u'\t'.join(vals) + u'\n')


def read_table(path):
    data = {}
    with io.open(path, encoding='utf-8') as f:
        header = None
        for line in f:
            if line.startswith('#'):
                continue
            parts = line.rstrip('\n').split('\t')
            if header is None:
                header = parts
                continue
            rec = dict(zip(header, parts))
            z = int(rec['z'])
            data.setdefault(z, []).append((float(rec['t_kev']),
                                           float(rec['a_tr1']) if rec['a_tr1'] else None,
                                           float(rec['a']),
                                           np.array([float(rec['c%d' % j]) for j in range(5)])))
    return data


def interp(rows, t):
    """Как в переносе: линейно по ln T от ln a и c_j; вне сетки — крайний узел."""
    ts = [r[0] for r in rows]
    if t <= ts[0]:
        return rows[0][2], rows[0][3], rows[0][1]
    if t >= ts[-1]:
        return rows[-1][2], rows[-1][3], rows[-1][1]
    k = int(np.searchsorted(ts, t)) - 1
    u = (math.log(t) - math.log(ts[k])) / (math.log(ts[k + 1]) - math.log(ts[k]))
    a = math.exp((1 - u) * math.log(rows[k][2]) + u * math.log(rows[k + 1][2]))
    c = (1 - u) * rows[k][3] + u * rows[k + 1][3]
    at = None
    if rows[k][1] is not None:
        at = math.exp((1 - u) * math.log(rows[k][1]) + u * math.log(rows[k + 1][1]))
    return a, c, at


# ---------------------------------------------------------------- проверка -------------------------
CHECK_ANGLES = (90.0, 105.0, 120.0, 135.0, 150.0, 165.0, 180.0)


def ratios(model, dcs):
    mc = mu_of(DEG_CUT)
    tr1 = model.tr1() / dcs.integ(1)
    h = model.hard(mc) / dcs.integ(0, mc, 2.0)
    s = model.soft(mc) / dcs.integ(1, 0.0, mc)
    d = [model.dsdmu(float(mu_of(th))) / float(dcs.dsdmu(mu_of(th))) for th in CHECK_ANGLES]
    return tr1, h, s, min(d), max(d)


def check(dpwa, mott, table, kind, zs, energies, log):
    log(u'== %s: модель / DPWA — σ_tr1, σ(>20°), ∫_0^{20°}μdσ, dσ/dΩ на 90…180° (наименьшее…наибольшее);'
        % ('e⁻' if kind == 'el' else 'e⁺'))
    log(u'   «было» — нынешний код (Мольер + Лицзянь/МкФ), «a_tr1» — постановка GS, «стало» — a + P таблицы.')
    log(u'   * — середина между узлами DPWA (интерполяция таблицы по ln T, DPWA — кубика по ln E).')
    worst = {}
    for z in zs:
        rows = table[z]
        for t, mid in energies:
            dcs = dpwa.between(z, kind, t) if mid else dpwa.node(z, kind, int(np.argmin(np.abs(dpwa.energy - t))))
            t = dcs.t
            a, c, a_tr1 = interp(rows, t)
            # «было»: и у e⁺ — тот же вид, что у e⁻ (перенос заряда не знает, находка Ф4)
            cur = Model(z, t, moliere_two_a(z, t), current_poly(z, t, mott), z <= MOTT_MAX_Z)
            new = Model(z, t, a, c)
            parts = [ratios(cur, dcs)]
            if kind == 'el':
                parts.append(ratios(Model(z, t, a_tr1, current_poly(z, t, mott), z <= MOTT_MAX_Z), dcs))
            r_new = ratios(new, dcs)
            parts.append(r_new)
            if t >= 10.0:
                w = worst.setdefault(z, [0.0] * 5)
                dev = [abs(r_new[0] - 1), abs(r_new[1] - 1), abs(r_new[2] - 1),
                       max(abs(r_new[3] - 1), abs(r_new[4] - 1)), 0.0]
                for i in range(4):
                    w[i] = max(w[i], dev[i])
            txt = u' | '.join(u'%.3f %.3f %.3f %.3f…%.3f' % p for p in parts)
            log(u'  Z=%2d %9.2f%s | %s' % (z, t, u'*' if mid else u' ', txt))
    log(u'  наихудшее «стало» от 10 кэВ, |отн. откл.|: Z → σ_tr1, σ(>20°), мягкая, dσ/dΩ 90…180°')
    for z in zs:
        w = worst.get(z)
        if w:
            log(u'    Z=%2d: %.4f %.4f %.4f %.4f' % (z, w[0], w[1], w[2], w[3]))
    return worst


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--g4emlow', default=os.environ.get('G4LEDATA', DEFAULT_G4EMLOW))
    ap.add_argument('--out', default=os.path.join(HERE, 'data', 'dpwa_screening'))
    ap.add_argument('--zmax', type=int, default=ZMAX)
    ap.add_argument('--z', default=None, help='список Z для пробной сборки (по умолчанию 1…zmax)')
    ap.add_argument('--jobs', type=int, default=1)
    ap.add_argument('--check', action='store_true')
    ap.add_argument('--check-z', default='6,8,13,14,26,29,32,53,55,82,83')
    ap.add_argument('--kinds', default='el,pos')
    args = ap.parse_args()
    if not os.path.isfile(os.path.join(args.g4emlow, 'dpwa', 'grid.dat')):
        raise SystemExit(u'нет %s\\dpwa\\grid.dat — укажите --g4emlow' % args.g4emlow)
    dpwa = Dpwa(args.g4emlow)
    mott = load_mott()
    kinds = args.kinds.split(',')
    if not args.check:
        os.makedirs(args.out, exist_ok=True)
        for kind in kinds:
            zs = [int(z) for z in args.z.split(',')] if args.z else list(range(1, args.zmax + 1))
            rows = build(args.g4emlow, kind, zs, args.jobs, print)
            path = os.path.join(args.out, 'dpwa_screening_%s.tsv' % kind)
            write_table(path, kind, rows, args.g4emlow)
            print(u'записано %s: %d строк' % (path, len(rows)))
        return 0
    zs = [int(z) for z in args.check_z.split(',')]
    energies = [(1.0, False), (3.98, False), (10.0, False), (18.48, False), (30.0, True), (46.42, False),
                (75.0, True), (100.0, False), (184.78, False), (292.86, False), (400.0, True), (1000.0, False),
                (1500.0, True), (2154.43, False), (3000.0, True)]
    for kind in kinds:
        table = read_table(os.path.join(args.out, 'dpwa_screening_%s.tsv' % kind))
        check(dpwa, mott, table, kind, zs, energies, print)
    return 0


if __name__ == '__main__':
    sys.exit(main())
