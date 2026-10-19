import sys, json, re
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
G = 'D:/BqMoni_Claude/p338/g4/'
def csv(fn):
    hdr = {}; c = []
    for line in open(G + fn, encoding='utf-8'):
        if line.startswith('#'):
            k, _, v = line[1:].partition(':'); hdr.setdefault(k.strip(), []).append(v.strip()); continue
        if line.startswith('channel'): continue
        c.append(float(line.split(',')[1]))
    return hdr, np.array(c)
def coeffs(s): return [float(x) for x in re.search(r'coeffs:\s*([-0-9.eE+ ]+)', s).group(1).split()[:5]]
def E(cf, n): ch = np.arange(n, dtype=float); return sum(c * ch ** i for i, c in enumerate(cf))
hb, bg = csv('bg_marinelli_water_1l_33.2h.csv'); bg[-1] = 0
for name, fn, bgi, lo, hi in (('черника', 'berry_dried_marinelli_1l_482g_49.8h.csv', 2, 38, 110), ('Th-232', 'th232_reference_marinelli_1l.csv', 1, 38, 110)):
    hs, y = csv(fn); y[-1] = 0
    es = E(coeffs(hs['page_scale_1'][0]), len(y)); eb = E(coeffs(hb['page_scale_%d' % bgi][0]), len(bg))
    ts, tb = float(hs['live_time_s'][0]), float(hb['live_time_s'][0])
    ds = y / np.gradient(es); db = bg / np.gradient(eb)
    w = (es > lo) & (es < hi); x = es[w]; yy = ds[w]; sig = np.sqrt(np.maximum(y[w], 1)) / np.gradient(es)[w]
    res = []
    for d in np.arange(-12, 12.001, 0.25):
        A = np.vstack([np.interp(x - d, eb, db) * ts / tb, np.ones_like(x), x - x.mean()]).T / sig[:, None]
        kk, *_ = np.linalg.lstsq(A, yy / sig, rcond=None); chi = float(np.sum((A @ kk - yy / sig) ** 2) / (w.sum() - 3)); res.append((chi, d, kk))
    at0 = [r for r in res if r[1] == 0][0]; best = min(res, key=lambda r: r[0])
    print(f'{name}: окно {lo}–{hi} кэВ, проба ≈ a·фон(E−δ) + b + c·E: δ=0 → χ²/ν {at0[0]:.1f}, a {at0[2][0]:.3f}, b {at0[2][1]:.0f}/кэВ | лучший δ {best[1]:+.2f} кэВ → χ²/ν {best[0]:.1f}, a {best[2][0]:.3f}, b {best[2][1]:.0f}/кэВ')
    # where is the BG hump max and the steep edge on each axis
    sm = lambda v: np.convolve(v, np.ones(15) / 15, 'same')
    for lab, e, v in (('проба', es, sm(ds)), ('фон', eb, sm(db))):
        m = (e > 40) & (e < 150); im = np.flatnonzero(m)[np.argmax(v[m])]
        half = np.flatnonzero((e < e[im]) & (e > 20) & (v < 0.5 * v[im]))
        print(f'   {lab}: максимум горба {e[im]:.1f} кэВ, полувысота левого фронта {e[half[-1]] if len(half) else float("nan"):.1f} кэВ')
