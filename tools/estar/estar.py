# -*- coding: utf-8 -*-
"""
Своя реализация ESTAR: пробег CSDA и выход тормозного излучения для электрона.

Зачем. Поставка NIST возит ВХОДЫ (оболочки, радиационная тормозная, составы,
средние энергии возбуждения) и программу, но не свой выход; `ESTAR.EXE` —
16-битный DOS-бинарь и на 64-битной Windows не идёт. Из-за этого в
`BecquerelMonitor/EfficiencyMaker/ElectronData.cs` вшиты готовые числа ровно
для четырёх веществ (CsI, NaI, BGO, LaBr3), а у CeBr3, SrI2, CdTe, CZT и GSO
`ElectronData.Match` возвращает null, и поправка на тормозное не считается
вовсе.

Алгоритм — ICRU 37, читается в `ESTAR.f` (строки 106-302). Здесь он повторён
по исходнику, а не по учебнику: расхождение в мелочи вроде ALF последней
оболочки видно только по коду.

Все входные данные берутся из `matdb.sqlite`: `estar_shells`,
`estar_radiative_stopping`, `estar_element_potential`, `star_materials`,
`xcom_elements`.

Проверка даровая: четыре вшитых вещества — эталон, снятый с настоящего ESTAR.

    python estar.py <matdb.sqlite> check        # сверка с четырьмя эталонами
    python estar.py <matdb.sqlite> table        # выдать таблицу для C#
"""
import math
import sqlite3
import sys

import numpy as np
from scipy.interpolate import CubicSpline

RMASS = 0.510999906          # ESTAR.f, DATA RMASS — масса покоя электрона, МэВ
COFF = 0.307072              # ESTAR.f, DATA COFF — множитель формулы Бете
QBEG, NUMQ, LMAX = 1.0e-04, 50, 1101       # сетка по Q для эффекта плотности
MGRD = 21                    # узлов Симпсона на интервал сетки при интегрировании

# Сетка энергий ESTAR (113 точек, МэВ) — DATA ER в ESTAR.f. Совпадает с сеткой
# `estar_radiative_stopping`, поэтому берётся прямо из базы.


class Material(object):
    """Вещество: массовые доли по Z, плотность, средняя энергия возбуждения."""

    def __init__(self, name, fractions, density, potential_ev=None):
        self.name = name
        self.fractions = dict(fractions)
        self.density = density
        self.potential_ev = potential_ev


def from_formula(db, name, formula, density, potential_ev=None):
    """«Cs1 I1» -> массовые доли. Веса — из xcom_elements, как ATB у ESTAR."""
    weights = dict(db.execute("select z, atomic_weight from xcom_elements"))
    # Символы — из `xcom_elements`, а НЕ из `nuclides`: после разреза базы
    # (D25, 08.08.2026) `nuclides` уехала в `nucdb.sqlite`, и запрос к ней
    # ронял estar.py на первом же веществе — «no such table: nuclides».
    # Шапка модуля всё это время утверждала, что читается `xcom_elements`, так
    # что расходились не поставки, а код и его же описание. Заодно это лучший
    # источник: `xcom_elements.symbol` заполнен у всех ста элементов и
    # канонизирован разрезом, тогда как в `nuclides` написания конфликтовали
    # (`TI` против `Ti`, N12).
    symbols = {s.upper(): z for z, s in db.execute(
        "select z, symbol from xcom_elements where symbol is not null")}
    atoms = {}
    for part in formula.split():
        i = 0
        while i < len(part) and not part[i].isdigit():
            i += 1
        z = symbols[part[:i].upper()]
        atoms[z] = atoms.get(z, 0.0) + (float(part[i:]) if i < len(part) else 1.0)
    total = sum(n * weights[z] for z, n in atoms.items())
    return Material(name, {z: n * weights[z] / total for z, n in atoms.items()},
                    density, potential_ev)


