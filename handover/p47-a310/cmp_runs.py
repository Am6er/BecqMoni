# -*- coding: utf-8 -*-
"""П47 13.09.2026 (A310) — сравнение двух каталогов прогона малой базы поимённо.

    python handover/p47-a310/cmp_runs.py <база> <плечо> [--full]

Читает *_spline_runs.csv, *_spline_components.csv, *_spline_limits.csv, *_spline_anchors.csv обоих
каталогов. Побитовость судится по всем графам, КРОМЕ времени (`ms`, `cpu_ms`) — их нет смысла сравнивать.
Для плеча печатает по спектрам: χ²/ndf решателя (`chi2ndf` — при весах по модели ДРУГАЯ метрика),
отчётный пуассоновский `chi2ndf_pois` (сравним, П30 §5), невязку модели, состав (найдено/снято).
"""
import csv
import glob
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

SKIP = {'ms', 'cpu_ms'}


def load(d, kind):
    rows = {}
    for p in sorted(glob.glob(os.path.join(d, '*_spline_%s.csv' % kind))):
        with open(p, encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                key = r.get('spectrum') or r.get('key')
                if kind == 'runs':
                    rows[key] = r
                else:
                    rows.setdefault(key, []).append(r)
    return rows


def same_row(a, b):
    ks = set(a) | set(b)
    return all(a.get(k) == b.get(k) for k in ks if k not in SKIP)


def main(argv):
    base, arm = argv[0], argv[1]
    full = '--full' in argv
    diff_total = 0
    for kind in ('runs', 'components', 'limits', 'anchors'):
        A, B = load(base, kind), load(arm, kind)
        keys = sorted(set(A) | set(B))
        diff = []
        for k in keys:
            a, b = A.get(k), B.get(k)
            if a is None or b is None:
                diff.append(k)
                continue
            if kind == 'runs':
                if not same_row(a, b):
                    diff.append(k)
            else:
                if len(a) != len(b) or not all(same_row(x, y) for x, y in zip(a, b)):
                    diff.append(k)
        diff_total += len(diff)
        print('%-11s спектров %d, расходятся %d%s' % (kind, len(keys), len(diff), (': ' + ', '.join(diff)) if diff and len(diff) <= 60 else ''))
    print('ИТОГ: %s' % ('ПОБИТОВО (кроме ms/cpu_ms)' if diff_total == 0 else 'РАСХОЖДЕНИЙ %d' % diff_total))
    if not full:
        return 0 if diff_total == 0 else 1
    A, B = load(base, 'runs'), load(arm, 'runs')
    CA, CB = load(base, 'components'), load(arm, 'components')
    print()
    print('%-28s %-7s %8s %8s | %8s %8s | %6s %6s | %s' % ('спектр', 'часть', 'χ²реш.б', 'χ²реш.п', 'χ²пуас.б', 'χ²пуас.п', 'ε%б', 'ε%п', 'состав: было → стало'))
    sums = {'known': [0.0, 0.0, 0.0, 0.0, 0], 'unknown': [0.0, 0.0, 0.0, 0.0, 0]}
    better = worse = same = 0
    for k in sorted(set(A) & set(B)):
        a, b = A[k], B[k]
        part = a.get('part')
        try:
            ca, cb = float(a['chi2ndf']), float(b['chi2ndf'])
            pa, pb = float(a['chi2ndf_pois']), float(b['chi2ndf_pois'])
            ea, eb = float(a.get('model_residual_pct') or 'nan'), float(b.get('model_residual_pct') or 'nan')
        except ValueError:
            print('%-28s %-7s ОШИБКА/нет чисел' % (k, part))
            continue
        s = sums.setdefault(part, [0.0, 0.0, 0.0, 0.0, 0])
        s[0] += ca; s[1] += cb; s[2] += pa; s[3] += pb; s[4] += 1
        na = sorted(r['component'] for r in CA.get(k, []))
        nb = sorted(r['component'] for r in CB.get(k, []))
        gone = [n for n in na if n not in nb]
        new = [n for n in nb if n not in na]
        comp = ('−' + ','.join(gone) if gone else '') + (' +' + ','.join(new) if new else '')
        if pb < pa - 1e-4:
            better += 1
        elif pb > pa + 1e-4:
            worse += 1
        else:
            same += 1
        print('%-28s %-7s %8.4f %8.4f | %8.4f %8.4f | %6.2f %6.2f | %s' % (k, part, ca, cb, pa, pb, ea, eb, comp or '='))
    print()
    for part, s in sorted(sums.items()):
        if s[4]:
            print('Σ %-8s n=%d: χ² решателя %.1f → %.1f; χ² пуассоновский (сравним) %.1f → %.1f' % (part, s[4], s[0], s[1], s[2], s[3]))
    print('по χ² пуассоновскому: лучше %d, хуже %d, равно %d' % (better, worse, same))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
