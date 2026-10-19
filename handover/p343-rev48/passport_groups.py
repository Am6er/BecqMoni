# -*- coding: utf-8 -*-
"""П343: мерка паспорта по группам (прибор + вид сцены) из csv `passport.py --csv=`.

python passport_groups.py <csv> [<csv> ...]   -> медиана и «вне порога» по (det, group), и строки G1S16
"""
import csv
import io
import statistics
import sys

for _s in (sys.stdout,):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass

for path in sys.argv[1:]:
    rows = list(csv.DictReader(io.open(path, encoding='utf-8-sig', newline='')))
    groups = {}
    for r in rows:
        groups.setdefault((r['det'], r['group']), []).append(r)
    total_out = sum(int(r['out']) for r in rows)
    print('== %s: строк %d, вне порога %d' % (path, len(rows), total_out))
    for key in sorted(groups):
        g = groups[key]
        ratios = [float(r['ratio']) for r in g]
        out = [r['spectrum'] + ' ' + r['nuclide'] + ' ' + ('%.3f' % float(r['ratio'])) for r in g if r['out'] == '1']
        print('  %-8s %-16s n %2d медиана %.3f  вне %d  %s' % (key[0], key[1], len(g), statistics.median(ratios),
                                                            len(out), '; '.join(out)))
    print('  -- G1S16, сосуды:')
    for r in rows:
        if r['det'] == 'G1S16' and ('марин' in r['group'] or 'цилинд' in r['group']):
            print('     %-24s %-8s %-12s %.3f  вне=%s' % (r['spectrum'], r['nuclide'], r['group'], float(r['ratio']), r['out']))