def tabulated_potential(db, fractions):
    """I готового вещества из `star_materials`, если состав с ним совпал.

    ESTAR берёт I ИЗ ТАБЛИЦЫ, когда вещество выбрано из списка, и считает по
    Брэггу, только когда состав ввели руками. Разница не всегда мелкая: у
    иодида цезия и иодида натрия правило Брэгга даёт табличное значение до
    сотых (553.10 и 452.01 против 553.1 и 452.0), а у германата висмута —
    523.5 против табличных 534.1, и это уже 0.4 % в пробеге.
    """
    rows = db.execute("select id, potential_ev from star_materials").fetchall()
    for material_id, potential in rows:
        comp = dict(db.execute("select z, weight_fraction from"
                               " star_material_composition where material_id=?",
                               (material_id,)))
        if set(comp) != set(fractions):
            continue
        if all(abs(comp[z] - fractions[z]) < 1.0e-3 for z in comp):
            return potential
    return None


def bragg_potential(db, fractions):
    """I смеси по правилу Брэгга — `ESTAR.f:714-734`.

    ln I = Σ wᵢ (Z/A)ᵢ ln Iᵢ / Σ wᵢ (Z/A)ᵢ, причём для элементов ТЯЖЕЛЕЕ неона
    ESTAR берёт не табличное I элемента, а 1.13·I: в соединении электроны
    связаны сильнее, чем в чистом веществе.
    """
    weights = dict(db.execute("select z, atomic_weight from xcom_elements"))
    pot = dict(db.execute("select z, potential_ev from estar_element_potential"))
    zav = 0.0
    acc = 0.0
    for z, w in fractions.items():
        za = z / weights[z]
        value = pot[z] if z < 10 else 1.13 * pot[z]
        zav += w * za
        acc += w * za * math.log(value)
    return math.exp(acc / zav)


def density_effect(db, material, zav, potential_ev):
    """Поправка на плотность по Штернхеймеру — `ESTAR.f:166-230, 390-530`.

    Возвращает (ln YQ, D) для интерполяции и порог YCUT. Осцилляторы строятся
    из заселённостей и энергий связи оболочек: у каждой оболочки своя сила
    f(n), а масштаб подбирается ньютоновской итерацией так, чтобы правило сумм
    сошлось с заданным I.
    """
    hom = 28.81593 * math.sqrt(material.density * zav)      # плазменная энергия, эВ
    phil = 2.0 * math.log(potential_ev / hom)

    weights = dict(db.execute("select z, atomic_weight from xcom_elements"))
    g = {z: w * z / weights[z] for z, w in material.fractions.items()}
    gtot = sum(g.values())
    g = {z: value / gtot for z, value in g.items()}

    single = len(material.fractions) == 1
    f, en = [], []
    for z in sorted(material.fractions):
        shells = db.execute("select occupation, binding_ev from estar_shells"
                            " where z=? order by shell_index", (z,)).fetchall()
        occ = [o for o, _ in shells]
        bind = [b for _, b in shells]
        # знак заселённости значащий: отрицательная метит проводящую оболочку
        if occ[-1] < 0:
            occ[-1] = -occ[-1]
            if single:
                bind[-1] = 0.0
        nsum = float(sum(occ))
        for k in range(len(occ)):
            f.append(occ[k] * g[z] / nsum)
            en.append(bind[k])

    f = np.array(f)
    en = np.array(en)
    alf = np.full(len(f), 2.0 / 3.0)
    if en[-1] <= 0.0:
        alf[-1] = 1.0

    eps = (en / hom) ** 2
    root = 1.0
    for _ in range(200):
        trm = root * eps + alf * f
        fun = -phil + float(np.sum(f * np.log(trm)))
        der = float(np.sum(f * eps / trm))
        droot = fun / der
        root -= droot
        if abs(droot) <= 1.0e-5:
            break
    eps = root * eps

    ycut = 0.0 if en[-1] <= 0.0 else 1.0 / float(np.sum(f / eps))

    q = QBEG * np.power(10.0, np.arange(LMAX) / float(NUMQ))
    yq = 1.0 / np.sum(f[None, :] / (eps[None, :] + q[:, None]), axis=1)
    d = (np.sum(f[None, :] * np.log(1.0 + q[:, None] / (eps + alf * f)[None, :]), axis=1)
         - q / (yq + 1.0))
    return np.log(yq), d, ycut


