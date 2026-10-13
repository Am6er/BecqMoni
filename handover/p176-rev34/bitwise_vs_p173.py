# -*- coding: utf-8 -*-
r"""П176: побитовая сверка прогонов основного дерева (tools\pie\out_rev34_*) с базой П173 (worktree, out_rev34c_*).

Сверяет (1) Σχ²/ndf частей known/unknown по *_spline_runs.csv и chi2ndf каждого спектра; (2) каждый файл выхода
построчно, игнорируя графы времени (ms, cpu_ms). Печатает число файлов побитово равных / разошедшихся только
временем / разошедшихся содержательно. Разделитель дробной части — точка.
"""
import csv
import glob
import io
import os
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
PAIRS = [(os.path.join(ROOT, 'tools', 'pie', 'out_rev34_full'), r'D:\BqMoni_Claude\p147\wt\tools\pie\out_rev34c_full'),
         (os.path.join(ROOT, 'tools', 'pie', 'out_rev34_mini'), r'D:\BqMoni_Claude\p147\wt\tools\pie\out_rev34c_mini')]
TIME = {'ms', 'cpu_ms'}


def runs(d):
    r = {}
    for f in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        with io.open(f, encoding='utf-8-sig', newline='') as fh:
            for row in csv.DictReader(fh):
                r[row['spectrum']] = (row.get('part'), row.get('chi2ndf'))
    return r


def rows(path):
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        return list(csv.reader(fh))


def strip_time(tab):
    if not tab:
        return tab
    hdr = tab[0]
    keep = [i for i, h in enumerate(hdr) if h not in TIME]
    return [[r[i] if i < len(r) else '' for i in keep] for r in tab]


bad_total = 0
for a, b in PAIRS:
    print(u'== %s\n   против %s' % (a, b))
    ra, rb = runs(a), runs(b)
    for part in ('known', 'unknown'):
        ka = {k: v[1] for k, v in ra.items() if v[0] == part}
        kb = {k: v[1] for k, v in rb.items() if v[0] == part}
        common = sorted(set(ka) & set(kb))
        diff = [k for k in common if ka[k] != kb[k]]
        def s(m):
            t = 0.0
            for k in common:
                try:
                    t += float(m[k])
                except (TypeError, ValueError):
                    pass
            return t
        print(u'   %-7s спектров %d / %d, общих %d, chi2ndf разошлось %d; Σ %.4f против %.4f'
              % (part, len(ka), len(kb), len(common), len(diff), s(ka), s(kb)))
        bad_total += len(diff) + len(set(ka) ^ set(kb))
    fa = sorted(os.path.basename(f) for f in glob.glob(os.path.join(a, '*')) if os.path.isfile(f))
    fb = sorted(os.path.basename(f) for f in glob.glob(os.path.join(b, '*')) if os.path.isfile(f))
    only = sorted(set(fa) ^ set(fb))
    same = timeonly = content = 0
    content_files = []
    for f in sorted(set(fa) & set(fb)):
        pa, pb = os.path.join(a, f), os.path.join(b, f)
        if open(pa, 'rb').read() == open(pb, 'rb').read():
            same += 1
            continue
        if f.endswith('.csv') and strip_time(rows(pa)) == strip_time(rows(pb)):
            timeonly += 1
            continue
        content += 1
        content_files.append(f)
    print(u'   файлов: общих %d — побитово %d, только время %d, содержательно %d; только в одном: %s'
          % (len(set(fa) & set(fb)), same, timeonly, content, only))
    if content_files:
        print(u'   содержательно разошлись: %s' % content_files)
    bad_total += len([f for f in content_files if not f.startswith('.run')])
print(u'ИТОГ: %s' % (u'ПОБИТОВО (без граф времени и клейма .run.json)' if bad_total == 0 else u'⛔ РАСХОЖДЕНИЙ %d' % bad_total))
sys.exit(0 if bad_total == 0 else 1)
