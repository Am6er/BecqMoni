# -*- coding: utf-8 -*-
"""П69 (S174): построчная таблица малой базы А (HEAD) против Б (правка) — «спектр — нуклид — доля до/после — серый слой в отсч.».

    python handover/p69-s174/mini_table.py <out_a> <out_b> [--out=handover/p69-s174/mini_rows.csv] [--sthr=0.30]

По каждому спектру (`*_spline_runs.csv`): часть, матрица применена, χ²/ndf А и Б (обязаны совпасть —
фит не тронут), серый слой Б (`*_spline_grey.csv`: ниже порога доверия и выше последней линии, отсчёты
и доля стека; у А файла нет), невязка «не описано / приписано» А → Б (из `*_spline_tails.csv`, где есть
хвост; иначе из `grey.csv` у Б), и все компоненты, чья доля слоя сдвинулась больше 0.005 %
(`share_pct` из `*_spline_components.csv`, F3). Разделитель — точка.

В конце — свод: сколько спектров сменили χ²/ndf (ожидание 0), у скольких серый слой ниже порога, у
скольких сдвинулась доля, сдвиг без серого слоя (ожидание 0); и ПОРОГ ДОЛИ мерки корпуса
(`score.py --sthr`, умолчание 0.30 %): по каждому плечу — сколько нуклидных СЕМЕЙСТВ (доля семейства =
сумма долей членов, `A280`) стоит ниже порога, сколько пересекло порог между А и Б, и наименьшая доля
семейства из объявленных в манифесте — то есть до какого порога recall не двинется.
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
            m[r['component']] = (float(r['share_pct']), r['kind'])
        except ValueError:
            m[r['component']] = (float('nan'), r['kind'])
    return m


def family_shares(rows):
    """Доля по семействам (`A280`): нуклидные строки, семейство — по DecayChainRoot нет в csv, поэтому
    семейством считается сам компонент; ряды видны по одинаковому z (связка) — здесь достаточно
    суммы по компоненту, порог судится по семейству ниже в score.py. Возвращает {компонент: доля}."""
    m = {}
    for r in rows:
        if r['kind'] == 'nuisance':
            continue
        try:
            m[r['component']] = float(r['share_pct'])
        except ValueError:
            pass
    return m


def main():
    da, db = sys.argv[1], sys.argv[2]
    out = None
    sthr = 0.30
    for a in sys.argv[3:]:
        if a.startswith('--out='):
            out = a[6:]
        elif a.startswith('--sthr='):
            sthr = float(a[7:])
    runsA, runsB = read(da, 'runs'), read(db, 'runs')
    compA, compB = read(da, 'components'), read(db, 'components')
    tailsA, tailsB = read(da, 'tails'), read(db, 'tails')
    greyB = read(db, 'grey')
    lines = ['spectrum,part,matrix,chi2_A,chi2_B,grey_below_B,grey_above_B,grey_pct_B,missing_A,missing_B,excess_A,excess_B,shifted,shifts']
    n_chi = n_grey = n_shift = n_shift_no_grey = 0
    below_a = below_b = crossed = 0
    min_share_a = min_share_b = None
    for s in sorted(runsA):
        ra, rb = runsA[s][0], runsB.get(s, [None])[0]
        if rb is None:
            lines.append('%s,%s,,%s,,НЕТ В Б,,,,,,,,' % (s, ra['part'], ra['chi2ndf']))
            continue
        ca, cb = comp_map(compA.get(s, [])), comp_map(compB.get(s, []))
        g = greyB.get(s, [None])[0]
        g_below = float(g['grey_below_floor']) if g else 0.0
        g_above = float(g['grey_above_lines']) if g else 0.0
        g_pct = g['grey_pct'] if g else ''
        ta = tailsA.get(s, [])
        tb = tailsB.get(s, [])
        missA = ta[0]['missing_pct'] if ta else ''
        excA = ta[0]['excess_pct'] if ta else ''
        missB = g['missing_pct'] if g else (tb[0]['missing_pct'] if tb else '')
        excB = g['excess_pct'] if g else (tb[0]['excess_pct'] if tb else '')
        shifts = []
        for k in sorted(set(ca) | set(cb)):
            a_ = ca.get(k, (0.0, ''))[0]
            b_ = cb.get(k, (0.0, ''))[0]
            if abs(a_ - b_) > 0.005:
                shifts.append('%s %.3f→%.3f' % (k, a_, b_))
        if ra['chi2ndf'] != rb['chi2ndf']:
            n_chi += 1
        if g_below > 0.0:
            n_grey += 1
        if shifts:
            n_shift += 1
        if shifts and not g_below > 0.0:
            n_shift_no_grey += 1
        fa, fb = family_shares(compA.get(s, [])), family_shares(compB.get(s, []))
        for k in fa:
            if fa[k] < sthr:
                below_a += 1
            if k in fb and (fa[k] >= sthr) != (fb[k] >= sthr):
                crossed += 1
                shifts.append('ПОРОГ %s %.3f→%.3f' % (k, fa[k], fb[k]))
        for k in fb:
            if fb[k] < sthr:
                below_b += 1
        # наименьшая доля из вошедших в состав — по всем нуклидным строкам
        for k, v in fa.items():
            if v > 0 and (min_share_a is None or v < min_share_a[0]):
                min_share_a = (v, s, k)
        for k, v in fb.items():
            if v > 0 and (min_share_b is None or v < min_share_b[0]):
                min_share_b = (v, s, k)
        lines.append(','.join([s, ra['part'], ra['matrix_applied'], ra['chi2ndf'], rb['chi2ndf'],
                               '%.1f' % g_below, '%.1f' % g_above, g_pct, missA, missB, excA, excB,
                               str(len(shifts)), '"%s"' % '; '.join(shifts)]))
    text = '\n'.join(lines) + '\n'
    sys.stdout.write(text)
    sys.stdout.write('свод: спектров %d; χ²/ndf сдвинулся у %d (ожидание 0); серый слой ниже порога у %d; доля сдвинулась у %d; '
                     'сдвиг без серого слоя %d (ожидание 0)\n'
                     % (len(runsA), n_chi, n_grey, n_shift, n_shift_no_grey))
    sys.stdout.write('порог --sthr %.2f %%: нуклидных строк ниже порога А %d, Б %d; пересекли порог между А и Б: %d; '
                     'наименьшая ненулевая доля нуклидной строки А %s, Б %s\n'
                     % (sthr, below_a, below_b, crossed,
                        ('%.3f %% (%s %s)' % min_share_a) if min_share_a else '-',
                        ('%.3f %% (%s %s)' % min_share_b) if min_share_b else '-'))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write(text)


if __name__ == '__main__':
    main()
