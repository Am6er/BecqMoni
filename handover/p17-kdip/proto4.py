# -*- coding: utf-8 -*-
# q по НАЧАЛЬНОЙ энергии трека: y(E) = yPayneJL(E)/(1+(Eq/E)^2); слияние подкэвных кусков в сгусток
import math, random, sys
sys.stdout.reconfigure(encoding='utf-8')
from lightproto import *
def build_iso(eta, eq, p=2):
    base = build(eta, True)
    return lambda e: base(e)/(1.0+(eq/e)**p) if eq>0 else base(e)
def photon_light_merge(E_kev, curve, rng, n, emerge=1000.0):
    E=E_kev*1000.0; tot=0.0
    for _ in range(n):
        u=rng.random()
        if E>bind[1] and u<0.85: sh=1
        else:
            u2=rng.random()
            sh = 3 if u2<0.18 else (5 if u2<0.48 else (6 if u2<0.87 else 8))
            if E<=bind[sh]: sh=8
        pieces=[E-bind[sh]]; cascade(sh, rng, pieces)
        big=[x for x in pieces if x>=emerge]; blob=sum(x for x in pieces if x<emerge)
        if blob>0: big.append(blob)
        tot += sum(x*curve(x/1000.0) for x in big)/E
    return tot/n
ES=[10,20,30,33.0,34.5,40,50,60,81,100,122,200,356,662]
def scan(eta,eq,p=2,n=4000):
    cur=build_iso(eta,eq,p); rng=random.Random(7)
    r={e:photon_light_merge(e,cur,rng,n) for e in ES}
    trend=r[20]+(r[50]-r[20])*(14.5/30.0)
    return cur,r,(r[34.5]/trend-1)*100,(r[34.5]/r[33.0]-1)*100, r[10]/r[20]
kh={10:1.12,20:1.172,34.5:1.141,50:1.158,100:1.112}
print("eta Eq | y(0.5) y(1) y(2) y(3) y(5) y(10) | 10 20 30 33 34.5 40 50 60 81 100 122 200 356 (к фотону 662) | провал% | ступень% | 10/20")
for eta,eq in [(0.33,0),(0.33,0.5),(0.33,0.7),(0.33,1.0),(0.33,1.5),(0.40,0.7),(0.45,0.7),(0.45,1.0),(0.50,1.0)]:
    cur,r,dip,step,r1020=scan(eta,eq)
    ph=[r[e]/r[662] for e in ES[:-1]]
    print("%.2f %.1f | %.3f %.3f %.3f %.3f %.3f %.3f | %s | %+.2f | %+.2f | %.3f" % (eta,eq,cur(0.5),cur(1),cur(2),cur(3),cur(5),cur(10)," ".join("%.4f"%x for x in ph),dip,step,r1020))
print("Ходюк:", kh)
