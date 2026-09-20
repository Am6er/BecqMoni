# -*- coding: utf-8 -*-
# П94: сколько энергии ВОЗВРАТ электрона приносит в кристалл на историю — у арбитра (def − killesc, средняя
# энергия на историю по гистограмме) и у нас (eltr1_detour0 − off_detour0 по гистограмме и по счётчику пробы).
import io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, r'D:\BqMoni_Claude\p94')
from cmp94 import read_ours, read_g4, load
def mean_kev(h):
    return sum(k * v for k, v in h.items())
for scene, e in (('RC103_point0_p55', '1460.82'), ('RC103_point0_p55', '2614.511'), ('RC103_point0_p55', '661.657')):
    d = load(scene, e)
    g4d, g4k = d['G4 def'][0], d['G4 killesc'][0]
    print('%s %s: G4 средний занос на историю def %.4f кэВ, killesc %.4f, возврат приносит %.4f кэВ/историю' % (
        scene, e, mean_kev(g4d), mean_kev(g4k), mean_kev(g4d) - mean_kev(g4k)))
    if 'наша eltr1_detour0' in d and 'наша off_detour0' in d:
        a, b = d['наша eltr1_detour0'][0], d['наша off_detour0'][0]
        print('   наш возврат (eltr1_detour0 − off_detour0): %.4f кэВ/историю' % (mean_kev(a) - mean_kev(b)))
    if 'наша eltr1' in d and 'наша off' in d:
        a, b = d['наша eltr1'][0], d['наша off'][0]
        print('   наш ключ целиком (eltr1 − off): %.4f кэВ/историю' % (mean_kev(a) - mean_kev(b)))
    txt = os.path.join(r'D:\BqMoni_Claude\p94\ours', 'ours_%s_%s_eltr1_detour0.txt' % (scene, e))
    if os.path.exists(txt):
        for line in io.open(txt, encoding='utf-8', errors='replace'):
            if 'перенос в слоях' in line:
                m = re.search(r'слои (\d+), из них вернулось (\d+) \(([\d.E+-]+) на историю, средняя энергия возврата ([\d.]+)', line)
                if m:
                    n_esc, n_ret, per, mean = int(m.group(1)), int(m.group(2)), float(m.group(3)), float(m.group(4))
                    print('   счётчик: вылетов %d, вернулось %d (%.1f %%), %.3e на историю, средняя %.1f кэВ → %.4f кэВ/историю' % (
                        n_esc, n_ret, 100.0 * n_ret / max(n_esc, 1), per, mean, per * mean))
