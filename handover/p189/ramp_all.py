# П189: правило A309 без границы ширины + логистика S(канал) по точкам 0.05<S<0.95, все спектры корпуса и фоны
import re, os, sys, glob, math
S = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
LEVEL, WIN, MINW, SCAN, FOOT, PERS, TAIL = 0.7, 3.0, 4, 20.0, 0.03, 0.5, 1 / 3
def blk(t, tag):
    i = t.find('<' + tag + '>')
    if i < 0: return None, None
    es = t[i:t.find('</' + tag + '>', i)]
    cal = [float(x) for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', es[es.find('<EnergyCalibration>'):es.find('</EnergyCalibration>')])]
    sp = es[es.find('<Spectrum>'):es.find('</Spectrum>')]
    return cal, [int(x) for x in re.findall(r'<DataPoint>(\d+)</DataPoint>', sp)]
def med(a):
    a = sorted(a); n = len(a)
    return a[n // 2] if n % 2 else 0.5 * (a[n // 2 - 1] + a[n // 2])
def rule(cal, d):
    E = lambda c: sum(a * c ** i for i, a in enumerate(cal))
    c0 = next((c for c in range(len(d)) if d[c] > 0 and E(c) > 0), -1)
    if c0 < 0: return None
    e0 = E(c0); pitch = E(c0 + 1) - e0
    win = max(MINW, math.ceil(WIN / pitch)) if pitch > 0 else MINW
    cand = -1
    for c in range(c0, len(d)):
        if E(c) - e0 > SCAN: break
        f, to = c + 1, min(len(d), c + 1 + win)
        if to - f < 2: break
        m = med(d[f:to])
        if d[c] >= LEVEL * m: cand, lvl = c, m; break
    r = dict(c0=c0, pitch=pitch, e0=e0)
    if cand < 0: r['st'] = 'неуст'; return r
    foot = c0
    while foot < cand and d[foot] < FOOT * lvl: foot += 1
    r.update(cand=cand, lvl=lvl, foot=foot, wkev=E(cand) - E(foot))
    pw = max(win, math.ceil(3 * WIN / pitch)) if pitch > 0 else win
    least = min(d[cand + 1:min(len(d), cand + 1 + pw)])
    if least < PERS * d[cand]: r['st'] = 'падение'; return r
    if cand == c0: r['st'] = 'жёсткий'; return r
    top = min(len(d) - 1, cand + round(TAIL * (cand - c0)))
    r['top'] = top
    r['st'] = 'рампа' if r['wkev'] <= 5.0 else 'шире'
    # логистика
    pts = []
    for c in range(c0, top + 1):
        s = d[c] / lvl
        if 0.05 < s < 0.95:
            wgt = d[c] * (1 - s)
            pts.append((c, math.log(s / (1 - s)), wgt))
    if len(pts) >= 2:
        W = sum(p[2] for p in pts); mx = sum(p[0] * p[2] for p in pts) / W; my = sum(p[1] * p[2] for p in pts) / W
        sxx = sum(p[2] * (p[0] - mx) ** 2 for p in pts)
        if sxx > 0:
            b = sum(p[2] * (p[0] - mx) * (p[1] - my) for p in pts) / sxx
            if b > 0:
                c50 = mx - my / b; w = 1 / b
                res = [d[c] / lvl - 1 / (1 + math.exp(-(c - c50) / w)) for c in range(c0, top + 1)]
                r.update(c50=c50, w=w, npt=len(pts), rms=math.sqrt(sum(x * x for x in res) / len(res)))
    r['prof'] = ' '.join('%.2f' % (d[c] / lvl) for c in range(max(0, c0 - 1), min(len(d), (r.get('top', c0 + 8)) + 3)))
    return r
def fmt(r):
    if r is None: return 'пусто'
    s = '%-7s c0=%d p=%.2f' % (r['st'], r['c0'], r['pitch'])
    if 'cand' in r: s += ' foot=%d cand=%d w=%.1fкэВ lvl=%.0f' % (r['foot'], r['cand'], r['wkev'], r['lvl'])
    if 'top' in r: s += ' top=%d' % r['top']
    if 'c50' in r: s += ' | c50=%.2f w=%.2f n=%d rms=%.3f' % (r['c50'], r['w'], r['npt'], r['rms'])
    return s
pat = sys.argv[1] if len(sys.argv) > 1 else '*'
for f in sorted(glob.glob(os.path.join(S, pat + '.xml'))):
    k = os.path.basename(f)[:-4]
    t = open(f, encoding='utf-8-sig').read()
    cal, d = blk(t, 'EnergySpectrum')
    bc, bd = blk(t, 'BackgroundEnergySpectrum')
    bn = re.search(r'<BackgroundSpectrumFile>([^<]*)<', t)
    ra = rule(cal, d)
    print('%-26s П %s' % (k, fmt(ra)))
    if bd:
        rb = rule(bc if bc else cal, bd)
        print('%-26s Ф %s  [%s]' % ('', fmt(rb), (bn.group(1) if bn else '?')[-30:]))
