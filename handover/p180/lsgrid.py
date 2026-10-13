# П180 S198: форма K-провала NaI у сетки (E_q, p) против оцифровки рис. 6 Ходюка; к 33.0 кэВ, %
import glob, re, os, sys
kh = {}
for line in open(r'D:\BqMoni_Claude\p180\lit\khodyuk_fig6_digitized.txt', encoding='utf-8'):
    if line.startswith('#'): continue
    e, v, _ = line.split(); kh[float(e)] = float(v)
ref = kh[33.0]
khr = {e: 100 * (v / ref - 1) for e, v in kh.items()}
cmp_e = [33.2, 33.5, 34.0, 34.5, 35.0, 35.5, 36.0, 36.5, 37.0, 38.0, 40.0]
print('набор'.ljust(14) + ''.join(('%.1f' % e).rjust(7) for e in cmp_e) + '   СКО    10/20  50/33   100/33')
print('Ходюк'.ljust(14) + ''.join(('%+.2f' % khr[e]).rjust(7) for e in cmp_e) + '          0.956  %.4f' % (115.8 / ref))
for f in sorted(glob.glob(r'D:\BqMoni_Claude\p180\ls\g_*.txt')):
    d = {}
    for line in open(f, encoding='utf-8'):
        m = re.match(r'\s+([\d.]+)\s+([\d.]+)\s+', line)
        if m: d[float(m.group(1))] = float(m.group(2))
    if 40.0 not in d or 33.0 not in d: continue
    r = {e: 100 * (d[e] / d[33.0] - 1) for e in d}
    sse = (sum((r[e] - khr[e]) ** 2 for e in cmp_e) / len(cmp_e)) ** 0.5
    name = os.path.basename(f)[2:-4]
    print(name.ljust(14) + ''.join(('%+.2f' % r[e]).rjust(7) for e in cmp_e) + '  %5.2f  %6.3f  %.4f  %.4f' % (sse, d.get(10, 0) / d.get(20, 1), d[50] / d[33.0], d.get(100, 0) / d[33.0]))