def stopping(db, material):
    """Тормозная способность на сетке ESTAR: (T, S_столкн, S_рад), МэВ·см²/г."""
    weights = dict(db.execute("select z, atomic_weight from xcom_elements"))
    zav = sum(w * z / weights[z] for z, w in material.fractions.items())

    potential = (material.potential_ev
                 or tabulated_potential(db, material.fractions)
                 or bragg_potential(db, material.fractions))
    material.potential_used = potential
    potl = math.log(potential * 1.0e-06)

    yql, dd, ycut = density_effect(db, material, zav, potential)

    grid = [r[0] for r in db.execute(
        "select distinct energy_mev from estar_radiative_stopping order by energy_mev")]
    t = np.array(grid)
    radiative = np.zeros(len(t))
    for z, w in material.fractions.items():
        rows = db.execute("select energy_mev, stopping_mev_cm2_g from"
                          " estar_radiative_stopping where z=? order by energy_mev",
                          (z,)).fetchall()
        radiative += w * np.array([r[1] for r in rows])

    tau = t / RMASS
    y = tau * (tau + 2.0)
    betq = y / (tau + 1.0) ** 2
    delta = np.zeros(len(t))
    inside = (y >= math.exp(yql[0])) & (y > ycut)
    delta[inside] = CubicSpline(yql, dd)(np.log(y[inside]))

    spart = np.log(t) - potl + 0.5 * np.log(1.0 + 0.5 * tau) - 0.5 * delta
    term = (1.0 - betq) * (1.0 + tau ** 2 / 8.0 - (2.0 * tau + 1.0) * math.log(2.0))
    collision = COFF * zav * (spart + 0.5 * term) / betq
    return t, collision, radiative


def range_and_yield(t, collision, radiative):
    """Пробег CSDA (г/см²) и выход тормозного — `ESTAR.f:596-695`.

    Интегрируется не по узлам сетки, а по сплайну ln S от ln T, Симпсоном по
    MGRD точкам внутри каждого интервала: сетка ESTAR редкая, и трапеции по её
    узлам дают у нижнего края проценты.

    Первый узел особый: ниже него формула Бете уже не работает, и ESTAR берёт
    для этого куска линейное приближение R = T/(2·S).
    """
    total = collision + radiative
    tl = np.log(t)
    spline_total = CubicSpline(tl, np.log(total))
    spline_rad = CubicSpline(tl, np.log(radiative))

    rg = np.zeros(len(t))
    rad = np.zeros(len(t))
    rg[0] = 0.5 * t[0] / total[0]
    rad[0] = 0.5 * t[0] * radiative[0] / total[0]
    for i in range(1, len(t)):
        lo, hi = t[i - 1], t[i]
        step = (hi - lo) / (MGRD - 1)
        points = np.log(hi - step * np.arange(MGRD))
        inv = np.exp(-spline_total(points))
        rg[i] = rg[i - 1] + simpson(inv, step / 3.0)
        rad[i] = rad[i - 1] + simpson(np.exp(spline_rad(points)) * inv, step / 3.0)
    return rg, rad / t


def simpson(values, third_step):
    """GRAL из ESTAR.f для нечётного числа узлов: множитель уже поделён на 3."""
    sigma = values[0] + values[-1] + 4.0 * np.sum(values[1:-1:2]) + 2.0 * np.sum(values[2:-2:2])
    return third_step * sigma


# Вещества для проверки. Плотности и составы — те же, что в ElectronData.cs:
# CsI/NaI/BGO — готовые составы ESTAR (141, 252, 117), LaBr3 считался через
# форму пользовательского состава с I = 454.5 эВ (её ESTAR вывел сам).
CHECK = [
    ("CsI", "Cs1 I1", 4.51, None),
    ("NaI", "Na1 I1", 3.667, None),
    ("BGO", "Bi4 Ge3 O12", 7.13, None),
    ("LaBr3", "La1 Br3", 5.08, None),
]

WANTED = [
    ("CeBr3", "Ce1 Br3", 5.1),
    ("SrI2", "Sr1 I2", 4.55),
    ("CdTe", "Cd1 Te1", 5.85),
    ("CZT", "Cd9 Zn1 Te10", 5.78),
    ("GSO", "Gd2 Si1 O5", 6.71),
    ("Ge", "Ge1", 5.323),
]

