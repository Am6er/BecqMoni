# -*- coding: utf-8 -*-
# П167 (AMBER133): арбитр roi_mc.py ревизии П163 в ОПРЕДЕЛЕНИИ ЗОНЫ ПРИЛОЖЕНИЯ.
#
# roi_mc.py (П163) считает «истина/зона» = (ВСЕ прямые линии в окне) / (всё в окне),
# то есть соседняя прямая линия, попавшая в окно (у 384 вплотную — 356, у 303 — 276),
# входит и в истину, и в наблюдение. K зоны знает выход ОДНОЙ линии, и поправка
# приложения отвечает на другой вопрос: во сколько раз наблюдённое в окне (своя линия
# после выноса + ВСЕ многофотонные суммы в окне) отличается от прямой площади своей
# линии. Здесь это и считается: прямая — только фотоны своей линии; наблюдение —
# полное поглощение своей линии без партнёров + все сочетания из >= 2 полных
# поглощений с суммой в окне. Одиночные полные поглощения ЧУЖИХ линий не считаются —
# это не суммирование, их K не знал и прежде.
import sys, math
sys.path.insert(0, r'D:\BqMoni_Claude\p163\s2_ba133_eu152')
from tcs import *
from eu152 import eu152
from roi_mc import build_paths, expand


def roi_own(sc, ep, et, lines, fwhm662, hw=0.75):
    counts = {E: 0.0 for E in lines}
    direct = {E: 0.0 for E in lines}
    for prob, seq in build_paths(sc):
        for p, ph in expand(sc, prob, seq):
            if p < 1e-9:
                continue
            n = len(ph)
            for E in lines:
                for x in ph:
                    if abs(x - E) < 0.01:
                        direct[E] += p * ep(x)
            for mask in range(1, 1 << n):
                pr = p
                s = 0.0
                k = 0
                for i in range(n):
                    if mask >> i & 1:
                        pr *= ep(ph[i])
                        s += ph[i]
                        k += 1
                    else:
                        pr *= (1 - et(ph[i]))
                if pr < 1e-12:
                    continue
                for E in lines:
                    w = hw * fwhm662 / 100 * 662 * math.sqrt(E / 662)
                    if abs(s - E) > w:
                        continue
                    if k == 1:
                        # одиночное полное поглощение: только своя линия
                        x = [ph[i] for i in range(n) if mask >> i & 1][0]
                        if abs(x - E) >= 0.01:
                            continue
                    counts[E] += pr
    return {E: (direct[E] / counts[E] if counts[E] > 0 else float('nan')) for E in lines}


if __name__ == '__main__':
    G = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries' + '\\'
    ba = ba133()
    sm, gd = eu152()
    for name, f, fw in [('G1S_point5', G + 'G1S_point5.rmx', 6.44), ('G1S_point25', G + 'G1S_point25.rmx', 6.44),
                        ('RC103_point0', G + 'RC103_point0.rmx', 8.49), ('AS80_point0', G + 'AS80_point0.rmx', 7.65)]:
        ep, et = eff(f)
        r = roi_own(ba, ep, et, [80.9979, 302.8512, 356.0134, 383.8491], fw)
        print(name, 'Ba-133', ' '.join('%.1f:%.4f' % (E, c) for E, c in r.items()))
        r = roi_own(sm, ep, et, [121.7818, 244.6976, 964.082, 1408.013], fw)
        print('   Eu Sm', ' '.join('%.1f:%.4f' % (E, c) for E, c in r.items()))
        r = roi_own(gd, ep, et, [344.2789], fw)
        print('   Eu Gd', ' '.join('%.1f:%.4f' % (E, c) for E, c in r.items()))
        sys.stdout.flush()
