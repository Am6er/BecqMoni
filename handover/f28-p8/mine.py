# -*- coding: utf-8 -*-
"""F28: печать строк сканера ТОЛЬКО по моей доле (для поимённого разбора)."""
import csv, sys, io, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from census import mine

src = sys.argv[1]
only = sys.argv[2] if len(sys.argv) > 2 else None
rows = list(csv.DictReader(io.open(src, encoding='utf-8'), delimiter='\t',
                           quoting=csv.QUOTE_NONE))
for r in rows:
    f = r['file'].replace(chr(92), '/')
    if not mine(f):
        continue
    if only and only.lower() not in f.lower():
        continue
    print('%s\t%s\t%s\t%s\t%s\t%s\t%s' % (f, r['side'], r['api'], r['kind'],
                                          r['line'], r['receiver'], r['args']))
