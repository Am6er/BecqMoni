import math, os, random, statistics, sys
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fwhm_step_sim import make, line_base

def solve3(xs, ys):
    S = [[0.0]*3 for _ in range(3)]; v = [0.0]*3
    for t, yy in zip(xs, ys):
        row = [1.0, t, t*t]
        for i in range(3):
            v[i] += row[i]*yy
            for j in range(3): S[i][j] += row[i]*row[j]
    M = [r[:] for r in S]; r = v[:]
    for i in range(3):
        p = M[i][i]
        if abs(p) < 1e-300: return None
        for j in range(3): M[i][j] /= p
        r[i] /= p
        for k in range(3):
            if k != i:
                f = M[k][i]
                for j in range(3): M[k][j] -= f*M[i][j]
                r[k] -= f*r[i]
    return r

def crossings(net, c, half):
    left = right = None
    for j in range(1, c + 1):
        if net[j] > half:
            left = (j - 1) + (half - net[j-1]) / (net[j] - net[j-1]); break
    for k in range(len(net) - 2, c - 1, -1):
        if net[k] > half:
            right = (k + 1) - (half - net[k+1]) / (net[k] - net[k+1]); break
    return left, right

def fwhm(y, a, b, mode, frac):
    base = line_base(y, a, b, 1)
    net = [y[i] - base[i - a] for i in range(a, b + 1)]
    c = max(range(len(net)), key=lambda j: net[j])
    top = net[c]
    l, r = crossings(net, c, top / 2)
    if l is None or r is None: return None
    if mode == 'max': return r - l
    w0 = r - l
    h = max(1, int(round(frac * w0)))
    lo, hi = c - h, c + h
    if lo < 0 or hi >= len(net): return r - l
    xs = list(range(-h, h + 1)); ys = [net[c + t] for t in xs]
    if mode == 'lin':
        p = solve3(xs, ys)
        if p and p[2] < 0:
            tv = -p[1] / (2 * p[2])
            if abs(tv) <= h: top = p[0] - p[1]*p[1]/(4*p[2])
    elif mode == 'log':
        if min(ys) > 0:
            p = solve3(xs, [math.log(v) for v in ys])
            if p and p[2] < 0:
                tv = -p[1] / (2 * p[2])
                if abs(tv) <= h: top = math.exp(p[0] - p[1]*p[1]/(4*p[2]))
    l, r = crossings(net, c, top / 2)
    if l is None or r is None: return None
    return r - l

rng = random.Random(11)
modes = [('max', 0), ('lin', 0.35), ('lin', 0.25), ('log', 0.35), ('log', 0.5)]
print('окно ±1.5 FWHM, ступенька 4 % высоты, 1500 выделений, центр пика дробный случайный')
print('смещение среднего FWHM, %  (разброс одного выделения, %)')
hdr = '{:>5} {:>6} | ' + ' | '.join(['{:^15}'] * len(modes))
print(hdr.format('sigma', 'A', *['{} {}'.format(m, f) for m, f in modes]))
for sigma in (1.3, 3.0, 8.0):
    tf = 2*math.sqrt(2*math.log(2))*sigma
    for A in (100000, 10000, 1000, 300, 100, 30):
        R = 0.2 * A
        out = {m: [] for m in modes}
        for t in range(1500):
            w = int(round(1.5*tf)); n = 2*w + 60
            x0 = n/2.0 + rng.random() - 0.5
            a = int(round(n/2.0)) - w; b = int(round(n/2.0)) + w
            y = make(n, x0, sigma, A, R, 0.04*A, 0.0, rng, True)
            for m in modes:
                f = fwhm(y, a, b, m[0], m[1])
                if f: out[m].append(f)
        cells = []
        for m in modes:
            f = out[m]
            cells.append('{:+6.2f} ({:5.2f})'.format(100*(statistics.fmean(f)/tf-1), 100*statistics.pstdev(f)/tf))
        print(hdr.format(sigma, A, *cells))
