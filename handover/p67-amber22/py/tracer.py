# -*- coding: utf-8 -*-
"""П67 (AMBER22): прямой (нерассеянный) поток от диска к лицевой грани кристалла AS80 — ТОЧНАЯ геометрия
(кабошон, кольцо оправы, бумажная трубка) против того, что умеет сцена `.in` (цилиндр/коробка с одной стенкой).

Считается F(E) = ⟨ exp(−Σ μ_i L_i) · cosθ / r² ⟩ по равномерному источнику и равномерной точке на грани кристалла
(Ø80 на 28.7 мм за крышкой) — величина ∝ доле квантов, дошедших до кристалла без взаимодействия. Отношение
F_точн/F_модель по энергиям — цена приближения сцены; им же подбирается высота эквивалентной коробки ребром и
эквивалентная толщина бумаги.

Оси: z — ось детектора, крышка z = 0, источник при z > 0, кристалл при z < 0. Кабошон: плоское дно к z_bottom,
купол h(r) = 3 + 2(1 − (r/20)²) мм (объём 5.03 см³). Ребром: ось диска вдоль y, центр в (0, 0, z_c).

    python tracer.py [--n=2000000] [--rho=3.3]
"""
import argparse
import math
import sys

import numpy as np

import glass

R_DISK = 20.0       # мм, стекло
R_RING = 21.0       # мм, наружный радиус кольца (сталь 1 мм)
RING_H = 6.0        # мм, высота кольца вдоль оси диска
H_RIM = 3.0         # мм, толщина стекла у кромки (П56: кромка 3 + купол 2; решение распорядителя 14.09.2026 — держать)
H_DOME = 2.0        # мм, купол
H_EQ = 4.08         # мм, эквивалентная равномерная толщина (⟨h²⟩/⟨h⟩)
TUBE_RIN, TUBE_ROUT, TUBE_H = 16.5, 18.5, 81.0
Z_CRYSTAL = -28.7   # мм, лицевая грань кристалла за крышкой
R_CRYSTAL = 40.0
E_LIST = [238.6, 338.3, 583.2, 911.2, 1460.8, 2614.5]


def h_of_r(r):
    return H_RIM + H_DOME * (1.0 - (r / R_DISK) ** 2)


def sample_cabochon(n, rng):
    """точки равномерно по объёму кабошона: (r, φ) по площади с весом h(r), t вдоль оси от плоского дна"""
    out = []
    got = 0
    hmax = H_RIM + H_DOME
    while got < n:
        m = n - got
        r = R_DISK * np.sqrt(rng.random(m))
        keep = rng.random(m) * hmax < h_of_r(r)
        r = r[keep]
        phi = 2 * np.pi * rng.random(r.size)
        t = rng.random(r.size) * h_of_r(r)
        out.append(np.stack([r * np.cos(phi), r * np.sin(phi), t], axis=1))
        got += r.size
    return np.concatenate(out)[:n]


def sample_slab(n, rng, r_max, h):
    r = r_max * np.sqrt(rng.random(n))
    phi = 2 * np.pi * rng.random(n)
    t = rng.random(n) * h
    return np.stack([r * np.cos(phi), r * np.sin(phi), t], axis=1)


def sample_box(n, rng, ax, ay, hz):
    return np.stack([ax * (2 * rng.random(n) - 1), ay * (2 * rng.random(n) - 1), hz * rng.random(n)], axis=1)


def targets(n, rng):
    r = R_CRYSTAL * np.sqrt(rng.random(n))
    phi = 2 * np.pi * rng.random(n)
    return np.stack([r * np.cos(phi), r * np.sin(phi), np.full(n, Z_CRYSTAL)], axis=1)


