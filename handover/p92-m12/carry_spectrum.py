# -*- coding: utf-8 -*-
# П92 (M12): СПЕКТР ВКЛАДА ЗАНОСА электронов из обвязки по полосам шкалы, доля на историю ×1e4:
#   G4 истинный          = G4 def − G4 killcarry      (занос как есть: перенос в обвязке + судьба в кристалле)
#   G4 «наше в кристалле» = G4 fullcarry − G4 killcarry (перенос в обвязке Geant4, но занесённый электрон отдаёт
#                                                      кристаллу весь остаток — наше ElectronCarryDeposit)
#   наша                 = наша ref − наша detour0     (наш обход по прямой с detour 0.7 + полный остаток)
# Форма этого спектра — диагноз: суммы близки, формы разные → ошибка в РАСПРЕДЕЛЕНИИ энергии занесённого,
# и столбец «наше в кристалле» показывает, какая доля сдвига — судьба электрона В КРИСТАЛЛЕ.
#   python carry_spectrum.py <сцена> <E> [шаг_полосы_кэВ=100] [--md]
import io, os, re, sys, math
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cmp92 import read_ours, read_g4, ROOT


def bandsum(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


def main():
    scene, e = sys.argv[1], sys.argv[2]
    step = int(sys.argv[3]) if len(sys.argv) > 3 and sys.argv[3].isdigit() else 100
    md = '--md' in sys.argv
    g4d, nd = read_g4(os.path.join(ROOT, 'g4out', 'g4_%s_%s_def.log' % (scene, e)))
    g4k, nk = read_g4(os.path.join(ROOT, 'g4out', 'g4_%s_%s_killcarry.log' % (scene, e)))
    fpath = os.path.join(ROOT, 'g4out', 'g4_%s_%s_fullcarry.log' % (scene, e))
    g4f, nf = read_g4(fpath) if os.path.exists(fpath) else (None, None)
    our, no = read_ours(os.path.join(ROOT, 'ours', 'ours_%s_%s_ref.csv' % (scene, e)))
    ou0, n0 = read_ours(os.path.join(ROOT, 'ours', 'ours_%s_%s_detour0.csv' % (scene, e)))
    p = max(max(g4d), max(our))
    title = '%s, E = %s кэВ: вклад заноса по полосам, доля на историю ×1e4 (бин пика %d исключён)' % (scene, e, p)
    hdr = ['полоса, кэВ', 'G4 def (всё)', 'G4 занос истинный', 'σ', 'G4 занос «наше в кристалле»', 'наша ref (всё)', 'наш занос', 'σ', 'наш/G4 ист.', 'наш/G4 «наше»']
    rows = []
    tg = tf = to = 0.0
    lo = 0
    while lo < p - 3:
        hi = min(lo + step, p - 3)
        gd, gk = bandsum(g4d, lo, hi), bandsum(g4k, lo, hi)
        gf = bandsum(g4f, lo, hi) if g4f else float('nan')
        od, ok = bandsum(our, lo, hi), bandsum(ou0, lo, hi)
        cg, cf, co = gd - gk, gf - gk, od - ok
        sg = math.sqrt(gd / nd + gk / nk)
        so = math.sqrt(od / no + ok / n0)
        tg += cg
        tf += cf if g4f else 0.0
        to += co
        r1 = co / cg if cg > 3 * sg else float('nan')
        r2 = co / cf if g4f and cf > 3 * sg else float('nan')
        rows.append(['%d–%d' % (lo, hi), '%.2f' % (gd * 1e4), '%.2f' % (cg * 1e4), '%.2f' % (sg * 1e4),
                     '%.2f' % (cf * 1e4) if g4f else '—', '%.2f' % (od * 1e4), '%.2f' % (co * 1e4), '%.2f' % (so * 1e4),
                     '%.2f' % r1, '%.2f' % r2 if g4f else '—'])
        lo = hi
    rows.append(['сумма континуума', '', '%.2f' % (tg * 1e4), '', '%.2f' % (tf * 1e4) if g4f else '—', '', '%.2f' % (to * 1e4), '',
                 '%.2f' % (to / tg), '%.2f' % (to / tf) if g4f else '—'])

    def mean_e(a, b):
        num = den = 0.0
        for k in range(0, p - 3):
            d = a.get(k, 0.0) - b.get(k, 0.0)
            num += d * k
            den += d
        return num / den if den else float('nan')

    def soft_share(a, b, cut=100):
        tot = sum(a.get(k, 0.0) - b.get(k, 0.0) for k in range(0, p - 3))
        soft = sum(a.get(k, 0.0) - b.get(k, 0.0) for k in range(0, min(cut, p - 3)))
        return 100.0 * soft / tot if tot else float('nan')

    if md:
        print('**%s**' % title)
        print()
        print('| ' + ' | '.join(hdr) + ' |')
        print('|' + '---|' * len(hdr))
        for r in rows:
            print('| ' + ' | '.join(r) + ' |')
    else:
        print('=== %s ===' % title)
        print(' | '.join(hdr))
        for r in rows:
            print(' | '.join(r))
    print()
    line = 'средняя энергия события заноса, кэВ: G4 истинный %.0f' % mean_e(g4d, g4k)
    if g4f:
        line += ', G4 «наше в кристалле» %.0f' % mean_e(g4f, g4k)
    line += ', наша %.0f' % mean_e(our, ou0)
    print(line)
    line = 'доля событий заноса в 0–100 кэВ, %%: G4 истинный %.1f' % soft_share(g4d, g4k)
    if g4f:
        line += ', G4 «наше в кристалле» %.1f' % soft_share(g4f, g4k)
    line += ', наша %.1f' % soft_share(our, ou0)
    print(line)


if __name__ == '__main__':
    main()
