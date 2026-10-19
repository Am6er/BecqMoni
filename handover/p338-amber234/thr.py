import sys, re
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
G = 'D:/BqMoni_Claude/p338/g4/'
def csv(fn):
    hdr = {}; c = []
    for line in open(G + fn, encoding='utf-8'):
        if line.startswith('#'):
            k, _, v = line[1:].partition(':'); hdr.setdefault(k.strip(), []).append(v.strip()); continue
        if line.startswith('channel'): continue
        c.append(float(line.split(',')[1]))
    return hdr, np.array(c)
def coeffs(s): return [float(x) for x in re.search(r'coeffs:\s*([-0-9.eE+ ]+)', s).group(1).split()[:5]]
def E(cf, ch): return sum(c * ch ** i for i, c in enumerate(cf))
for fn in ('th232_reference_marinelli_1l.csv', 'bg_marinelli_water_1l_33.2h.csv', 'berry_dried_marinelli_1l_482g_49.8h.csv', 'berry_dried_marinelli_1l_482g_20.2h.csv'):
    h, y = csv(fn); y[-1] = 0
    nz = np.flatnonzero(y > 0)[0]; mx = y[:200].max(); half = np.flatnonzero(y[:200] > 0.5 * np.convolve(y, np.ones(9)/9, 'same')[30:200].max())[0]
    sc = [(k, coeffs(v)) for k, vs in h.items() if k.startswith("page_scale_") and "note" not in k for v in vs if "coeffs" in v]
    print(fn, 'первый ненулевой канал', nz, '| первый канал выше половины плато', half)
    for k, cf in sc: print('    ', k, ' E(первый)=%.2f  E(полувыс.)=%.2f  E(ch100)=%.2f  E(ch200)=%.2f' % (E(cf, nz), E(cf, half), E(cf, 100), E(cf, 200)))
    # peaks: positions of high-energy anchors by channel to compare gain
print()
print('канал, в который шкала страницы кладёт энергию E; отношение к фону = «усиление», должно быть постоянным')
def chan(cf, e0):
    ch = np.arange(0, 8192, 0.01); ee = E(cf, ch); return float(np.interp(e0, ee, ch))
_, _ = None, None
hs = {fn: csv(fn)[0] for fn in ('th232_reference_marinelli_1l.csv', 'bg_marinelli_water_1l_33.2h.csv', 'berry_dried_marinelli_1l_482g_49.8h.csv')}
sth = coeffs(hs['th232_reference_marinelli_1l.csv']['page_scale_1'][0])
sb1 = coeffs(hs['bg_marinelli_water_1l_33.2h.csv']['page_scale_1'][0]); sb2 = coeffs(hs['bg_marinelli_water_1l_33.2h.csv']['page_scale_2'][0])
sbe = coeffs(hs['berry_dried_marinelli_1l_482g_49.8h.csv']['page_scale_1'][0])
print('   E кэВ | Th/фон(шк1) | черника/фон(шк2) | фон шк1/шк2')
for e0 in (20, 30, 40, 50, 60, 80, 100, 150, 238.6, 351.9, 609.3, 911.2, 1460.8, 2614.5):
    a, b, c, d = chan(sth, e0), chan(sb1, e0), chan(sbe, e0), chan(sb2, e0)
    print(f'  {e0:7.1f} | {a/b:.4f} ({a:7.1f}/{b:7.1f}) | {c/d:.4f} ({c:7.1f}/{d:7.1f}) | {b/d:.4f}')
