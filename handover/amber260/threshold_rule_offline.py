import re, glob, os, numpy as np
D = "C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/tools/CORPUS/corpus/spectra"
files = sorted(glob.glob(os.path.join(D, "*.xml"))) + ["D:/BqMoni_Claude/max_uglass/spec/UG_cube25mm_nano_spectrum.xml"]
LMIN = 1.0/(1-0.7)**2
def blocks(t):
    out = {}
    for tag in ("EnergySpectrum", "BackgroundEnergySpectrum"):
        m = re.search(r"<%s>(.*?)</%s>" % (tag, tag), t, re.S)
        if not m: continue
        b = m.group(1)
        co = re.search(r"<EnergyCalibration[^>]*>.*?<Coefficients>(.*?)</Coefficients>", b, re.S)
        if not co: continue
        c = [float(x) for x in re.findall(r"<Coefficient>([^<]*)</Coefficient>", co.group(1))]
        y = np.array([int(float(v)) for v in re.findall(r"<DataPoint>([^<]*)</DataPoint>", b)])
        out[tag] = (c, y)
    return out
def scan(y, E):
    c0 = next((k for k in range(len(y)) if y[k] > 0 and E[k] > 0), -1)
    if c0 < 0: return None
    e0=E[c0]; pitch=E[c0+1]-e0; w=max(4,int(np.ceil(3.0/pitch))); pw=max(w,int(np.ceil(9.0/pitch)))
    r = dict(c0=c0, e0=e0, w=w, pw=pw, cand=-1)
    for c in range(c0, len(y)):
        if E[c]-e0 > 20.0: break
        fr,to=c+1,min(len(y),c+1+w)
        if to-fr<2: break
        med=float(np.median(y[fr:to]))
        if y[c] >= 0.7*med: r['cand']=c; r['level']=med; break
    if r['cand']<0: return r
    cand=r['cand']; foot=c0
    while foot<cand and y[foot] < 0.03*r['level']: foot+=1
    r['width']=E[cand]-E[foot]
    seg=y[cand+1:min(len(y),cand+1+pw)]
    r['persist']= not (len(seg) and seg.min() < 0.5*y[cand])
    return r
def thr(y, E, r5):
    r = scan(y, E)
    if r is None: return ('none', 0.0)
    e0 = r['e0']
    fb = e0
    if r5:
        for c in range(r['c0'], len(y)):
            if E[c]-e0 > 20.0: break
            if np.median(y[c:c+r['w']]) >= LMIN: fb = E[c]; break
    if r['cand'] < 0: return ('nolevel', fb)
    if r5 and r['level'] < LMIN: return ('noise', fb)
    fb = e0
    if r['width'] > 5.0: return ('wide', fb)
    if not r['persist']: return ('fall', fb)
    if r['cand'] == r['c0']: return ('hard', e0)
    tail=int(round((r['cand']-r['c0'])/3.0)); top=min(len(y)-1, r['cand']+tail)
    return ('ramp', E[top])
n=0
for f in files:
    t=open(f,encoding='utf-8-sig').read(); b=blocks(t); name=os.path.basename(f)[:-4]
    if "EnergySpectrum" not in b: continue
    res={}
    for tag,(c,y) in b.items():
        ch=np.arange(len(y),dtype=float); E=sum(c[i]*ch**i for i in range(len(c)))
        res[tag]=(thr(y,E,False), thr(y,E,True))
    s=res["EnergySpectrum"]; bg=res.get("BackgroundEnergySpectrum")
    f0=max(s[0][1], bg[0][1] if bg else 0); f5=max(s[1][1], bg[1][1] if bg else 0)
    if abs(f0-f5)>1e-9 or 'UG_' in name:
        n+=1
        print('%-26s floor %6.2f -> %6.2f | S %s %.2f -> %s %.2f | B %s' % (name, f0, f5, s[0][0], s[0][1], s[1][0], s[1][1], ('%s %.2f -> %s %.2f' % (bg[0][0], bg[0][1], bg[1][0], bg[1][1])) if bg else '-'))
print('changed', n, 'LMIN', LMIN)
