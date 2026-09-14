"""Сцена «детектор в лунке»: какого размера маринелли даёт 98 % сигнала.

Зачем. Прибор, опущенный в лунку, подменять НЕ нужно — это в точности стакан
Маринелли: колодец есть (сама лунка), проба вокруг и снизу (грунт), стенок
сосуда нет. Остаётся назвать размеры: докуда считать грунт вокруг и под лункой,
чтобы сцена держала 98 % сигнала, и глубже какой глубины копать бесполезно.

Что считается — то же, что в `ground_halfspace.py`: нерассеянный поток 662 кэВ
в точке, где стоит кристалл, от равномерно активного грунта. Рассеявшийся квант
в пик не попадает, поэтому для площади фотопика это и есть ответ.

Геометрия: грунт занимает z > 0 (z вниз от поверхности) всюду, кроме лунки
{r < a, 0 < z < H}. Кристалл на оси на глубине z_d. Счёт ведётся по НАПРАВЛЕНИЯМ
из точки кристалла — так задача становится одномерной:

    Phi = 1/2 * int_0^pi sin(th) * G(th) d(th),
    G(th) = exp(-mu_a * rho0) * (1 - exp(-mu_s * (rho1 - rho0))) / mu_s,

где rho0 — где луч выходит из лунки в грунт, rho1 — где он выходит из грунта
(наружу сцены или в открытый воздух над поверхностью). Луч, ушедший вверх через
устье лунки, не даёт ничего: он выходит в воздух и в грунт больше не вернётся.

Выпуклость лунки (в осевом сечении это прямоугольник) гарантирует, что луч
пересекает её границу один раз, — поэтому «воздух, потом грунт» и есть полное
описание пути.

Запуск (числа печатаются, ничего не пишется):

    python tools/effmaker/borehole.py
    python tools/effmaker/borehole.py --hole-diameter=20 --energy=1461

E27. Смысл чисел и оговорки — в `tools/effmaker/README.md`.
"""

import argparse
import math

import numpy as np
from scipy.optimize import brentq

from ground_halfspace import MU_AIR, MU_SOIL, _interp

# Узлов по углу. Подынтегральная функция гладкая, но у самого устья лунки есть
# излом (луч перестаёт находить грунт), и сетка должна его разрешать.
NODES = 200001


def flux(mu_s, mu_a, hole_radius, hole_depth, detector_depth,
         outer_radius=math.inf, bottom=math.inf):
    """Поток от грунта, ограниченного радиусом `outer_radius` и глубиной `bottom`.

    Оба ограничения меряются от ОСИ и от ПОВЕРХНОСТИ соответственно — так же,
    как размеры стакана в модели геометрии.
    """
    a = hole_radius
    H = hole_depth
    zd = detector_depth

    th = np.linspace(0.0, math.pi, NODES)
    sin = np.sin(th)
    cos = np.cos(th)                      # +1 — вниз, -1 — вверх

    big = 1e12
    # Выход из лунки: вбок через стенку, вниз через дно, вверх через устье.
    with np.errstate(divide="ignore", invalid="ignore"):
        side = np.where(sin > 1e-12, a / np.maximum(sin, 1e-12), big)
        down = np.where(cos > 1e-12, (H - zd) / np.maximum(cos, 1e-12), big)
        up = np.where(cos < -1e-12, zd / np.maximum(-cos, 1e-12), big)

    rho0 = np.minimum(side, down)
    escapes = up < rho0                   # луч ушёл в устье — грунта на нём нет

    # Выход из грунта: через поверхность (только вверх) или за границу сцены.
    with np.errstate(divide="ignore", invalid="ignore"):
        surface = np.where(cos < -1e-12, zd / np.maximum(-cos, 1e-12), big)
        rlimit = (np.where(sin > 1e-12, outer_radius / np.maximum(sin, 1e-12), big)
                  if math.isfinite(outer_radius) else np.full_like(th, big))
        zlimit = (np.where(cos > 1e-12, (bottom - zd) / np.maximum(cos, 1e-12), big)
                  if math.isfinite(bottom) else np.full_like(th, big))

    rho1 = np.minimum(np.minimum(surface, rlimit), zlimit)
    span = np.maximum(0.0, rho1 - rho0)

    g = np.exp(-mu_a * rho0) * (1.0 - np.exp(-mu_s * span)) / mu_s
    g[escapes] = 0.0
    return 0.5 * np.trapezoid(sin * g, th)