def cyl_exit(p, d, axis, r_in, r_out, lo, hi):
    """длина пути луча p + s·d (s ≥ 0) внутри кольца/цилиндра r ∈ [r_in, r_out] по осям ≠ axis, координата axis ∈ [lo, hi].
    Точный расчёт: пересечения с двумя цилиндрами и двумя плоскостями, путь = мера множества s, где точка внутри."""
    ax = [i for i in range(3) if i != axis]
    px, py = p[:, ax[0]], p[:, ax[1]]
    dx, dy = d[:, ax[0]], d[:, ax[1]]
    pz, dz = p[:, axis], d[:, axis]
    # интервалы s внутри r ≤ r_out и вне r ≤ r_in, внутри слоя по axis; собираем отрезки
    a = dx * dx + dy * dy
    b = 2 * (px * dx + py * dy)

    def circle(rr):
        c = px * px + py * py - rr * rr
        disc = b * b - 4 * a * c
        ok = disc > 0
        sq = np.sqrt(np.where(ok, disc, 0.0))
        s1 = np.where(ok, (-b - sq) / (2 * a), np.inf)
        s2 = np.where(ok, (-b + sq) / (2 * a), -np.inf)
        return s1, s2   # внутри круга при s1 ≤ s ≤ s2

    o1, o2 = circle(r_out)
    with np.errstate(divide='ignore', invalid='ignore'):
        zl = np.where(dz != 0, (lo - pz) / dz, -np.inf)
        zh = np.where(dz != 0, (hi - pz) / dz, np.inf)
    z1, z2 = np.minimum(zl, zh), np.maximum(zl, zh)
    inside_z = (dz != 0) | ((pz >= lo) & (pz <= hi))
    z1 = np.where(dz == 0, np.where(inside_z, -np.inf, np.inf), z1)
    z2 = np.where(dz == 0, np.where(inside_z, np.inf, -np.inf), z2)
    lo_s = np.maximum(np.maximum(o1, z1), 0.0)
    hi_s = np.minimum(o2, z2)
    total = np.clip(hi_s - lo_s, 0.0, None)
    if r_in > 0:
        i1, i2 = circle(r_in)
        # вычесть пересечение [lo_s, hi_s] ∩ [i1, i2]
        a1 = np.maximum(lo_s, i1)
        a2 = np.minimum(hi_s, i2)
        total = total - np.clip(a2 - a1, 0.0, None)
    return total


def box_exit(p, d, ax, ay, z0, z1):
    """путь внутри коробки |x| ≤ ax, |y| ≤ ay, z ∈ [z0, z1] (точка может быть внутри или снаружи)"""
    lo = np.zeros(p.shape[0])
    hi = np.full(p.shape[0], np.inf)
    for k, (a_lo, a_hi) in enumerate(((-ax, ax), (-ay, ay), (z0, z1))):
        pk, dk = p[:, k], d[:, k]
        with np.errstate(divide='ignore', invalid='ignore'):
            s_lo = np.where(dk != 0, (a_lo - pk) / dk, -np.inf)
            s_hi = np.where(dk != 0, (a_hi - pk) / dk, np.inf)
        s1, s2 = np.minimum(s_lo, s_hi), np.maximum(s_lo, s_hi)
        inside = (pk >= a_lo) & (pk <= a_hi)
        s1 = np.where(dk == 0, np.where(inside, -np.inf, np.inf), s1)
        s2 = np.where(dk == 0, np.where(inside, np.inf, -np.inf), s2)
        lo = np.maximum(lo, s1)
        hi = np.minimum(hi, s2)
    return np.clip(hi - lo, 0.0, None)


def flux(src, tgt, layers, mus):
    """layers: список (путь_мм массив, ключ вещества); mus: {ключ: μ 1/мм}"""
    d = tgt - src
    r2 = np.sum(d * d, axis=1)
    r = np.sqrt(r2)
    cos = -d[:, 2] / r
    att = np.zeros(src.shape[0])
    for path, key in layers:
        att += mus[key] * path
    return np.mean(np.exp(-att) * cos / r2)


def mus_for(E, rho, fr):
    return {'glass': glass.mix_mu_rho(fr, E) * rho / 10.0,
            'steel': glass.mix_mu_rho(glass.STEEL, E) * glass.RHO_STEEL / 10.0,
            'paper': glass.mix_mu_rho(glass.CELLULOSE, E) * glass.RHO_PAPER / 10.0}


def scene_contact_exact(n, rng, z_bottom=0.0, lip=0.0):
    """кабошон плашмя, плоское дно на z_bottom, кольцо стали 1 мм r 20…21 высотой 6 (центр по середине стекла 2.5)"""
    src = sample_cabochon(n, rng)
    src[:, 2] += z_bottom
    tgt = targets(n, rng)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    # стекло: цилиндр r ≤ 20 между z_bottom и z_bottom + h(r) — купол приближаем: путь до выхода вниз/вбок как из
    # плоского цилиндра высотой h(r точки) (лучи идут вниз — купол сверху не задевают)
    hp = h_of_r(np.hypot(src[:, 0], src[:, 1]))
    lg = cyl_exit(src, d, 2, 0.0, R_DISK, z_bottom, z_bottom + hp)
    zc = z_bottom + H_RIM / 2.0
    ls = cyl_exit(src, d, 2, R_DISK, R_RING, zc - RING_H / 2, zc + RING_H / 2)
    layers = [(lg, 'glass'), (ls, 'steel')]
    if lip > 0:
        # губка: кольцо r 19…20 (наружный 21) толщиной lip ниже дна стекла
        ll = cyl_exit(src, d, 2, R_DISK - 1.0, R_RING, z_bottom - lip, z_bottom)
        layers.append((ll, 'steel'))
    return src, tgt, layers


