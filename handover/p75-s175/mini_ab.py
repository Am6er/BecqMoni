# -*- coding: utf-8 -*-
"""П75 (S175): малая база — плечо А (HEAD 08030c57: хвост в подложке → серый слой) против Б (правка: хвост в слое
и доле своего образа).

    python mini_ab.py <out_a> <out_b> [--out=<csv>]

Хвосты берутся у Б (`*_spline_tails.csv`: у А `UntiedTails` пуст по построению П70). По каждому спектру с
ненулевым хвостом: χ²/ndf А/Б (обязаны совпасть), хвост Б (отсч., адресат), серый слой ниже порога А → Б
(`*_spline_grey.csv`; ожидание по второму решению Amber «В слои образов по S76 везде»: у Б 0 — пола разноса
нет), «не описано» А → Б (ожидание: равны — хвост в модели в обоих плечах), доли компонентов А → Б
(`*_spline_components.csv`). Свод: спектров с хвостом, сколько сменили χ²/ndf (ожидание 0), у скольких серый
слой ниже порога у Б нулевой, у скольких «не описано» то же. Разделитель — точка."""
import csv
import glob
import io
import os
import sys


def read(d, suffix, key='spectrum'):
    rows = {}
    for path in sorted(glob.glob(os.path.join(d, '*_spline_%s.csv' % suffix))):
        with io.open(path, encoding='utf-8-sig', newline='') as fh:
            for r in csv.DictReader(fh):
                rows.setdefault(r[key], []).append(r)
    return rows


def main():
    da, db = sys.argv[1], sys.argv[2]
    out = None
    for a in sys.argv[3:]:
        if a.startswith('--out='):
            out = a[6:]
    tails = read(db, 'tails')
    grey_a, grey_b = read(da, 'grey'), read(db, 'grey')
    runs_a, runs_b = read(da, 'runs'), read(db, 'runs')
    comp_a, comp_b = read(da, 'components'), read(db, 'components')
    lines = []
    hdr = ['spectrum', 'part', 'chi2ndf_a', 'chi2ndf_b', 'tail_b', 'placement', 'grey_below_a', 'grey_below_b',
           'grey_b_zero', 'missing_a', 'missing_b', 'shares_a_to_b']
    lines.append(','.join(hdr))
    n_tail = n_chi = n_grey_ok = n_miss_ok = 0
    n_runs = 0
    n_chi_all = 0
    for sp in sorted(runs_a):
        if sp in runs_b:
            n_runs += 1
            if runs_a[sp][0]['chi2ndf'] != runs_b[sp][0]['chi2ndf']:
                n_chi_all += 1
    for sp in sorted(tails):
        tail = sum(float(r['tail_counts']) for r in tails[sp])
        if not tail > 0.0:
            continue
        n_tail += 1
        ra, rb = runs_a[sp][0], runs_b[sp][0]
        ca, cb = float(ra['chi2ndf']), float(rb['chi2ndf'])
        if ra['chi2ndf'] != rb['chi2ndf']:
            n_chi += 1
        ga = grey_a.get(sp, [{}])[0]
        gb = grey_b.get(sp, [{}])[0]
        gba = float(ga.get('grey_below_floor', 'nan'))
        gbb = float(gb.get('grey_below_floor', 'nan'))
        # Второе решение Amber: пола разноса нет, серый слой ниже порога у Б обязан быть нулём.
        ok = gbb == 0.0
        if ok:
            n_grey_ok += 1
        ma, mb = float(ga.get('missing_pct', 'nan')), float(gb.get('missing_pct', 'nan'))
        if abs(mb - ma) <= 1e-6:
            n_miss_ok += 1
        sa = {r['component']: float(r['share_pct']) for r in comp_a.get(sp, [])}
        sb = {r['component']: float(r['share_pct']) for r in comp_b.get(sp, [])}
        shifts = []
        for c in sorted(set(sa) | set(sb)):
            va, vb = sa.get(c, float('nan')), sb.get(c, float('nan'))
            if abs(va - vb) > 0.0005 or c in [t['component'] for t in tails[sp]]:
                shifts.append('%s %.3f->%.3f' % (c, va, vb))
        placement = ';'.join(sorted(set(t.get('placement', '?') for t in tails[sp])))
        lines.append(','.join([sp, ra['part'], '%.4f' % ca, '%.4f' % cb, '%.1f' % tail, placement, '%.1f' % gba, '%.1f' % gbb,
                               '1' if ok else '0', '%.3f' % ma, '%.3f' % mb, '"' + '; '.join(shifts) + '"']))
    text = '\n'.join(lines) + '\n'
    text += ('свод: спектров в обоих плечах %d, χ²/ndf сдвинулся у %d (ожидание 0); спектров с хвостом у Б %d; χ²/ndf сдвинулся у %d;'
             ' серый слой ниже порога у Б нулевой у %d из %d; «не описано» то же у %d из %d\n'
             % (n_runs, n_chi_all, n_tail, n_chi, n_grey_ok, n_tail, n_miss_ok, n_tail))
    sys.stdout.write(text)
    if out:
        io.open(out, 'w', encoding='utf-8', newline='\n').write(text)


if __name__ == '__main__':
    main()
