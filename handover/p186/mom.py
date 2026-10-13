# П186: моменты данных (fit) и модели (model) в окнах по кривым разбора (--dump-curves)
# python mom.py <каталог_кривых> спектр:lo:hi [...]
import csv, sys, os, math
d = sys.argv[1]
for arg in sys.argv[2:]:
    s, lo, hi = arg.split(':'); lo = float(lo); hi = float(hi)
    p = os.path.join(d, s + '_curves.csv')
    if not os.path.exists(p):
        print(s, 'нет'); continue
    rows = [r for r in csv.DictReader(open(p)) if lo <= float(r['keV']) <= hi]
    def mom(col):
        w = [(float(r['keV']), float(r[col])) for r in rows]
        n = sum(v for _, v in w)
        m = sum(e * v for e, v in w) / n
        var = sum((e - m) ** 2 * v for e, v in w) / n
        return n, m, math.sqrt(var)
    nd, md, sd = mom('fit'); nm, mm, sm = mom('model')
    print('%-18s %6.1f-%6.1f  n/m %.4f  центр данные %.3f модель %.3f Δ %+.3f кэВ (%+.2f %%)  σ данные %.3f модель %.3f  σд/σм %.3f'
          % (s, lo, hi, nd / nm, md, mm, md - mm, 100 * (md - mm) / mm, sd, sm, sd / sm))