def scene_contact_model(n, rng, z_bottom=0.0, side=1.0, end=0.0, end_key='steel', h=H_EQ):
    """сцена .in: цилиндр Ø40 × h, боковая стенка side (сталь) на высоте пробы, торцевая end под пробой"""
    src = sample_slab(n, rng, R_DISK, h)
    src[:, 2] += z_bottom
    tgt = targets(n, rng)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    lg = cyl_exit(src, d, 2, 0.0, R_DISK, z_bottom, z_bottom + h)
    layers = [(lg, 'glass')]
    if side > 0:
        layers.append((cyl_exit(src, d, 2, R_DISK, R_DISK + side, z_bottom, z_bottom + h), 'steel'))
    if end > 0:
        layers.append((cyl_exit(src, d, 2, 0.0, R_DISK + side, z_bottom - end, z_bottom), end_key))
    return src, tgt, layers


def tube_layer(src, d):
    return cyl_exit(src, d, 2, TUBE_RIN, TUBE_ROUT, 0.0, TUBE_H)


def scene_face81_exact(n, rng, z_bottom=82.0):
    src, tgt, layers = scene_contact_exact(n, rng, z_bottom)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    layers.append((tube_layer(src, d), 'paper'))
    return src, tgt, layers


def scene_edge_exact(n, rng, z_c=93.0, ring_t=1.0):
    """кабошон ребром: ось диска вдоль y; кольцо r 20…20+ring_t по (x,z), |y| ≤ 3; трубка до 81 мм"""
    p = sample_cabochon(n, rng)          # (x, y, t) в осях диска: (x,y) — плоскость диска, t — вдоль оси диска
    # перевод: диск в плоскости (x, z), ось диска — y; t → y − h/2 (симметрично)
    r = np.hypot(p[:, 0], p[:, 1])
    src = np.stack([p[:, 0], p[:, 2] - h_of_r(r) / 2.0, z_c + p[:, 1]], axis=1)
    tgt = targets(n, rng)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    # стекло: цилиндр по оси y, r ≤ 20 в (x, z−z_c), |y| ≤ h(r)/2 (толщину берём по точке — луч почти в плоскости диска)
    hp = h_of_r(r)
    src_c = src.copy()
    src_c[:, 2] -= z_c
    lg = cyl_exit(src_c, d, 1, 0.0, R_DISK, -hp / 2, hp / 2)
    ls = cyl_exit(src_c, d, 1, R_DISK, R_DISK + ring_t, -RING_H / 2, RING_H / 2)
    layers = [(lg, 'glass'), (ls, 'steel'), (tube_layer(src, d), 'paper')]
    return src, tgt, layers


def scene_edge_model(n, rng, z_c=93.0, hz=34.0, ay=H_EQ / 2, end_steel=1.0, end_paper=0.0):
    """сцена .in: коробка |x| ≤ 20, |y| ≤ ay, z ∈ [z_c − hz/2, z_c + hz/2]; торцевая стенка под коробкой:
    сталь end_steel + бумага end_paper (в .in — один слой-смесь той же массовой толщины; здесь два слоя, поток тот же)"""
    src = sample_box(n, rng, R_DISK, ay, hz)
    z0 = z_c - hz / 2
    src[:, 2] += z0
    tgt = targets(n, rng)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    layers = [(box_exit(src, d, R_DISK, ay, z0, z0 + hz), 'glass')]
    z = z0
    if end_steel > 0:
        layers.append((box_exit(src, d, R_DISK, ay, z - end_steel, z), 'steel'))
        z -= end_steel
    if end_paper > 0:
        layers.append((box_exit(src, d, R_DISK, ay, z - end_paper, z), 'paper'))
    return src, tgt, layers


def run(scene_fn, n, rho, fr, seed=1, **kw):
    rng = np.random.default_rng(seed)
    src, tgt, layers = scene_fn(n, rng, **kw)
    out = {}
    for E in E_LIST:
        out[E] = flux(src, tgt, layers, mus_for(E, rho, fr))
    # средние пути (для эквивалентных толщин), взвешенные cosθ/r²
    d = tgt - src
    r2 = np.sum(d * d, axis=1)
    w = (-d[:, 2] / np.sqrt(r2)) / r2
    paths = {key: float(np.sum(path * w) / np.sum(w)) for path, key in layers}
    return out, paths


