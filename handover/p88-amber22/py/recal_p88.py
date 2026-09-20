# -*- coding: utf-8 -*-
r"""П88: шкала ПРОБЫ по её пикам (ряд Th-232) — устойчивый вариант recal.py П56/П67 для плеча `eq`.
Отличие: сид — ВСЕГДА линейная шкала по двум опорам (максимумы 238 и 2614 в нетто), а не файловая: файловая
квартика P врёт на −90 кэВ у 1588, и группа 1588 при окнах по ней подгонялась мимо линии (П88: recal.py принимал
шкалу у одних копий и отвергал у других того же спектра — разница была лишь в шкале фона). Три прохода «фит групп по
текущей шкале → кубика», группа с невязкой > 6 кэВ выбрасывается (не более двух), итог — кубика по оставшимся."""
import sys

import numpy as np
from scipy.ndimage import uniform_filter1d

sys.path.insert(0, r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p67-amber22\py')
import recal   # noqa: E402


def recal_probe(S, deg=3, verbose=False):
    net, _ = S.net()
    sm = uniform_filter1d(net, 9)
    ch = np.arange(S.n)

    def argmax_in(lo, hi):
        ids = np.where((S.keV >= lo) & (S.keV < hi) & (ch < S.n - 100))[0]
        return ids[np.argmax(sm[ids])]
    c238 = argmax_in(190, 280); c2614 = argmax_in(2200, 3000)
    g = (2614.511 - 238.632) / (c2614 - c238); z = 238.632 - g * c238
    coef = [z, g]
    groups = list(recal.GROUPS)
    dropped = []
    for it in range(4):
        pts = []
        for label, lo, hi, lines in groups:
            try:
                cc, fwk, chi, A, gg = recal.fit_group_ch(net, coef, lo, hi, lines)
            except Exception as e:  # noqa: BLE001
                if verbose:
                    print('  группа', label, 'не подогнана:', e)
                continue
            pts.append((label, cc, lines[0][0], fwk, chi))
        chs = np.array([p[1] for p in pts]); E = np.array([p[2] for p in pts])
        if len(pts) < deg + 2:
            break
        coef = list(np.polyfit(chs, E, deg)[::-1])
        res = E - recal.scale_of(coef, chs)
        worst = int(np.argmax(np.abs(res)))
        if it >= 1 and abs(res[worst]) > 6.0 and len(dropped) < 2:
            dropped.append((pts[worst][0], res[worst]))
            groups = [gr for gr in groups if gr[0] != pts[worst][0]]
    res = E - recal.scale_of(coef, chs)
    return coef, [(p[0], p[1], p[2]) for p in pts], res, dropped