def surface_flux(mu_s, mu_a, height):
    """То же для прибора, лежащего НА грунте: лунка нулевая, кристалл над землёй."""
    return flux(mu_s, mu_a, 0.0, 0.0, -height)


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--energy", type=float, default=661.7, help="энергия, кэВ")
    p.add_argument("--density", type=float, default=1.5, help="плотность грунта, г/см3")
    p.add_argument("--hole-diameter", type=float, default=4.0, help="диаметр лунки, см")
    p.add_argument("--hole-depth", type=float, default=40.0, help="глубина лунки, см")
    p.add_argument("--clearance", type=float, default=3.0,
                   help="кристалл над дном лунки, см")
    p.add_argument("--fraction", type=float, default=0.98, help="требуемая доля потока")
    args = p.parse_args()

    mu_s = _interp(MU_SOIL, args.energy) * args.density
    mu_a = _interp(MU_AIR, args.energy) * 0.001205
    lam = 1.0 / mu_s
    a = 0.5 * args.hole_diameter
    zd = args.hole_depth - args.clearance

    full = flux(mu_s, mu_a, a, args.hole_depth, zd)
    print("энергия %.1f кэВ, грунт %.2f г/см3, свободный пробег %.2f см"
          % (args.energy, args.density, lam))
    print("лунка Ø%.0f см, глубина %.0f см, кристалл в %.0f см над дном"
          % (args.hole_diameter, args.hole_depth, args.clearance))

    radius = brentq(lambda r: flux(mu_s, mu_a, a, args.hole_depth, zd, outer_radius=r) / full
                    - args.fraction, a + 0.01, 200.0 * lam)
    bottom = brentq(lambda b: flux(mu_s, mu_a, a, args.hole_depth, zd, bottom=b) / full
                    - args.fraction, zd + 0.01, 200.0 * lam)
    joint = flux(mu_s, mu_a, a, args.hole_depth, zd, outer_radius=radius, bottom=bottom) / full
    print("радиус сцены от оси   %6.1f см = %5.2f пробега (%.2f пробега от стенки лунки)"
          % (radius, radius / lam, (radius - a) / lam))
    print("дно сцены от поверхн. %6.1f см = %5.2f пробега (%.2f пробега под кристаллом)"
          % (bottom, bottom / lam, (bottom - zd) / lam))
    print("совместная доля при этих двух размерах: %.3f" % joint)

    print("\nвыигрыш лунки против прибора на грунте (кристалл в %.0f см над землёй):"
          % args.clearance)
    surface = surface_flux(mu_s, mu_a, args.clearance)
    for depth in (5.0, 10.0, 20.0, 30.0, 40.0, 60.0, 100.0):
        d = max(0.0, depth - args.clearance)
        print("  лунка %5.0f см: %.3f — %.2f× к поверхности"
              % (depth, flux(mu_s, mu_a, a, depth, d) / full,
                 flux(mu_s, mu_a, a, depth, d) / surface))

    print("\nцена ширины лунки (та же глубина):")
    base = None
    for diameter in (4.0, 8.0, 12.0, 20.0, 30.0):
        value = flux(mu_s, mu_a, 0.5 * diameter, args.hole_depth, zd)
        base = value if base is None else base
        print("  Ø%4.0f см: %.3f от Ø%.0f" % (diameter, value / base, 4.0))

    print("\nустойчивость доли пробега к энергии:")
    for e in (59.5, 186.0, 351.9, 661.7, 1173.2, 1460.8, 2614.5):
        ms = _interp(MU_SOIL, e) * args.density
        ma = _interp(MU_AIR, e) * 0.001205
        f = flux(ms, ma, a, args.hole_depth, zd)
        r = brentq(lambda x: flux(ms, ma, a, args.hole_depth, zd, outer_radius=x) / f
                   - args.fraction, a + 0.01, 400.0 / ms)
        b = brentq(lambda x: flux(ms, ma, a, args.hole_depth, zd, bottom=x) / f
                   - args.fraction, zd + 0.01, 400.0 / ms)
        print("  %7.1f кэВ: пробег %5.2f см, радиус %5.2f пробега, под кристаллом %5.2f пробега"
              % (e, 1.0 / ms, r * ms, (b - zd) * ms))


if __name__ == "__main__":
    main()
