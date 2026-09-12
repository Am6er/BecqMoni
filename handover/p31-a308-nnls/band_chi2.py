# -*- coding: utf-8 -*-
"""П31 12.09.2026, `A308` — ПУАССОНОВСКИЙ χ²/n плеч малой базы В ОБЩЕЙ ПОЛОСЕ по дампам кривых
(`corpus/curves_<плечо>/<спектр>_curves.csv`, `CorpusFsaProbe --dump-curves=`).

    python handover/p31-a308-nnls/band_chi2.py [плечо ...]

`chi2ndf_pois` из `*_runs.csv` считается ПО ПОЛОСЕ ФИТА, и у плеча с полом 20 кэВ полоса другая — число
улучшается отчасти по построению. Здесь мера одна для всех плеч: Σ(net − model)²/max(net,1) на канал
по каналам 20 ≤ кэВ (верх — по дампу), плюс отдельно 0…20 кэВ (где и сидит порог АЦП), по спектрам
против `ctl`: лучше / хуже (порог 0.5 %), Σ по понятной части (parts.csv).
"""
import csv
import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

PARTS = {}
for r in csv.DictReader(open(os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'parts.csv'), encoding='utf-8-sig')):
    PARTS[r['spectrum']] = r['part']


def load(arm):
    out = {}
    for p in glob.glob(os.path.join(HERE, 'corpus', 'curves_' + arm, '*_curves.csv')):
        name = os.path.basename(p)[:-len('_curves.csv')]
        rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
        kev = [float(r['keV']) for r in rows]
        net = [float(r['net']) for r in rows]
        model = [float(r['model']) for r in rows]
        out[name] = (kev, net, model)
    return out


def chi(kev, net, model, lo, hi):
    ids = [i for i, e in enumerate(kev) if lo <= e < hi]
    if not ids:
        return float('nan'), 0
    return sum((net[i] - model[i]) ** 2 / max(net[i], 1.0) for i in ids) / len(ids), len(ids)


def main(arms):
    base = load('ctl')
    for part in ('known', 'unknown'):
        print('=== часть %s: χ²п/n, полоса 20+ кэВ (одна на все плечи) ===' % part)
        print('%-7s %3s %10s %6s %6s | %10s | %s' % ('плечо', 'n', 'Σχ²п/n 20+', 'лучше', 'хуже', 'Σχ²п/n <20', 'худшие сдвиги 20+: ctl → плечо'))
        for arm in arms:
            cur = load(arm)
            names = sorted(s for s in cur if PARTS.get(s) == part and s in base)
            tot = tot_lo = 0.0
            better = worse = 0
            deltas = []
            for s in names:
                a, _ = chi(*base[s], 20, 1e9)
                b, _ = chi(*cur[s], 20, 1e9)
                blo, _ = chi(*cur[s], -1e9, 20)
                tot += b
                tot_lo += blo if blo == blo else 0.0
                d = (b - a) / a if a else 0.0
                if d < -0.005:
                    better += 1
                elif d > 0.005:
                    worse += 1
                deltas.append((b - a, s, a, b))
            deltas.sort(reverse=True)
            worst = '; '.join('%s %.2f → %.2f' % (s, a, b) for _, s, a, b in deltas[:3])
            print('%-7s %3d %10.1f %6d %6d | %10.1f | %s' % (arm, len(names), tot, better, worse, tot_lo, worst))
        print()


if __name__ == '__main__':
    main(sys.argv[1:] or ['ctl', 'f20', 'adc', 'h0', 'f20_h0'])
