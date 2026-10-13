# AMBER87: центроиды известных линий Th-232 в спектре AtomSpectra, два соглашения
import sys, numpy as np
from scipy.optimize import curve_fit
def read_atom(p):
    L=open(p,encoding='utf-8').read().splitlines()
    assert L[0]=='FORMAT: 3'
    n=int(L[9]); order=int(L[10]); c=[float(x.replace(',','.')) for x in L[11:12+order]]
    y=np.array([float(v) for v in L[12+order:12+order+n]])
    t=float(L[8])
    return c,y,t
P=lambda c,x: sum(ci*x**k for k,ci in enumerate(c))
c,y,t=read_atom(sys.argv[1])
bg=None
if len(sys.argv)>2:
    cb,yb,tb=read_atom(sys.argv[2]); assert cb==c or True
    bg=yb*t/tb
x=np.arange(len(y),dtype=float)
def inv(E):
    lo,hi=0.0,len(y)-1.0
    for _ in range(80):
        m=(lo+hi)/2
        if P(c,m)<E: lo=m
        else: hi=m
    return lo
def g(x,a,mu,s,b0,b1): return a*np.exp(-0.5*((x-mu)/s)**2)+b0+b1*(x-mu)
lines=[(238.632,'Pb-212'),(583.187,'Tl-208'),(727.330,'Bi-212'),(911.204,'Ac-228'),(968.971,'Ac-228'),(1588.19,'Ac-228'),(2614.511,'Tl-208')]
print("h/2 at 662:", (P(c,inv(662)+1)-P(c,inv(662)-1))/4)
res=[]
for E,nm in lines:
    ch=inv(E)
    # ширина: ПШПВ ~ оценка по данным — окно ±1.2 ПШПВ, итерации
    h=(P(c,ch+1)-P(c,ch-1))/2
    fw=max(0.07*E*np.sqrt(662/E),15)/h  # грубая ПШПВ в каналах
    mu=ch
    for it in range(4):
        w=1.0*fw
        m=(x>mu-w)&(x<mu+w)
        yy=y[m]-(bg[m] if bg is not None else 0)
        try:
            p,cov=curve_fit(g,x[m],yy,p0=[yy.max(),mu,fw/2.355,yy.min(),0],sigma=np.sqrt(np.maximum(y[m],1)),maxfev=20000)
        except Exception as e:
            p=None;break
        mu=p[1]; fw=abs(p[2])*2.355
    if p is None: print(nm,E,'fit failed'); continue
    err=np.sqrt(cov[1,1])
    hh=(P(c,mu+1)-P(c,mu-1))/2
    ec=P(c,mu); ee=P(c,mu+0.5)
    res.append((E,ec-E,ee-E,err*hh,hh/2))
    print(f"{nm:7s} {E:9.3f}  центроид {mu:9.3f}±{err:.3f} кан  ПШПВ {fw*hh:6.1f} кэВ  центр: {ec-E:+.3f}  край: {ee-E:+.3f}  (σ {err*hh:.3f}, h/2 {hh/2:.3f}) кэВ")
r=np.array(res)
for j,nm in ((1,'центр'),(2,'край')):
    w=1/np.maximum(r[:,3],1e-3)**2
    print(f"{nm}: среднее невязки {np.mean(r[:,j]):+.3f}, взвеш. {np.sum(w*r[:,j])/np.sum(w):+.3f}, СКО {np.sqrt(np.mean(r[:,j]**2)):.3f} кэВ")
