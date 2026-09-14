# -*- coding: utf-8 -*-
# Скан: фактор обрыва конца трека q(E')=1/(1+(Eq/E')^p) поверх Пейна+JL; глубина K-провала и r(40)/r(122)
import math, random, sys
sys.stdout.reconfigure(encoding='utf-8')
from lightproto import *
def build_q(eta, eq, p):
    e_lo=0.01; e_hi=3000.0
    steps=int(500*math.log10(e_hi/e_lo))
    grid=[e_lo*(e_hi/e_lo)**(i/float(steps)) for i in range(steps+1)]
    def l(e):
        q = 1.0/(1.0+(eq/e)**p) if eq>0 else 1.0
        return lyield(s_of(e,True),eta)*q
    light=[l(e_lo)*e_lo]
    for a,b in zip(grid,grid[1:]):
        light.append(light[-1]+0.5*(l(a)+l(b))*(b-a))
    def rel(e): return interp_loglog(grid,light,e)/e
    norm=rel(661.657)
    return lambda e: rel(max(e,e_lo))/norm

def scan(eta,eq,p,n=3000):
    cur=build_q(eta,eq,p); rng=random.Random(7)
    r={e:photon_light(e,cur,True,rng,n) for e in [10,20,33.0,34.5,40,50,81,100,122,200,662]}
    trend=r[20]+(r[50]-r[20])*(14.5/30.0)
    return cur,r,(r[34.5]/trend-1)*100,(r[34.5]/r[33.0]-1)*100, r[40]/r[122]
print("Eq p | y(0.3) y(1) y(3) y(10) | 10/662 20 33 34.5 50 100 122 | провал к тренду % | ступень 33->34.5 % | r40/r122")
for eq,p in [(0,2),(0.3,2),(0.5,2),(0.7,2),(1.0,2),(1.5,2),(0.7,1),(0.7,3),(1.0,3)]:
    cur,r,dip,step,ratio=scan(0.33,eq,p)
    print("%.1f %d | %.3f %.3f %.3f %.3f | %.4f %.4f %.4f %.4f %.4f %.4f %.4f | %+.2f | %+.2f | %.4f" % (eq,p,cur(0.3),cur(1),cur(3),cur(10), r[10]/r[662],r[20]/r[662],r[33.0]/r[662],r[34.5]/r[662],r[50]/r[662],r[100]/r[662],r[122]/r[662],dip,step,ratio))
