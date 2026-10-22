import re, sys, glob, os
import numpy as np
sys.path.insert(0, r"D:\BqMoni_Claude\max_uglass\py")
D = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra"
files = sorted(glob.glob(os.path.join(D, "*.xml"))) + sys.argv[1:]
def blocks(t):
    out = {}
    for tag in ("EnergySpectrum", "BackgroundEnergySpectrum"):
        m = re.search(r"<%s>(.*?)</%s>" % (tag, tag), t, re.S)
        if not m: continue
        b = m.group(1)
        co = re.search(r"<EnergyCalibration[^>]*>.*?<Coefficients>(.*?)</Coefficients>", b, re.S)
        if not co: out[tag] = None; continue
        c = [float(x) for x in re.findall(r"<Coefficient>([^<]*)</Coefficient>", co.group(1))]
        y = np.array([int(float(v)) for v in re.findall(r"<DataPoint>([^<]*)</DataPoint>", b)])
        out[tag] = (c, y)
    return out
def energy(c, n):
    ch = np.arange(n, dtype=float); return sum(c[i] * ch**i for i in range(len(c)))
def rule(y, E, r3):
    c0 = next((ch for ch in range(len(y)) if y[ch] > 0 and E[ch] > 0), -1)
    if c0 < 0: return None
    pitch = E[c0+1]-E[c0]
    window = max(4, int(np.ceil(3.0/pitch)))
    pw = max(window, int(np.ceil(9.0/pitch)))
    note = ''
    if r3:
        P = max(1.0, float(np.median(y[c0:c0+window])))
        cR = -1
        for c in range(c0+1, len(y)):
            if E[c] - E[c0] > 20.0: break
            if np.median(y[c:c+window]) >= P/0.03 and np.median(y[c:c+pw]) >= P/0.03:
                cR = c; break
        if cR > 0:
            last = max([c for c in range(c0, cR) if y[c] <= 3*P] or [c0-1])
            note = ' ped(P=%.0f, %0.2f->%0.2f)' % (P, E[c0], E[last+1])
            c0 = last+1
    e0 = E[c0]
    cand=-1
    for c in range(c0, len(y)):
        if E[c]-e0 > 20.0: break
        fr, to = c+1, min(len(y), c+1+window)
        if to-fr < 2: break
        med = float(np.median(y[fr:to]))
        if y[c] >= 0.7*med: cand=c; level=med; break
    if cand < 0: return ('nolevel', e0, note)
    foot=c0
    while foot<cand and y[foot] < 0.03*level: foot+=1
    if E[cand]-E[foot] > 5.0: return ('wide', e0, note)
    seg = y[cand+1:min(len(y), cand+1+pw)]
    if len(seg) and seg.min() < 0.5*y[cand]: return ('fall', e0, note)
    if cand == c0: return ('hard', e0, note)
    tail = int(round((cand-c0)/3.0)); top=min(len(y)-1, cand+tail)
    return ('ramp', E[top], note)
n=0
for f in files:
    t = open(f, encoding='utf-8-sig').read(); b = blocks(t); name = os.path.basename(f)[:-4]
    res = {}
    for tag in ("EnergySpectrum", "BackgroundEnergySpectrum"):
        if b.get(tag) is None: res[tag] = (None, None); continue
        c, y = b[tag]; E = energy(c, len(y)); res[tag] = (rule(y, E, False), rule(y, E, True))
    s, bg = res["EnergySpectrum"], res["BackgroundEnergySpectrum"]
    if s[0] is None: continue
    f0 = max(s[0][1], bg[0][1] if bg[0] else 0); f3 = max(s[1][1], bg[1][1] if bg[1] else 0)
    if abs(f0-f3) > 1e-9 or 'UG_' in name:
        n+=1
        print("%-26s floor %6.2f -> %6.2f | S %s %.2f -> %s %.2f%s | B %s" % (name, f0, f3, s[0][0], s[0][1], s[1][0], s[1][1], s[1][2],
              ('%s %.2f -> %s %.2f%s' % (bg[0][0], bg[0][1], bg[1][0], bg[1][1], bg[1][2])) if bg[0] else '-'))
print('changed', n)
