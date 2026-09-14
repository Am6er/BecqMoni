# -*- coding: utf-8 -*-
# П8: сверка двух каталогов прогона строка в строку; у runs.csv столбцы времени (ms, cpu_ms) маскируются.
import csv, io, os, sys
a, b = sys.argv[1], sys.argv[2]
mask = set(sys.argv[3].split(',')) if len(sys.argv) > 3 else {'ms', 'cpu_ms'}
tot = 0; dif = 0; cols = {}
for name in sorted(os.listdir(a)):
    pa, pb = os.path.join(a, name), os.path.join(b, name)
    if not os.path.exists(pb):
        print('MISSING', name); continue
    ra = list(csv.reader(io.open(pa, encoding='utf-8-sig', newline='')))
    rb = list(csv.reader(io.open(pb, encoding='utf-8-sig', newline='')))
    hdr = ra[0]
    d = 0
    for i in range(max(len(ra), len(rb))):
        xa = ra[i] if i < len(ra) else None
        xb = rb[i] if i < len(rb) else None
        if xa is None or xb is None:
            d += 1; continue
        if name.endswith('_runs.csv') and i > 0:
            for k, h in enumerate(hdr):
                if h in mask and k < len(xa) and k < len(xb):
                    xa = list(xa); xb = list(xb); xa[k] = xb[k] = '*'
        if xa != xb:
            d += 1
            for k in range(max(len(xa), len(xb))):
                va = xa[k] if k < len(xa) else None; vb = xb[k] if k < len(xb) else None
                if va != vb:
                    h = hdr[k] if k < len(hdr) else str(k)
                    cols[(name, h)] = cols.get((name, h), 0) + 1
    tot += len(ra); dif += d
    print('%-34s строк %4d / %4d  расхождений %d' % (name, len(ra), len(rb), d))
print('ИТОГО строк %d, расхождений %d (маска: %s)' % (tot, dif, ','.join(sorted(mask))))
for k, v in sorted(cols.items()):
    print('  столбец %s / %s: %d строк' % (k[0], k[1], v))
