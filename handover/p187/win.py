# П187: отношение данные/модель (net/model) по окнам энергии из кривых разбора (--dump-curves)
# python win.py <каталог_кривых> спектр:lo:hi[:lo:hi...] ...
import csv, sys, os
d = sys.argv[1]
for arg in sys.argv[2:]:
    a = arg.split(':'); s = a[0]
    p = os.path.join(d, s + '_curves.csv')
    if not os.path.exists(p):
        print(s, 'нет'); continue
    rows = list(csv.DictReader(open(p)))
    out = []
    for i in range(1, len(a), 2):
        lo, hi = float(a[i]), float(a[i + 1])
        n = sum(float(r['net']) for r in rows if lo <= float(r['keV']) <= hi)
        m = sum(float(r['model']) for r in rows if lo <= float(r['keV']) <= hi)
        out.append('%5.0f-%-5.0f n=%9.0f d/m %.3f' % (lo, hi, n, n / m if m else float('nan')))
    print('%-20s ' % s + ' | '.join(out))
