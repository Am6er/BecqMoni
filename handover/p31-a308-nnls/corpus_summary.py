# -*- coding: utf-8 -*-
"""П31 12.09.2026, `A308` — сводка плеч пола полосы фита на МАЛОЙ БАЗЕ (каталоги tools/pie/out_p31_<плечо>).

    python handover/p31-a308-nnls/corpus_summary.py [плечо ...]

По части (parts.csv, как score.py): Σ chi2ndf (ВЕСА РЕШАТЕЛЯ — между плечами с разным M несравним, это
число печатает score.py), Σ chi2ndf_pois (пуассоновские отчётные веса — единственная сравнимая метрика,
`FsaResult.Chi2NdfPoisson`), медиана невязки модели, и по спектрам против `ctl`: лучше / хуже / равно по
chi2ndf_pois (порог 0.5 %), худшие сдвиги. Плюс диск AS80_Th232Medal: обе метрики, невязка, опоры, усиление.
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


def runs(arm):
    out = {}
    for p in glob.glob(os.path.join(ROOT, 'tools', 'pie', 'out_p31_' + arm, '*_spline_runs.csv')):
        for r in csv.DictReader(open(p, encoding='utf-8-sig')):
            out[r['spectrum']] = r
    return out


def f(r, k):
    try:
        return float(r[k])
    except (KeyError, ValueError):
        return float('nan')


def main(arms):
    base = runs('ctl')
    for part in ('known', 'unknown'):
        print('=== часть %s ===' % part)
        print('%-5s %5s %10s %12s %8s | %6s %6s %6s | %s' % (
            'плечо', 'n', 'Σχ²(solv)', 'Σχ²(pois)', 'невяз.м', 'лучше', 'хуже', 'равно', 'худшие сдвиги χ²(pois), спектр: ctl → плечо'))
        for arm in arms:
            rr = runs(arm)
            sel = [s for s in rr if PARTS.get(s) == part and rr[s].get('error', '') == '']
            n = len(sel)
            s_solv = sum(f(rr[s], 'chi2ndf') for s in sel)
            s_pois = sum(f(rr[s], 'chi2ndf_pois') for s in sel)
            res = sorted(f(rr[s], 'model_residual_pct') for s in sel)
            med = res[len(res) // 2] if res else float('nan')
            better = worse = same = 0
            deltas = []
            for s in sel:
                if s not in base:
                    continue
                a, b = f(base[s], 'chi2ndf_pois'), f(rr[s], 'chi2ndf_pois')
                d = (b - a) / a if a else 0.0
                if d < -0.005:
                    better += 1
                elif d > 0.005:
                    worse += 1
                else:
                    same += 1
                deltas.append((b - a, s, a, b))
            deltas.sort(reverse=True)
            worst = '; '.join('%s %.2f → %.2f' % (s, a, b) for _, s, a, b in deltas[:3])
            print('%-5s %5d %10.1f %12.1f %7.1f%% | %6d %6d %6d | %s' % (arm, n, s_solv, s_pois, med, better, worse, same, worst))
    print()
    print('=== диск AS80_Th232Medal ===')
    print('%-5s %10s %12s %8s %5s %8s %8s' % ('плечо', 'χ²(solv)', 'χ²(pois)', 'невяз.м', 'опор', 'усил', 'ноль,к'))
    for arm in arms:
        r = runs(arm).get('AS80_Th232Medal')
        if r is None:
            print('%-5s — нет' % arm)
            continue
        print('%-5s %10.3f %12.3f %7.2f%% %5s %8.5f %8.3f' % (
            arm, f(r, 'chi2ndf'), f(r, 'chi2ndf_pois'), f(r, 'model_residual_pct'), r.get('anchors_used', ''),
            f(r, 'gain'), f(r, 'offset_ch')))


if __name__ == '__main__':
    main(sys.argv[1:] or ['ctl', 'f20', 'adc', 'h0', 'f20_h0'])
