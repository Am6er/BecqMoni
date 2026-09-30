# П189: логистика S(канал) с уточнением уровня выше верха рампы (2 прохода), по фонам и континууму
import re, os, sys, glob, math
sys.path.insert(0, r'D:\BqMoni_Claude\p189')
from ramp_all import blk, med, rule, S
def logfit(d, c0, hi, lvl):
    pts = []
    for c in range(c0, hi + 1):
        s = d[c] / lvl
        if 0.05 < s < 0.95:
            pts.append((c, math.log(s / (1 - s)), d[c] * (1 - s) ** 2))
    if len(pts) < 3: return None
    W = sum(p[2] for p in pts); mx = sum(p[0] * p[2] for p in pts) / W; my = sum(p[1] * p[2] for p in pts) / W
    sxx = sum(p[2] * (p[0] - mx) ** 2 for p in pts)
    b = sum(p[2] * (p[0] - mx) * (p[1] - my) for p in pts) / sxx
    if b <= 0: return None
    return mx - my / b, 1 / b, len(pts)
def fit2(cal, d, passes=3):
    r = rule(cal, d)
    if r is None or r.get('st') != 'шире': return r, None
    c0, top, lvl = r['c0'], r['top'], r['lvl']
    pitch = r['pitch']; win = max(4, math.ceil(3.0 / pitch))
    out = []
    f = logfit(d, c0, top, lvl)
    out.append((f, lvl))
    for _ in range(passes):
        if f is None: break
        t1 = int(math.ceil(f[0] + f[1] * math.log(99)))
        lvl = med(d[t1:t1 + win])
        f = logfit(d, c0, max(top, t1), lvl)
        out.append((f, lvl))
    return r, out
for f in sorted(glob.glob(os.path.join(S, (sys.argv[1] if len(sys.argv) > 1 else '*') + '.xml'))):
    k = os.path.basename(f)[:-4]
    t = open(f, encoding='utf-8-sig').read()
    for tag, lab in (('EnergySpectrum', 'П'), ('BackgroundEnergySpectrum', 'Ф')):
        cal, d = blk(t, tag)
        if not d: continue
        if lab == 'Ф' and not cal: cal = blk(t, 'EnergySpectrum')[0]
        r, out = fit2(cal, d)
        if not out: continue
        s = ' '.join('[%s]' % ('c50=%.2f w=%.2f n=%d L=%.0f' % (o[0][0], o[0][1], o[0][2], o[1]) if o[0] else '-') for o in out)
        print('%-26s %s %s' % (k, lab, s))
