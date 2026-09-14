# -*- coding: utf-8 -*-
"""П67: гипотеза «торий в ПОВЕРХНОСТНОМ слое» против «торий по объёму» — прямой поток контакт / ребром.

Без связки ряда (`--no-equilibrium`) отношение амплитуд Pb-212 (238) / Tl-208 (2614 + 583) при одном составе ρ 3.3:
контакт 1.016, ребром 1.171 — модель однородного стекла поглощает 238 ребром на ~15 % сильнее, чем данные, при
верном контакте. Здесь: трассировкой точной геометрии (кабошон 3+2, кольцо 1 мм, трубка) считается F(238)/F(2614)
для источника, распределённого (а) равномерно по объёму, (б) в слое толщиной L от поверхности (обе грани + кромка),
при ρ 2.8 / 3.3 / 3.8; печатается «ребром / контакт» двойное отношение — что оно даёт против наблюдённого сдвига.
"""
import sys

import numpy as np

sys.path.insert(0, r'D:\BqMoni_Claude\p67\py')
import glass   # noqa: E402
import tracer  # noqa: E402


def sample_layer(n, rng, L):
    """точки кабошона в слое L мм от поверхности (грани и кромка), равномерно по объёму слоя"""
    out = []
    got = 0
    while got < n:
        p = tracer.sample_cabochon(4 * n, rng)
        r = np.hypot(p[:, 0], p[:, 1])
        h = tracer.h_of_r(r)
        keep = (p[:, 2] < L) | (p[:, 2] > h - L) | (r > tracer.R_DISK - L)
        out.append(p[keep])
        got += int(keep.sum())
    return np.concatenate(out)[:n]


def run(n, rho, fr, L=None, seed=1):
    rng = np.random.default_rng(seed)
    res = {}
    # контакт
    src = tracer.sample_cabochon(n, rng) if L is None else sample_layer(n, rng, L)
    tgt = tracer.targets(n, rng)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    hp = tracer.h_of_r(np.hypot(src[:, 0], src[:, 1]))
    lg = tracer.cyl_exit(src, d, 2, 0.0, tracer.R_DISK, 0.0, hp)
    ls = tracer.cyl_exit(src, d, 2, tracer.R_DISK, tracer.R_RING, 2.5 - 3.0, 2.5 + 3.0)
    layers = [(lg, 'glass'), (ls, 'steel')]
    res['contact'] = {E: tracer.flux(src, tgt, layers, tracer.mus_for(E, rho, fr)) for E in (238.6, 583.2, 2614.5)}
    # ребром
    p = tracer.sample_cabochon(n, rng) if L is None else sample_layer(n, rng, L)
    r = np.hypot(p[:, 0], p[:, 1])
    src = np.stack([p[:, 0], p[:, 2] - tracer.h_of_r(r) / 2.0, 93.0 + p[:, 1]], axis=1)
    tgt = tracer.targets(n, rng)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    hp = tracer.h_of_r(r)
    src_c = src.copy()
    src_c[:, 2] -= 93.0
    lg = tracer.cyl_exit(src_c, d, 1, 0.0, tracer.R_DISK, -hp / 2, hp / 2)
    ls = tracer.cyl_exit(src_c, d, 1, tracer.R_DISK, tracer.R_DISK + 1.0, -3.0, 3.0)
    layers = [(lg, 'glass'), (ls, 'steel'), (tracer.tube_layer(src, d), 'paper')]
    res['edge'] = {E: tracer.flux(src, tgt, layers, tracer.mus_for(E, rho, fr)) for E in (238.6, 583.2, 2614.5)}
    return res


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    n = 400000
    print('%-8s %-10s | %9s %9s | %9s %9s | %9s %9s' % ('ρ', 'источник', 'K238/2614', 'R238/2614', 'R/K', 'K583/2614', 'R583/2614', 'R/K 583'))
    base = None
    for rho in (2.8, 3.3, 3.8):
        fr = glass.oxide_fractions(glass.composition(rho))
        for L, tag in ((None, 'объём'), (0.5, 'слой 0.5'), (0.2, 'слой 0.2'), (1.0, 'слой 1.0')):
            res = run(n, rho, fr, L)
            kc = res['contact'][238.6] / res['contact'][2614.5]
            ke = res['edge'][238.6] / res['edge'][2614.5]
            kc5 = res['contact'][583.2] / res['contact'][2614.5]
            ke5 = res['edge'][583.2] / res['edge'][2614.5]
            print('%-8.1f %-10s | %9.4f %9.4f | %9.4f %9.4f | %9.4f %9.4f' % (rho, tag, kc, ke, ke / kc, kc5, ke5, ke5 / kc5))
    print('наблюдено (FSA без связки, Pb-212/Tl-208): контакт 1.016 (ρ 3.3), ребром 1.171 → ребром/контакт по 238 : 2614 у данных выше модели на +15 % (при ρ 3.3), +9 % (2.8), +22 % (3.8)')


if __name__ == '__main__':
    main()
