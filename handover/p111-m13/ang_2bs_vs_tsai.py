# П111 (M13): угловое распределение кванта тормозного относительно электрона — модифицированный Цай
# (G4ModifiedTsai, как у нас в `TsaiCosine`) против 2BS Коха—Моца (G4Generator2BS, как у арбитра option4).
# Числа: доля квантов в заднюю полусферу (cos θ < 0), средний cos θ, средний угол — по энергии электрона
# и энергии кванта; Z — вещества слоя (F 9 для PTFE, Al 13). Розыгрыши переписаны с исходников Geant4 11.4.2.
import math, random, sys

MC2 = 510.99895

def tsai_cos(t_kev, rnd):
    a1, a2, border = 1.6, 1.6 / 3.0, 0.25
    umax = 2.0 * (1.0 + t_kev / MC2)
    while True:
        uu = -math.log(rnd.random() * rnd.random())
        u = uu * a1 if border > rnd.random() else uu * a2
        if u <= umax:
            break
    return 1.0 - 2.0 * u * u / (umax * umax)

def z13(z):
    return z ** (1.0 / 3.0)

def bs2_cos(t_kev, k_kev, z, rnd):
    energy = t_kev + MC2                    # полная энергия электрона до излучения
    final = energy - k_kev                  # полная энергия после
    ratio = final / energy
    ratio1 = (1 + ratio) ** 2
    ratio2 = 1 + ratio * ratio
    gamma = energy / MC2
    beta = math.sqrt((gamma - 1) * (gamma + 1)) / gamma
    fz = 0.00008116224 * z13(z) * z13(z + 1)
    delta = 0.0
    def rej(y):
        y2 = (1 + y) * (1 + y)
        x = 4 * y * ratio / y2
        return 4 * x - ratio1 - (ratio2 - x) * math.log(delta + fz / y2)
    ymax = 2 * beta * (1 + beta) * gamma * gamma
    gmax = max(rej(0.0), rej(ymax))
    while True:
        q = rnd.random()
        y = q * ymax / (1 + ymax * (1 - q))
        g = rej(y)
        if rnd.random() * gmax <= g and y <= ymax:
            break
    return 1 - 2 * y / ymax

def stats(sampler, n):
    back = 0; sc = 0.0; sth = 0.0
    for _ in range(n):
        c = sampler()
        if c < 0: back += 1
        sc += c; sth += math.degrees(math.acos(max(-1.0, min(1.0, c))))
    return back / n, sc / n, sth / n

rnd = random.Random(20260919)
n = 200000
print("T,кэВ  k,кэВ  Z |  Цай: назад  <cos>  <θ>° |  2BS: назад  <cos>  <θ>° | назад 2BS/Цай")
for t in (100, 300, 500, 1000, 2000):
    for k in (10, 50, 200):
        if k >= t: continue
        for z in (9, 13):
            bt, ct, tht = stats(lambda: tsai_cos(t, rnd), n)
            bb, cb, thb = stats(lambda: bs2_cos(t, k, z, rnd), n)
            print("%5d %6d %2d | %11.4f %6.3f %5.1f | %11.4f %6.3f %5.1f | %6.2f" % (t, k, z, bt, ct, tht, bb, cb, thb, bb / bt if bt else float('nan')))
