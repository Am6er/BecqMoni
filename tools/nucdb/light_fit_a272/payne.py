# -*- coding: utf-8 -*-
# (`AMBER272`, полоса `dbdata2`, 02.11.2026) Оснастка подгонки кривой света GAGG — журнал
# `handover/handover-2026-11-02-amber274-dbdata2.md` §4. Рабочий каталог (выводы проб, якобианы) — переменная
# окружения A272_WORK; сцены GAGG — A272_WORK/scenes; данные Kole — tools/nucdb/data/photon_light/.
# Модель света Пейна — те же числа, что MaterialDatabase.LoadLightYieldPayne (ecomp, продолжение ниже 1 кэВ,
# обрыв короткого трека). Входы — вывод пробы LightModelDumpA272c (тормозная ESTAR по составу, ρ, I).
import os as _os
WORK = _os.environ.get('A272_WORK', '.').replace('\\', '/').rstrip('/') + '/'
HERE = _os.path.dirname(_os.path.abspath(__file__))
TREE = _os.path.dirname(_os.path.dirname(_os.path.dirname(HERE)))
import io
import math

DUMP = WORK + 'gagg_model_dump.txt'   # вывод пробы LightModelDumpA272c


def load_dump(path=DUMP):
    d = {'S': [], 'Y': [], 'T': []}
    for line in io.open(path, encoding='utf-8'):
        p = line.split()
        if not p:
            continue
        if p[0] in ('S', 'Y', 'T'):
            d[p[0]].append((float(p[1]), float(p[2])))
        elif p[0] in ('density', 'potential_ev', 'track_end_kev', 'track_end_power'):
            d[p[0]] = float(p[1])
    return d


def loglog(xs, ys, x):
    n = len(xs)
    if x <= xs[0]:
        return ys[0]
    if x >= xs[n - 1]:
        return ys[n - 1]
    lo, hi = 0, n - 1
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if xs[mid] <= x:
            lo = mid
        else:
            hi = mid
    t = (math.log(x) - math.log(xs[lo])) / (math.log(xs[hi]) - math.log(xs[lo]))
    return math.exp(math.log(ys[lo]) + t * (math.log(ys[hi]) - math.log(ys[lo])))


class Stopping(object):
    """Тормозная способность как у кода: ESTAR по составу от 1 кэВ, ниже — Joy — Luo, сшитая на 1 кэВ."""

    def __init__(self, d, sub_kev=True):
        self.e = [x for x, _ in d['S']]
        self.s = [y for _, y in d['S']]
        self.j = d['potential_ev']
        self.sub = sub_kev
        j = self.j
        jl = lambda ev: math.log(1.166 * (ev + 0.85 * j) / j) / ev
        self.jl = jl
        peak_ev, peak = 1000.0, jl(1000.0)
        ev = 1000.0
        while ev > 5.0:
            v = jl(ev)
            if v > peak:
                peak, peak_ev = v, ev
            ev /= 1.01
        self.peak_ev = peak_ev
        self.s1 = self.s[0]
        self.jl1 = jl(self.e[0] * 1000.0)

    def __call__(self, kev):
        if kev >= self.e[0] or not self.sub:
            return loglog(self.e, self.s, kev)
        ev = max(kev * 1000.0, self.peak_ev)
        return self.s1 * self.jl(ev) / self.jl1


def curve(d, eta, ons, trap, birks, sub_kev=True, track_end=None, power=None, local=None, light_fn=None):
    """Узлы (E, y) кривой электронов. local(s) — своя местная форма (по умолчанию Пейн);
    light_fn — готовая функция L(E) (интеграл) вместо трапеций, для иных моделей гашения."""
    st = Stopping(d, sub_kev)
    if track_end is None:
        track_end = d['track_end_kev'] if sub_kev else 0.0
    if power is None:
        power = d['track_end_power'] if sub_kev else 2.0
    if local is None:
        def local(s):
            inner = (s / ons) * math.exp(-trap / s)
            return (1.0 - eta * math.exp(-inner)) / (1.0 + s / birks)
    e_lo = 0.01 if sub_kev else 1.0
    e_hi = 3000.0
    if light_fn is None:
        steps = int(500 * math.log10(e_hi / e_lo))
        grid = [e_lo * math.pow(e_hi / e_lo, i / float(steps)) for i in range(steps + 1)]
        light = [0.0] * (steps + 1)
        light[0] = local(st(e_lo)) * e_lo
        for i in range(1, steps + 1):
            la = local(st(grid[i - 1]))
            lb = local(st(grid[i]))
            light[i] = light[i - 1] + 0.5 * (la + lb) * (grid[i] - grid[i - 1])
        lf = lambda e: loglog(grid, light, e)
    else:
        lf = light_fn
    norm = lf(661.657) / 661.657
    n = int(20 * math.log10(e_hi / e_lo)) + 1
    out = []
    for i in range(n):
        e = e_lo * math.pow(e_hi / e_lo, i / float(n - 1))
        y = lf(e) / e / norm
        if track_end > 0.0:
            y /= 1.0 + ((track_end / e) * (track_end / e) if power == 2.0 else math.pow(track_end / e, power))
        out.append((e, y))
    return out
