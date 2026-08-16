"""Сцена «детектор на грунте»: какого размера цилиндр даёт 98 % сигнала.

Зачем. В формате геометрий источника «полупространство» нет (`GeometrySourceType`
знает точку, цилиндр, маринелли и наш короб), а прибор, положенный на землю,
меряет именно полупространство. Значит, грунт приходится подменять цилиндром
«побольше» — и надо знать, насколько побольше, иначе кривая эффективности
считается для сцены, в которой сигнала не хватает, и недобор молча уходит в
общий множитель.

Что считается. Нерассеянный поток 662 кэВ в ТОЧКЕ, где стоит кристалл, от
равномерно активного полупространства. Для площади фотопика это и есть ответ:
рассеявшийся квант в пик не попадает. Для континуума сцену надо брать шире —
здесь этого не считают.

Геометрия: грунт занимает z > 0, детектор поднят на h над поверхностью, ось
вертикальна. Вклад элемента (r, z):

    dPhi = dV / (4 pi d^2) * exp(-mu_s * L_s - mu_a * L_a),
    u = z + h,  d = sqrt(r^2 + u^2),  L_s = z * d/u,  L_a = h * d/u.

Интеграл по радиусу берётся аналитически (подстановка t = k d/u сводит его к
интегральной показательной E1), по глубине — численно:

    Phi(R, D) = 1/2 * int_0^D [ E1(k) - E1(k * sqrt(R^2+u^2)/u) ] dz,
    k(z) = mu_s z + mu_a h.

Полный поток — это же при R -> бесконечность (E1(inf) = 0) и D -> бесконечность.

⚠ Воздух в счёте ОСТАВЛЕН и выкидывать его нельзя: у скользящих лучей путь по
воздуху не мал. С высоты 3 см источник в 8 м виден под углом, при котором луч
идёт по воздуху те же 8 м, а это уже 8 % ослабления — ровно та добавка, которая
и заставляет далёкий вклад сойтись.

Запуск (числа печатаются, ничего не пишется):

    python tools/effmaker/ground_halfspace.py
    python tools/effmaker/ground_halfspace.py --energy=1461 --density=1.3

E27. Смысл чисел и оговорки — в `tools/effmaker/README.md`.
"""

import argparse
import math

import numpy as np
from scipy.integrate import quad
from scipy.special import exp1
from scipy.optimize import brentq

# Массовый коэффициент ослабления, см2/г. Таблица короткая нарочно: скрипт
# отвечает на вопрос «во сколько свободных пробегов укладывается сцена», а
# пробег в него входит одним числом. Значения — NIST XCOM для стандартного
# грунта (SiO2 + Al2O3 + Fe2O3, Z/A = 0.4989) и сухого воздуха.
MU_SOIL = {
    59.5: 0.1873,
    186.0: 0.1226,
    351.9: 0.0999,
    661.7: 0.0770,
    1173.2: 0.0587,
    1460.8: 0.0530,
    2614.5: 0.0400,
}

MU_AIR = {
    59.5: 0.1875,
    186.0: 0.1288,
    351.9: 0.1052,
    661.7: 0.0857,
    1173.2: 0.0636,
    1460.8: 0.0570,
    2614.5: 0.0429,
}


def _interp(table, energy):
    keys = sorted(table)
    return float(np.interp(math.log(energy),
                           [math.log(k) for k in keys],
                           [table[k] for k in keys]))


