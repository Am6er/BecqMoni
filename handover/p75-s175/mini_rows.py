# -*- coding: utf-8 -*-
"""П75 (S175): таблица «спектр — компонент — доля А → Б — серый слой А (ниже порога) — хвост Б» по ВСЕМ спектрам
малой базы, где доля сдвинулась (>0.0005 п.п.) либо есть хвост. А — HEAD 08030c57 (сплайн ниже 100 кэВ серым,
хвост серым), Б — S175 (хвост в слое своего образа, S76 везде).

    python mini_rows.py <out_a> <out_b> --out=<csv>
"""
import csv
import glob
import io
import os
import sys


def read(d, suffix):
    rows = {}
    for path in sorted(glob.glob(os.path.join(d, '*_spline_%s.csv' % suffix))):
        with io.open(path, encoding='utf-8-sig', newline='') as fh:
            for r in csv.DictReader(fh):
                rows.setdefault(r['spectrum'], []).append(r)
    return rows


def main():
    da, db = sys.argv[1], sys.argv[2]
    out = None
    for a in sys.argv[3:]:
        if a.startswith('--out='):
            out = a[6:]
    comp_a, comp_b = read(da, 'components'), read(db, 'components')
    grey_a, grey_b = read(da, 'grey'), read(db, 'grey')
    tails_b = read(db, 'tails')
    lines = ['spectrum,part,component,share_a,share_b,delta,grey_below_a,grey_pct_a,grey_pct_b,tail_b']
    n_sp = 0
    n_rows = 0
    for sp in sorted(comp_a):
        if sp not in comp_b:
            continue
        sa = {r['component']: (float(r['share_pct']), r['part']) for r in comp_a[sp]}
        sb = {r['component']: float(r['share_pct']) for r in comp_b[sp]}
        ga = grey_a.get(sp, [{}])[0]
        gb = grey_b.get(sp, [{}])[0]
        tb = {r['component']: float(r['tail_counts']) for r in tails_b.get(sp, [])}
        any_row = False
        for c in sorted(set(sa) | set(sb)):
            va = sa.get(c, (float('nan'), ''))[0]
            vb = sb.get(c, float('nan'))
            if abs(va - vb) > 0.0005 or c in tb:
                any_row = True
                n_rows += 1
                lines.append(','.join([sp, sa.get(c, ('', ga.get('part', '')))[1] or ga.get('part', ''), c,
                                       '%.3f' % va, '%.3f' % vb, '%+.3f' % (vb - va),
                                       ga.get('grey_below_floor', ''), ga.get('grey_pct', ''), gb.get('grey_pct', ''),
                                       '%.1f' % tb[c] if c in tb else '']))
        if any_row:
            n_sp += 1
    text = '\n'.join(lines) + '\n'
    text += 'свод: строк %d у %d спектров\n' % (n_rows, n_sp)
    sys.stdout.write(text if len(lines) < 40 else text.splitlines()[-1] + '\n')
    if out:
        io.open(out, 'w', encoding='utf-8', newline='\n').write(text)


if __name__ == '__main__':
    main()
