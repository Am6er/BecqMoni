# П186: численная проба на готовой кривой разбора — что снимает окно Kβ: ширина или сдвиг рентгена.
# Модель в каналах lo..hi (группа K-рентгена) доуширяется гауссом σ_extra (каналы) и/или сдвигается на d каналов
# (с сохранением площади), затем считаются n/m и Σ(n−m)²/m в окнах Kα 29–34 и Kβ 34.5–39.5 кэВ, как kbwin.py.
# python broad.py <curves.csv> <lo_ch> <hi_ch>
import csv, sys, math
rows = list(csv.DictReader(open(sys.argv[1])))
lo, hi = int(sys.argv[2]), int(sys.argv[3])
keV = [float(r['keV']) for r in rows]; n = [float(r['fit']) for r in rows]; m0 = [float(r['model']) for r in rows]
# рентгеновская группа модели = модель в lo..hi минус линейная подложка по краям (грубо: краевые каналы)
base = [0.0] * len(rows)
for i in range(lo, hi + 1):
    t = (i - lo) / float(hi - lo)
    base[i] = m0[lo] * (1 - t) + m0[hi] * t
grp = [max(m0[i] - base[i], 0.0) if lo <= i <= hi else 0.0 for i in range(len(rows))]
def transform(se, d):
    out = [0.0] * len(rows)
    for i in range(lo, hi + 1):
        if grp[i] == 0: continue
        c = i + d
        if se <= 0:
            j = int(math.floor(c)); f = c - j
            if 0 <= j < len(out): out[j] += grp[i] * (1 - f)
            if 0 <= j + 1 < len(out): out[j + 1] += grp[i] * f
            continue
        w = [math.exp(-0.5 * ((k - c) / se) ** 2) for k in range(len(rows))]
        s = sum(w)
        for k in range(len(rows)): out[k] += grp[i] * w[k] / s
    return [m0[k] - grp[k] + out[k] for k in range(len(rows))]
def win(m, a, b):
    nn = mm = c = 0.0
    for i, e in enumerate(keV):
        if a <= e <= b:
            nn += n[i]; mm += m[i]; c += (n[i] - m[i]) ** 2 / max(m[i], 1.0)
    return nn / mm, c
def tot(m):
    c = 0.0
    for i in range(lo, hi + 1): c += (n[i] - m[i]) ** 2 / max(m[i], 1.0)
    return c
print('σ_доп(кан)  сдвиг(кан)   Kα n/m      χ²     Kβ n/m      χ²   Σχ² группы')
for se, d in [(0, 0), (0.6, 0.15), (0.7, 0.2), (0.75, 0.2), (0.8, 0.2), (0.8, 0.25), (0.9, 0.2), (0.75, 0.3)]:
    m = transform(se, d)
    a1, c1 = win(m, 29, 34); a2, c2 = win(m, 34.5, 39.5)
    print('%8.2f %10.2f %10.4f %8.0f %10.4f %8.0f %10.0f' % (se, d, a1, c1, a2, c2, tot(m)))