# Сетка ElectronData.Grid — стандартный список ENG.ELE от 1 кэВ до 3 МэВ.
#
# ДО 09.08.2026 она начиналась с 10 кэВ, и этот обрез был причиной M7:
# интеграл тормозного по пути торможения не добирал того, что излучается на
# последних десяти килоэлектронвольтах, а это ровно `Y(10 кэВ) / Y(T)` —
# 11.4 % от полного выхода при T = 100 кэВ и 0.97 % при 2614 кэВ. Измеренная
# подтяжка к ESTAR была 1.089 и 1.010: на верху шкалы совпадает в сотых долях
# процента. Внутренняя сетка `estar.py` и так идёт от 1 кэВ, обрезалась только
# выдача.
#
# Первые 16 значений — та же решётка на декаду (1.00, 1.25, 1.50, 1.75, 2.00,
# 2.50 … 9.00), что и во всех остальных декадах списка.
OUT_GRID = [
    1.000e-03, 1.250e-03, 1.500e-03, 1.750e-03, 2.000e-03, 2.500e-03,
    3.000e-03, 3.500e-03, 4.000e-03, 4.500e-03, 5.000e-03, 5.500e-03,
    6.000e-03, 7.000e-03, 8.000e-03, 9.000e-03,
    1.000e-02, 1.250e-02, 1.500e-02, 1.750e-02, 2.000e-02, 2.500e-02,
    3.000e-02, 3.500e-02, 4.000e-02, 4.500e-02, 5.000e-02, 5.500e-02,
    6.000e-02, 7.000e-02, 8.000e-02, 9.000e-02, 1.000e-01, 1.250e-01,
    1.500e-01, 1.750e-01, 2.000e-01, 2.500e-01, 3.000e-01, 3.500e-01,
    4.000e-01, 4.500e-01, 5.000e-01, 5.500e-01, 6.000e-01, 7.000e-01,
    8.000e-01, 9.000e-01, 1.000e+00, 1.250e+00, 1.500e+00, 1.750e+00,
    2.000e+00, 2.500e+00, 3.000e+00,
]


def compute(db, name, formula, density, potential_ev=None):
    material = from_formula(db, name, formula, density, potential_ev)
    t, collision, radiative = stopping(db, material)
    rg, yield_ = range_and_yield(t, collision, radiative)
    grid = np.array(OUT_GRID)
    lr = CubicSpline(np.log(t), np.log(rg))(np.log(grid))
    ly = CubicSpline(np.log(t), np.log(yield_))(np.log(grid))
    return np.exp(lr), np.exp(ly), material.potential_used


def main():
    db = sqlite3.connect(sys.argv[1])
    mode = sys.argv[2] if len(sys.argv) > 2 else "check"

    if mode == "check":
        import reference
        # Эталон NIST снят на СТАРОЙ сетке от 10 кэВ (39 точек). Сетка выдачи
        # с 09.08.2026 длиннее, поэтому сверяется общий хвост, а не начало:
        # подставить 55 наших значений под 39 эталонных значило бы сверять
        # сдвинутые энергии и получить красивую бессмыслицу.
        for name, formula, density, potential in CHECK:
            rg, yield_, used = compute(db, name, formula, density, potential)
            ref_rg, ref_yield = reference.DATA[name]
            n = len(ref_rg)
            if len(rg) < n:
                sys.exit(u"эталон длиннее выдачи: %d против %d" % (n, len(rg)))
            rg, yield_ = rg[-n:], yield_[-n:]
            dr = 100.0 * (rg / np.array(ref_rg) - 1.0)
            dy = 100.0 * (yield_ / np.array(ref_yield) - 1.0)
            print("%-6s I=%6.1f эВ | пробег: медиана %+.3f%%, макс |%.3f%%| при "
                  "%g МэВ | выход: медиана %+.3f%%, макс |%.3f%%| при %g МэВ"
                  % (name, used, np.median(dr), np.max(np.abs(dr)),
                     OUT_GRID[-n:][int(np.argmax(np.abs(dr)))],
                     np.median(dy), np.max(np.abs(dy)),
                     OUT_GRID[-n:][int(np.argmax(np.abs(dy)))]))
    else:
        for name, formula, density in WANTED:
            rg, yield_, used = compute(db, name, formula, density)
            print("// %s, %s, %g г/см3, I = %.1f эВ" % (name, formula, density, used))
            print("Range: " + ", ".join("%.3E" % x for x in rg))
            print("Yield: " + ", ".join("%.3E" % x for x in yield_))


if __name__ == "__main__":
    main()
