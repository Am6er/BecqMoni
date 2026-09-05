# -*- coding: utf-8 -*-
"""F34: печатает исходную строку каждого остатка сканера в моих файлах."""
import csv, io, sys, collections, os

ROOT = 'C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/'
MINE = set(sys.argv[2].split(','))

rows = []
with io.open(sys.argv[1], encoding='utf-8', newline='') as f:
    for r in csv.DictReader(f, delimiter='\t', quoting=csv.QUOTE_NONE):
        r['file'] = r['file'].replace('\\', '/')
        if any(r['file'].endswith('/' + m) for m in MINE):
            rows.append(r)

cache = {}
by = collections.OrderedDict()
for r in rows:
    by.setdefault(r['file'], []).append(r)

for fn, rs in by.items():
    print('===== %s =====' % fn)
    if fn not in cache:
        cache[fn] = io.open(ROOT + fn, encoding='utf-8-sig', newline='').read().split('\n')
    src = cache[fn]
    seen = set()
    for r in sorted(rs, key=lambda x: int(x['line'])):
        ln = int(r['line'])
        if ln in seen:
            continue
        seen.add(ln)
        apis = ','.join(sorted(set(x['api'] + '/' + x['receiver'] for x in rs if int(x['line']) == ln)))
        print('  %5d [%s]  %s' % (ln, apis, src[ln - 1].strip()[:220]))
    print()
