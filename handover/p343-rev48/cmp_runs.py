# -*- coding: utf-8 -*-
"""П302: посиектральное сравнение двух каталогов прогона корпуса (rev46 против rev47).

Ключ — спектр. Сравнивает *_spline_runs.csv (все поля, кроме ms/cpu_ms) и прочие
*_spline_*.csv построчно по спектру. Печатает: какие спектры сдвинулись, chi2ndf
и chi2ndf_pois до/после, и какие файлы разошлись вне сдвинутых спектров.
"""
import csv, glob, io, os, sys

A, B = sys.argv[1], sys.argv[2]
SKIP = set(sys.argv[3].split(',')) if len(sys.argv) > 3 else {'ms', 'cpu_ms'}


def load_runs(d):
    rows = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as fh:
            for r in csv.DictReader(fh):
                rows[r['spectrum']] = r
    return rows


def load_other(d):
    """{(тип файла, спектр): [строки]} для прочих csv по спектрам."""
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_*.csv')):
        kind = p.rsplit('_spline_', 1)[1]
        if kind == 'runs.csv':
            continue
        with io.open(p, encoding='utf-8-sig', newline='') as fh:
            rd = csv.reader(fh)
            hdr = next(rd, None)
            if not hdr:
                continue
            key = hdr.index('spectrum') if 'spectrum' in hdr else None
            for row in rd:
                s = row[key] if key is not None and key < len(row) else '?'
                out.setdefault((kind, s), []).append(tuple(row))
    return out


ra, rb = load_runs(A), load_runs(B)
print('спектров: %d / %d' % (len(ra), len(rb)))
moved = []
for s in sorted(set(ra) | set(rb)):
    a, b = ra.get(s), rb.get(s)
    if a is None or b is None:
        print('  НЕТ ПАРЫ', s, a is None, b is None)
        moved.append(s)
        continue
    diff = [k for k in sorted(set(a) | set(b)) if k not in SKIP and a.get(k) != b.get(k)]
    if diff:
        moved.append(s)
        print('СДВИГ %-28s %-7s chi2ndf %s -> %s ; pois %s -> %s ; поля: %s'
              % (s, a.get('part'), a.get('chi2ndf'), b.get('chi2ndf'),
                 a.get('chi2ndf_pois'), b.get('chi2ndf_pois'), ', '.join(diff)))
print('сдвинулись в runs: %d' % len(moved))

oa, ob = load_other(A), load_other(B)
other = {}
for k in set(oa) | set(ob):
    if sorted(oa.get(k, [])) != sorted(ob.get(k, [])):
        other.setdefault(k[1], set()).add(k[0])
print('расхождения прочих файлов по спектрам: %d' % len(other))
for s in sorted(other):
    tag = '' if s in moved else '   <-- ВНЕ сдвинутых по runs'
    print('  %-28s %s%s' % (s, ', '.join(sorted(other[s])), tag))

# файлы целиком
fa = set(os.path.basename(p) for p in glob.glob(os.path.join(A, '*')))
fb = set(os.path.basename(p) for p in glob.glob(os.path.join(B, '*')))
print('файлы только в A:', sorted(fa - fb))
print('файлы только в B:', sorted(fb - fa))