def flux(radius_cm, depth_cm, mu_s, mu_a, height_cm):
    """Поток от цилиндра радиуса `radius_cm` и глубины `depth_cm`.

    Бесконечность допускается обоими аргументами: E1(inf) = 0, и формула
    вырождается в полный поток сама.
    """

    def integrand(z):
        u = z + height_cm
        k = mu_s * z + mu_a * height_cm
        if k <= 0.0:
            return 0.0
        near = exp1(k)
        if math.isinf(radius_cm):
            return 0.5 * near
        far = exp1(k * math.sqrt(radius_cm * radius_cm + u * u) / u)
        return 0.5 * (near - far)

    # Разбиение по глубине: у поверхности подынтегральная функция логарифмически
    # растёт (E1(k) ~ -ln k при k -> 0), и одним куском квадратура её не берёт.
    limit = depth_cm if math.isfinite(depth_cm) else 40.0 / mu_s
    edges = [0.0]
    step = 0.01 / mu_s
    while edges[-1] < limit:
        edges.append(min(limit, edges[-1] + step))
        step *= 1.6

    total = 0.0
    for a, b in zip(edges, edges[1:]):
        total += quad(integrand, a, b, limit=200)[0]
    return total


def solve(fraction, mu_s, mu_a, height_cm):
    """Радиус и глубина, при которых собирается заданная доля потока.

    Считаются ПОСЛЕДОВАТЕЛЬНО и обе от полного потока: сначала глубина при
    бесконечном радиусе, потом радиус при этой глубине. Совместная доля от этого
    получается чуть ниже заданной — её и печатаем, чтобы не выдавать желаемое.
    """
    full = flux(math.inf, math.inf, mu_s, mu_a, height_cm)

    depth = brentq(lambda d: flux(math.inf, d, mu_s, mu_a, height_cm) / full - fraction,
                   0.01 / mu_s, 60.0 / mu_s, xtol=1e-4 / mu_s)
    radius = brentq(lambda r: flux(r, math.inf, mu_s, mu_a, height_cm) / full - fraction,
                    0.1 / mu_s, 5000.0 / mu_s, xtol=1e-3 / mu_s)
    joint = flux(radius, depth, mu_s, mu_a, height_cm) / full
    return radius, depth, joint


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--energy", type=float, default=661.7, help="энергия, кэВ")
    p.add_argument("--density", type=float, default=1.5, help="плотность грунта, г/см3")
    p.add_argument("--height", type=float, default=3.0, help="кристалл над грунтом, см")
    p.add_argument("--fraction", type=float, default=0.98, help="требуемая доля потока")
    args = p.parse_args()

    mu_s = _interp(MU_SOIL, args.energy) * args.density
    mu_a = _interp(MU_AIR, args.energy) * 0.001205
    lam = 1.0 / mu_s

    radius, depth, joint = solve(args.fraction, mu_s, mu_a, args.height)
    print("энергия %.1f кэВ, грунт %.2f г/см3, свободный пробег %.2f см"
          % (args.energy, args.density, lam))
    print("высота кристалла над грунтом %.1f см = %.2f пробега" % (args.height, args.height / lam))
    print("радиус  %8.1f см = %6.1f пробега" % (radius, radius / lam))
    print("глубина %8.1f см = %6.1f пробега" % (depth, depth / lam))
    print("совместная доля при этих двух размерах: %.3f" % joint)

    print("\nчто даёт сцена поменьше (радиус, доля от полного потока при полной глубине):")
    full = flux(math.inf, math.inf, mu_s, mu_a, args.height)
    for r in (25.0, 50.0, 100.0, 150.0, 200.0, 300.0, 500.0):
        print("  R = %5.0f см (%5.1f пробега): %.3f"
              % (r, r / lam, flux(r, math.inf, mu_s, mu_a, args.height) / full))

    print("\nустойчивость доли пробега к энергии (та же плотность, та же высота):")
    for e in (59.5, 186.0, 351.9, 661.7, 1173.2, 1460.8, 2614.5):
        ms = _interp(MU_SOIL, e) * args.density
        ma = _interp(MU_AIR, e) * 0.001205
        r, d, _ = solve(args.fraction, ms, ma, args.height)
        print("  %7.1f кэВ: пробег %5.2f см, радиус %5.1f пробега, глубина %5.2f пробега"
              % (e, 1.0 / ms, r * ms, d * ms))


if __name__ == "__main__":
    main()
