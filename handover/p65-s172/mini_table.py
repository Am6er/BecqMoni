# -*- coding: utf-8 -*-
"""П65 (S172): построчная таблица малой базы А (HEAD) против Б (правка).

    python handover/p65-s172/mini_table.py <out_a> <out_b> [--out=handover/p65-s172/mini_rows.csv]

По каждому спектру (`*_runs.csv`): часть, матрица применена, χ²/ndf А и Б, разность, образ вылета
кристалла (`Esc-*`) в составе А и Б (доля %, z), число нуклидных строк состава, чья доля сдвинулась
больше 0.01 %, и сами сдвиги. Разделитель — точка. В конце — свод: сколько спектров сменили χ²/ndf,
сколько — состав, у скольких был образ вылета.
"""
import csv
import glob
import io
import os
import sys


def read(pattern_dir, suffix):
    rows = {}
    for path in sorted(glob.glob(os.path.join(pattern_dir, '*_spline_%s.csv' % suffix))):
        with io.open(path, encoding='utf-8-sig', newline='') as fh:
            for r in csv.DictReader(fh):
                rows.setdefault(r['spectrum'], []).append(r)
    return rows


def comp_map(rows):
    m = {}
    for r in rows:
        try:
            m[r['component']] = (float(r['share_pct']), float(r['z']))
        except ValueError:
            m[r['component']] = (float('nan'), float('nan'))
    return m


def main():
    da, db = sys.argv[1], sys.argv[2]
    out = None
    for a in sys.argv[3:]:
        if a.startswith('--out='):
            out = a[6:]
    runsA, runsB = read(da, 'runs'), read(db, 'runs')
    compA, compB = read(da, 'components'), read(db, 'components')
    lines = ['spectrum,part,matrix,chi2_A,chi2_B,delta,esc_A,esc_B,changed_nuclides,shifts']
    n_chi = n_comp = n_esc = 0
    for s in sorted(runsA):
        ra, rb = runsA[s][0], runsB.get(s, [None])[0]
        if rb is None:
            lines.append('%s,%s,,%s,,НЕТ В Б,,,,' % (s, ra['part'], ra['chi2ndf']))
            continue
        ca, cb = comp_map(compA.get(s, [])), comp_map(compB.get(s, []))
        escA = ', '.join('%s %.3f%% z=%.2f' % (k, v[0], v[1]) for k, v in sorted(ca.items()) if k.startswith('Esc-'))
        escB = ', '.join('%s %.3f%% z=%.2f' % (k, v[0], v[1]) for k, v in sorted(cb.items()) if k.startswith('Esc-'))
        shifts = []
        for k in sorted(set(ca) | set(cb)):
            if k.startswith('Esc-'):
                continue
            a_ = ca.get(k, (0.0, 0.0))[0]
            b_ = cb.get(k, (0.0, 0.0))[0]
            if abs(a_ - b_) > 0.01:
                shifts.append('%s %.3f→%.3f' % (k, a_, b_))
        try:
            chi_a, chi_b = float(ra['chi2ndf']), float(rb['chi2ndf'])
            delta = '%.4f' % (chi_b - chi_a)
            if abs(chi_b - chi_a) > 0.00005:
                n_chi += 1
        except ValueError:
            delta = ''
        if shifts:
            n_comp += 1
        if escA:
            n_esc += 1
        lines.append(','.join([s, ra['part'], ra['matrix_applied'], ra['chi2ndf'], rb['chi2ndf'], delta,
                               '"%s"' % escA, '"%s"' % escB, str(len(shifts)), '"%s"' % '; '.join(shifts)]))
    text = '\n'.join(lines) + '\n'
    sys.stdout.write(text)
    sys.stdout.write('свод: спектров %d; χ²/ndf сдвинулся у %d; состав (доля нуклида > 0.01 %%) у %d; образ вылета был у %d\n'
                     % (len(runsA), n_chi, n_comp, n_esc))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write(text)


if __name__ == '__main__':
    main()
