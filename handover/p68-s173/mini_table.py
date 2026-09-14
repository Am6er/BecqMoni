# -*- coding: utf-8 -*-
"""П68 (S173): построчная таблица малой базы А (HEAD) против Б (правка) — «спектр — нуклид — доля до/после — хвост в отсч.».

    python handover/p68-s173/mini_table.py <out_a> <out_b> [--out=handover/p68-s173/mini_rows.csv]

По каждому спектру (`*_spline_runs.csv`): часть, матрица применена, χ²/ndf А и Б (обязаны совпасть —
фит не тронут), отвязанные хвосты Б (`*_spline_tails.csv`: компонент и отсчёты; у А файла нет),
невязка «не описано / приписано» Б, и все компоненты, чья доля слоя сдвинулась больше 0.005 %
(`share_pct` из `*_spline_components.csv`, F3). Разделитель — точка. В конце — свод: сколько спектров
сменили χ²/ndf (ожидание 0), у скольких есть хвост, у скольких сдвинулась доля, и что сдвиг без хвоста
либо хвост без сдвига — отдельно (ожидание 0 и 0).
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
            m[r['component']] = float(r['share_pct'])
        except ValueError:
            m[r['component']] = float('nan')
    return m


def main():
    da, db = sys.argv[1], sys.argv[2]
    out = None
    for a in sys.argv[3:]:
        if a.startswith('--out='):
            out = a[6:]
    runsA, runsB = read(da, 'runs'), read(db, 'runs')
    compA, compB = read(da, 'components'), read(db, 'components')
    tailsB = read(db, 'tails')
    lines = ['spectrum,part,matrix,chi2_A,chi2_B,tail_components,tail_counts,missing_pct_B,excess_pct_B,shifted,shifts']
    n_chi = n_tail = n_shift = n_shift_no_tail = n_tail_no_shift = 0
    for s in sorted(runsA):
        ra, rb = runsA[s][0], runsB.get(s, [None])[0]
        if rb is None:
            lines.append('%s,%s,,%s,,НЕТ В Б,,,,,' % (s, ra['part'], ra['chi2ndf']))
            continue
        ca, cb = comp_map(compA.get(s, [])), comp_map(compB.get(s, []))
        tails = tailsB.get(s, [])
        tail_names = '; '.join('%s %s' % (t['component'], t['tail_counts']) for t in tails)
        tail_total = sum(float(t['tail_counts']) for t in tails)
        missing = tails[0]['missing_pct'] if tails else ''
        excess = tails[0]['excess_pct'] if tails else ''
        shifts = []
        for k in sorted(set(ca) | set(cb)):
            a_ = ca.get(k, 0.0)
            b_ = cb.get(k, 0.0)
            if abs(a_ - b_) > 0.005:
                shifts.append('%s %.3f→%.3f' % (k, a_, b_))
        if ra['chi2ndf'] != rb['chi2ndf']:
            n_chi += 1
        if tails:
            n_tail += 1
        if shifts:
            n_shift += 1
        if shifts and not tails:
            n_shift_no_tail += 1
        if tails and not shifts:
            n_tail_no_shift += 1
        lines.append(','.join([s, ra['part'], ra['matrix_applied'], ra['chi2ndf'], rb['chi2ndf'],
                               '"%s"' % tail_names, '%.1f' % tail_total, missing, excess,
                               str(len(shifts)), '"%s"' % '; '.join(shifts)]))
    text = '\n'.join(lines) + '\n'
    sys.stdout.write(text)
    sys.stdout.write('свод: спектров %d; χ²/ndf сдвинулся у %d (ожидание 0); хвост есть у %d; доля сдвинулась у %d; '
                     'сдвиг без хвоста %d (ожидание 0); хвост без сдвига %d\n'
                     % (len(runsA), n_chi, n_tail, n_shift, n_shift_no_tail, n_tail_no_shift))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write(text)


if __name__ == '__main__':
    main()
