# -*- coding: utf-8 -*-
r"""П88 (AMBER22): что меняет ПОСТАНОВКА диска ребром — расстояние центра и наклон плоскости диска к оси детектора — в
прямом потоке к кристаллу, для источника по объёму и в поверхностном слое 0.25 мм (трассировка П67 tracer.py, точная
геометрия: кабошон 3+2, кольцо 1 мм, трубка). Печатает F(238)/F(583)/F(2614) относительно номинала (центр 93 мм, без
наклона) и сдвиг отношения 238:2614 — сравнивать с наблюдённым между съёмками 14.09 и 17.09: все линии +7…+16 %,
(238/2614)_17.09/(238/2614)_14.09 = 1.086 ± 0.019.
Наклон θ — поворот плоскости диска вокруг оси x (горизонтальной, поперёк оси детектора): одна грань подаётся к детектору.

    python tilt.py [--n=400000] [--rho=3.3]
"""
import argparse
import sys

import numpy as np

sys.path.insert(0, r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p67-amber22\py')
import glass    # noqa: E402
import tracer   # noqa: E402
import layer    # noqa: E402

E3 = (238.6, 583.2, 2614.5)


def scene_edge_tilt(n, rng, z_c=93.0, theta_deg=0.0, L=None, ring_t=1.0, dx=0.0):
    """кабошон ребром: ось диска вдоль y (лаб), плоскость диска (x, z); наклон θ — поворот вокруг лаб. x; dx — сдвиг центра по x"""
    p = tracer.sample_cabochon(n, rng) if L is None else layer.sample_layer(n, rng, L)
    r = np.hypot(p[:, 0], p[:, 1])
    hp = tracer.h_of_r(r)
    # система диска: (x, a, s): a — вдоль оси диска, s — в плоскости диска (вертикаль к детектору)
    src_d = np.stack([p[:, 0], p[:, 2] - hp / 2.0, p[:, 1]], axis=1)
    th = np.deg2rad(theta_deg)
    c, s_ = np.cos(th), np.sin(th)
    # лаб: поворот вокруг x: (a, s) → (a·c − s·s_, a·s_ + s·c), затем сдвиг центра
    src = np.stack([src_d[:, 0] + dx, src_d[:, 1] * c - src_d[:, 2] * s_, z_c + src_d[:, 1] * s_ + src_d[:, 2] * c], axis=1)
    tgt = tracer.targets(n, rng)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    # направление в систему диска (обратный поворот)
    d_d = np.stack([d[:, 0], d[:, 1] * c + d[:, 2] * s_, -d[:, 1] * s_ + d[:, 2] * c], axis=1)
    lg = tracer.cyl_exit(src_d, d_d, 1, 0.0, tracer.R_DISK, -hp / 2, hp / 2)
    ls = tracer.cyl_exit(src_d, d_d, 1, tracer.R_DISK, tracer.R_DISK + ring_t, -tracer.RING_H / 2, tracer.RING_H / 2)
    layers = [(lg, 'glass'), (ls, 'steel'), (tracer.tube_layer(src, d), 'paper')]
    return src, tgt, layers


def run(n, rho, fr, seed=1, **kw):
    rng = np.random.default_rng(seed)
    src, tgt, layers = scene_edge_tilt(n, rng, **kw)
    return {E: tracer.flux(src, tgt, layers, tracer.mus_for(E, rho, fr)) for E in E3}


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('--n', type=int, default=400000)
    ap.add_argument('--rho', type=float, default=3.3)
    a = ap.parse_args()
    fr = glass.oxide_fractions(glass.composition(a.rho))
    print('ρ %.1f, %d историй; F относительно номинала (центр 93 мм, θ = 0, тот же источник); последний столбец — сдвиг 238:2614' % (a.rho, a.n))
    print('%-10s %-26s | %7s %7s %7s | %8s' % ('источник', 'постановка', 'F238', 'F583', 'F2614', '238:2614'))
    for L, tag in ((None, 'объём'), (0.25, 'слой 0.25')):
        base = run(a.n, a.rho, fr, L=L)
        for z_c, th, dx, label in ((93.0, 0.0, 0.0, 'номинал 93 мм'), (90.0, 0.0, 0.0, 'центр 90 мм'), (88.0, 0.0, 0.0, 'центр 88 мм'),
                                   (93.0, 10.0, 0.0, 'наклон 10°'), (93.0, 20.0, 0.0, 'наклон 20°'), (93.0, 30.0, 0.0, 'наклон 30°'),
                                   (90.0, 20.0, 0.0, 'центр 90 + наклон 20°'), (93.0, 0.0, 10.0, 'сдвиг по x 10 мм'), (93.0, 0.0, 20.0, 'сдвиг по x 20 мм')):
            res = run(a.n, a.rho, fr, L=L, z_c=z_c, theta_deg=th, dx=dx)
            rel = [res[E] / base[E] for E in E3]
            print('%-10s %-26s | %7.3f %7.3f %7.3f | %+7.1f %%' % (tag, label, rel[0], rel[1], rel[2], 100 * (rel[0] / rel[2] - 1)))
    print('наблюдено 17.09 / 14.09 (данные, окно ±1.2 ПШПВ, фон вычтен): 238 1.160 ± 0.010, 583 1.130 ± 0.016, 911 1.078 ± 0.012, 2614 1.068 ± 0.016; 238:2614 +8.6 ± 1.9 %')


if __name__ == '__main__':
    main()
