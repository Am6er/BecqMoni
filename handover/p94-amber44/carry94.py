# -*- coding: utf-8 -*-
# П94: спектр вклада ЗАНОСА по полосам (доля на историю ×1e4) — наш под ключом (eltr1 − eltr1_detour0) против
# истинного у арбитра (def − killcarry); средняя энергия события заноса и доля событий в 0–100 кэВ (как П92 carry_spectrum.py).
import io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, r'D:\BqMoni_Claude\p94')
from cmp94 import load
def bandsum(h, lo, hi): return sum(v for k, v in h.items() if lo <= k < hi)
for scene, e in (('RC103_point0_p55','1460.82'), ('RC103_point0_p55','661.657'), ('RC103_point0_p55','2614.511'), ('AS80_th_disk','2614.511')):
    d = load(scene, e)
    if not all(k in d for k in ('G4 def','G4 killcarry','наша eltr1','наша eltr1_detour0')):
        print(scene, e, 'нет плеч'); continue
    g4d, g4k = d['G4 def'][0], d['G4 killcarry'][0]
    ou, o0 = d['наша eltr1'][0], d['наша eltr1_detour0'][0]
    p = max(max(g4d), max(ou))
    print('=== %s %s (бин пика %d исключён)' % (scene, e, p))
    print('| полоса, кэВ | G4 занос истинный ×1e4 | наш занос (ВКЛ) ×1e4 | наш/G4 |')
    print('|---|---|---|---|')
    tg = to = 0.0; eg = eo = 0.0; g100 = o100 = 0.0
    lo = 0; step = 100 if p < 2000 else 200
    while lo < p - 3:
        hi = min(lo + step, p - 3)
        g = bandsum(g4d, lo, hi) - bandsum(g4k, lo, hi)
        o = bandsum(ou, lo, hi) - bandsum(o0, lo, hi)
        print('| %d–%d | %.2f | %.2f | %.2f |' % (lo, hi, 1e4*g, 1e4*o, o/g if g else float('nan')))
        tg += g; to += o
        mid = 0.5*(lo+hi); eg += g*mid; eo += o*mid
        if hi <= 100: g100 += g; o100 += o
        lo = hi
    print('| сумма | %.2f | %.2f | %.2f |' % (1e4*tg, 1e4*to, to/tg))
    print('| средняя энергия события, кэВ | %.0f | %.0f | |' % (eg/tg, eo/to))
    g100 = bandsum(g4d,0,100)-bandsum(g4k,0,100); o100 = bandsum(ou,0,100)-bandsum(o0,0,100)
    print('| доля событий в 0–100 кэВ | %.1f %% | %.1f %% | |' % (100*g100/tg, 100*o100/to))
    print()
