# -*- coding: utf-8 -*-
"""П70 (AMBER30): малая база — плечо А (HEAD da9caf85: хвост лентой невязки) против Б (правка: хвост в сером слое).

    python D:\\BqMoni_Claude\\p70\\mini_ab.py <out_a> <out_b> [--out=<csv>]

По каждому спектру с ненулевым хвостом у А (`*_spline_tails.csv` есть только у А — у Б `UntiedTail` пуст по
построению): χ²/ndf А/Б (обязаны совпасть), хвост А (отсч.), серый слой ниже порога А → Б (`*_spline_grey.csv`),
«не описано» А → Б, доли носителя и нуклидных строк А → Б (`*_spline_components.csv`). Свод: спектров с хвостом,
сколько сменили χ²/ndf (ожидание 0), у скольких серый слой вырос ровно на хвост (в допуске 0.5 отсч. + 1e-6 доли),
у скольких «не описано» не выросло. Разделитель — точка."""
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
    tails = read(da, 'tails')
    grey_a, grey_b = read(da, 'grey'), read(db, 'grey')
    runs_a, runs_b = read(da, 'runs'), read(db, 'runs')
    comp_a, comp_b = read(da, 'components'), read(db, 'components')
    lines = []
    hdr = ['spectrum', 'part', 'chi2ndf_a', 'chi2ndf_b', 'tail_a', 'grey_below_a', 'grey_below_b', 'grey_grew_by_tail',
           'missing_a', 'missing_b', 'shares_a_to_b']
    lines.append(','.join(hdr))
    n_tail = n_chi = n_grey_ok = n_miss_ok = 0
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
        grew = abs((gbb - gba) - tail) <= 0.5 + 1e-6 * tail
        if grew:
            n_grey_ok += 1
        ma, mb = float(ga.get('missing_pct', 'nan')), float(gb.get('missing_pct', 'nan'))
        if mb <= ma + 1e-9:
            n_miss_ok += 1
        sa = {r['component']: float(r['share_pct']) for r in comp_a.get(sp, [])}
        sb = {r['component']: float(r['share_pct']) for r in comp_b.get(sp, [])}
        shifts = []
        for c in sorted(set(sa) | set(sb)):
            va, vb = sa.get(c, float('nan')), sb.get(c, float('nan'))
            if abs(va - vb) > 0.0005 or c in [t['component'] for t in tails[sp]]:
                shifts.append('%s %.3f->%.3f' % (c, va, vb))
        lines.append(','.join([sp, ra['part'], '%.4f' % ca, '%.4f' % cb, '%.1f' % tail, '%.1f' % gba, '%.1f' % gbb,
                               '1' if grew else '0', '%.3f' % ma, '%.3f' % mb, '"' + '; '.join(shifts) + '"']))
    text = '\n'.join(lines) + '\n'
    text += ('свод: спектров с хвостом у А %d; χ²/ndf сдвинулся у %d (ожидание 0); серый слой ниже порога вырос ровно на хвост у %d из %d;'
             ' «не описано» не выросло у %d из %d\n' % (n_tail, n_chi, n_grey_ok, n_tail, n_miss_ok, n_tail))
    sys.stdout.write(text)
    if out:
        io.open(out, 'w', encoding='utf-8', newline='\n').write(text)


if __name__ == '__main__':
    main()