def show(tag, res, ref=None):
    s = '%-34s' % tag
    for E in E_LIST:
        s += ' %9.3e' % res[E]
    if ref is not None:
        s += ' | отн.: ' + ' '.join('%6.3f' % (res[E] / ref[E]) for E in E_LIST)
        s += ' | 238:2614 %+.2f %%' % (100 * ((res[238.6] / res[2614.5]) / (ref[238.6] / ref[2614.5]) - 1))
    print(s)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--n', type=int, default=1000000)
    ap.add_argument('--rho', type=float, default=3.3)
    a = ap.parse_args()
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    fr = glass.oxide_fractions(glass.composition(a.rho))
    n = a.n
    print('ρ = %.2f, N = %d; столбцы: %s кэВ' % (a.rho, n, ' '.join('%.0f' % E for E in E_LIST)))
    print('\n== КОНТАКТ (плашмя на крышке) ==')
    ex, pex = run(scene_contact_exact, n, a.rho, fr)
    show('точно: кабошон + кольцо', ex)
    for h in (H_EQ, 5.0, 4.0):
        m, pm = run(scene_contact_model, n, a.rho, fr, h=h)
        show('модель: цилиндр h=%.2f + стенка 1' % h, m, ex)
    m, pm = run(scene_contact_model, n, a.rho, fr, side=0.0)
    show('модель: цилиндр h=4.08 без стенки', m, ex)
    exl, _ = run(scene_contact_exact, n, a.rho, fr, lip=1.0)
    show('точно + губка 1 мм к детектору', exl, ex)
    print('\n== ЛИЦОМ НА ТРУБКЕ 81 мм (дно стекла на 82) ==')
    ex, pex = run(scene_face81_exact, n, a.rho, fr)
    show('точно: кабошон + кольцо + трубка', ex)
    print('   средний путь в бумаге (взвеш. потоком): %.2f мм' % pex['paper'])
    m, _ = run(scene_contact_model, n, a.rho, fr, z_bottom=82.0)
    show('модель: цилиндр + стенка 1, без трубки', m, ex)
    for tp in (3.0, 4.0, 5.0, 6.0):
        m, _ = run(scene_contact_model, n, a.rho, fr, z_bottom=82.0, side=0.0, end=tp, end_key='paper')
        show('модель: цилиндр, торец бумага %.1f, без стенки' % tp, m, ex)
    print('\n== РЕБРОМ, центр 93 мм ==')
    ex, pex = run(scene_edge_exact, n, a.rho, fr)
    show('точно: кабошон ребром + кольцо + трубка', ex)
    print('   средний путь: стекло %.2f, сталь %.3f, бумага %.2f мм' % (pex['glass'], pex['steel'], pex['paper']))
    ex0, _ = run(scene_edge_exact, n, a.rho, fr, ring_t=0.0)
    show('точно без кольца', ex0, ex)
    for hz in (40.0, 36.0, 34.0, 33.0, 32.0):
        m, pm = run(scene_edge_model, n, a.rho, fr, hz=hz, end_steel=1.0, end_paper=0.0)
        show('модель: коробка H=%.0f, торец сталь 1.0' % hz, m, ex)
    for es, ep in ((1.27, 0.0), (1.27, 2.0), (1.27, 3.0), (1.27, 4.0), (1.0, 3.0)):
        m, pm = run(scene_edge_model, n, a.rho, fr, hz=34.0, end_steel=es, end_paper=ep)
        show('модель: H=34, сталь %.2f, бумага %.1f' % (es, ep), m, ex)


if __name__ == '__main__':
    main()


def scene_edge_model2(n, rng, z_c=93.0, hz=40.0, ay=H_EQ / 2, t_fe=1.7, t_p=13.0, w=0.0):
    """сцена .in: коробка + ОДИН материал стенок — смесь (сталь t_fe + бумага t_p по массовой толщине) торцом
    толщиной t_fe + t_p и боковой стенкой w той же смеси (на всех четырёх гранях, на высоте пробы)"""
    src = sample_box(n, rng, R_DISK, ay, hz)
    z0 = z_c - hz / 2
    src[:, 2] += z0
    tgt = targets(n, rng)
    d = tgt - src
    d /= np.linalg.norm(d, axis=1)[:, None]
    t_end = t_fe + t_p
    f_fe = t_fe / t_end
    f_p = t_p / t_end
    layers = [(box_exit(src, d, R_DISK, ay, z0, z0 + hz), 'glass')]
    le = box_exit(src, d, R_DISK + w, ay + w, z0 - t_end, z0)
    layers.append((le * f_fe, 'steel'))
    layers.append((le * f_p, 'paper'))
    if w > 0:
        outer = box_exit(src, d, R_DISK + w, ay + w, z0, z0 + hz)
        inner = box_exit(src, d, R_DISK, ay, z0, z0 + hz)
        ls = np.clip(outer - inner, 0.0, None)
        layers.append((ls * f_fe, 'steel'))
        layers.append((ls * f_p, 'paper'))
    return src, tgt, layers
