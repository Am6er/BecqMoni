# П187: повтор правила FsaBand.AdcThresholdOf (A309) на питоне по всем спектрам корпуса и их фонам,
# с границей ширины рампы RMAX (кэВ) — сколько спектров меняют пол при её смене
import re, os, sys, glob
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
def rule(cal, d, rmax):
    E = lambda c: sum(a * c ** i for i, a in enumerate(cal))
    c0 = next((c for c in range(len(d)) if d[c] > 0 and E(c) > 0), -1)
    if c0 < 0: return 0, 'пусто'
    e0 = E(c0); pitch = E(c0 + 1) - e0
    win = max(MINW, int(-(-WIN // pitch))) if pitch > 0 else MINW
    import math
    win = max(MINW, math.ceil(WIN / pitch)) if pitch > 0 else MINW
    cand = -1
    for c in range(c0, len(d)):
        if E(c) - e0 > SCAN: break
        f, to = c + 1, min(len(d), c + 1 + win)
        if to - f < 2: break
        m = med(d[f:to])
        if d[c] >= LEVEL * m: cand, lvl = c, m; break
    if cand < 0: return e0, 'не устоялся'
    foot = c0
    while foot < cand and d[foot] < FOOT * lvl: foot += 1
    if E(cand) - E(foot) > rmax: return e0, 'шире %.1f' % (E(cand) - E(foot))
    pw = max(win, math.ceil(3 * WIN / pitch)) if pitch > 0 else win
    least = min(d[cand + 1:min(len(d), cand + 1 + pw)])
    if least < PERS * d[cand]: return e0, 'падение'
    if cand == c0: return e0, 'жёсткий'
    top = min(len(d) - 1, cand + round(TAIL * (cand - c0)))
    return E(top), 'рампа ch%d→%d' % (c0, top)
rmax = float(sys.argv[1])
for f in sorted(glob.glob(os.path.join(S, '*.xml'))):
    k = os.path.basename(f)[:-4]
    t = open(f, encoding='utf-8-sig').read()
    cal, d = blk(t, 'EnergySpectrum')
    a5, h5 = rule(cal, d, 5.0); a, h = rule(cal, d, rmax)
    bc, bd = blk(t, 'BackgroundEnergySpectrum')
    b5 = b = 0; bh5 = bh = '-'
    if bd:
        bc = bc if bc else cal
        b5, bh5 = rule(bc, bd, 5.0); b, bh = rule(bc, bd, rmax)
    p5, p = max(a5, b5), max(a, b)
    if abs(p - p5) > 1e-9 or '-v' in sys.argv:
        print('%-26s пол %6.2f -> %6.2f кэВ | проба [%s]->[%s] фон [%s]->[%s]' % (k, p5, p, h5, h, bh5, bh))
