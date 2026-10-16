"""FWHM выделения: прямая между краями против ступенчатой базы (Gunnink/PANDA 5.3.4).

Пик: гаусс sigma (каналы) амплитуды A над подложкой R + H * 0.5*erfc((x-x0)/(sigma*sqrt2)) + slope*(x-x0).
Окно выделения: x0 +- k*FWHM. Считаем:
  line1  — нынешний алгоритм приложения (база — прямая по ОДНОМУ каналу на каждом краю);
  lineN  — та же прямая, но края — среднее по n каналам (как в N42.14 / GammaVision);
  step   — ступенька: base(x) = L + (R - L) * S(x)/S_tot, S — накопленный чистый счёт (итерации),
           L, R — средние по n каналам у краёв.
Полувысота — как в EnergyResolutionCalculator: чистый отсчёт против половины чистой вершины,
пересечение линейной интерполяцией.
"""
import math
import random
import statistics


def erfc(x):
    return math.erfc(x)


def make(n_ch, x0, sigma, A, R, H, slope, rng, noise):
    y = []
    for i in range(n_ch):
        x = i - x0
        mu = A * math.exp(-0.5 * (x / sigma) ** 2) + R + H * 0.5 * erfc(x / (sigma * math.sqrt(2))) + slope * x
        mu = max(mu, 0.0)
        y.append(float(poisson(mu, rng)) if noise else mu)
    return y


def poisson(mu, rng):
    if mu > 50:
        return max(0, int(round(rng.gauss(mu, math.sqrt(mu)))))
    L = math.exp(-mu); k = 0; p = 1.0
    while True:
        p *= rng.random()
        if p <= L:
            return k
        k += 1


def fwhm_with_base(y, a, b, base):
    net = [y[i] - base[i - a] for i in range(a, b + 1)]
    c = max(range(len(net)), key=lambda j: net[j])
    half = net[c] / 2.0
    if not half > 0:
        return None
    left = None
    for j in range(1, c + 1):
        if net[j] > half:
            if not net[j] > net[j - 1]:
                return None
            left = (j - 1) + (half - net[j - 1]) / (net[j] - net[j - 1])
            break
    right = None
    for k in range(len(net) - 2, c - 1, -1):
        if net[k] > half:
            if not net[k] > net[k + 1]:
                return None
            right = (k + 1) - (half - net[k + 1]) / (net[k] - net[k + 1])
            break
    if left is None or right is None:
        return None
    return right - left, 0.5 * (left + right) + a, sum(max(v, 0) for v in net)


def line_base(y, a, b, n):
    yl = statistics.fmean(y[a - n + 1:a + 1]) if n > 1 else y[a]
    yr = statistics.fmean(y[b:b + n]) if n > 1 else y[b]
    return [yl + (yr - yl) * (i - a) / (b - a) for i in range(a, b + 1)]


def step_base(y, a, b, n, iters=6):
    yl = statistics.fmean(y[a - n + 1:a + 1]) if n > 1 else y[a]
    yr = statistics.fmean(y[b:b + n]) if n > 1 else y[b]
    base = [yl + (yr - yl) * (i - a) / (b - a) for i in range(a, b + 1)]
    for _ in range(iters):
        net = [max(y[i] - base[i - a], 0.0) for i in range(a, b + 1)]
        tot = sum(net)
        if not tot > 0:
            break
        s = 0.0
        nb = []
        for j, v in enumerate(net):
            s += v
            frac = (s - 0.5 * v) / tot
            nb.append(yl + (yr - yl) * frac)
        base = nb
    return base


def run(sigma, A, R, H, slope, k, n_edge, noise, trials, seed=1):
    rng = random.Random(seed)
    true_fwhm = 2.0 * math.sqrt(2 * math.log(2)) * sigma
    w = int(round(k * true_fwhm))
    n_ch = 2 * w + 4 * n_edge + 40
    x0 = n_ch / 2.0
    a = int(round(x0)) - w
    b = int(round(x0)) + w
    out = {"line1": [], "lineN": [], "step": []}
    for t in range(trials):
        y = make(n_ch, x0, sigma, A, R, H, slope, rng, noise)
        for name, base in (("line1", line_base(y, a, b, 1)),
                           ("lineN", line_base(y, a, b, n_edge)),
                           ("step", step_base(y, a, b, n_edge))):
            r = fwhm_with_base(y, a, b, base)
            if r:
                out[name].append(r)
    res = {}
    for name, rs in out.items():
        if not rs:
            res[name] = None
            continue
        f = [r[0] for r in rs]
        c = [r[1] - x0 for r in rs]
        res[name] = (statistics.fmean(f) / true_fwhm - 1.0,
                     (statistics.pstdev(f) / true_fwhm) if len(f) > 1 else 0.0,
                     statistics.fmean(c) / true_fwhm, len(rs))
    return true_fwhm, res


def fmt(r):
    if r is None:
        return "     отказ      "
    bias, sd, cshift, n = r
    return "{:+6.2f}% ±{:5.2f}% c{:+5.2f}".format(100 * bias, 100 * sd, cshift)


if __name__ == "__main__":
    import sys
    sys.stdout.reconfigure(encoding="utf-8")
    print("смещение FWHM, % (среднее) ± разброс одного выделения, сдвиг середины полувысот в долях FWHM")
    print("sigma=8 кан. (FWHM 18.8), окно ±k·FWHM, края n=5 каналов для lineN/step")
    print()
    print("== без шума: только систематика ступеньки ==")
    print("{:>6} {:>4} | {:^22} | {:^22} | {:^22}".format("H/A", "k", "line1", "lineN", "step"))
    for H in (0.0, 0.05, 0.1, 0.2, 0.5, 1.0):
        for k in (1.0, 1.5, 2.5):
            _, r = run(8.0, 1000.0, 200.0, H * 1000.0, 0.0, k, 5, False, 1)
            print("{:>6.2f} {:>4.1f} | {} | {} | {}".format(H, k, fmt(r["line1"]), fmt(r["lineN"]), fmt(r["step"])))
    print()
    print("== с шумом Пуассона, 400 выделений: вершина A над подложкой ==")
    print("{:>6} {:>6} {:>4} | {:^22} | {:^22} | {:^22}".format("A", "H/A", "k", "line1", "lineN", "step"))
    for A, R in ((10000.0, 2000.0), (1000.0, 200.0), (100.0, 20.0)):
        for H in (0.0, 0.1, 0.3):
            for k in (1.5,):
                _, r = run(8.0, A, R, H * A, 0.0, k, 5, True, 400)
                print("{:>6.0f} {:>6.2f} {:>4.1f} | {} | {} | {}".format(A, H, k, fmt(r["line1"]), fmt(r["lineN"]), fmt(r["step"])))
