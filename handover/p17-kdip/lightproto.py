# -*- coding: utf-8 -*-
# Прототип П17: кривая электронов Пейна с подкэвным продолжением (Joy-Luo) и
# фотонная кривая иода из каскада EADL (рентген поглощается на месте).
import math, random, sqlite3, sys
sys.stdout.reconfigure(encoding='utf-8')
DB = "C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/BecquerelMonitor/matdb.sqlite"
c = sqlite3.connect("file:%s?mode=ro" % DB, uri=True)
ONS = 36.4
eta0, trap, birks = 0.33, 28.25, 415.0
rows = c.execute("select energy_mev, collision_mev_cm2_g from estar_collision_stopping where material_star_id=252 order by energy_mev").fetchall()
rho = 3.667; I_ev = 452.0
e_kev = [r[0]*1000 for r in rows]; s_cm = [r[1]*rho for r in rows]
def interp_loglog(xs, ys, x):
    if x <= xs[0]: return ys[0]
    if x >= xs[-1]: return ys[-1]
    lo, hi = 0, len(xs)-1
    while hi-lo > 1:
        m=(lo+hi)//2
        if xs[m] <= x: lo=m
        else: hi=m
    t=(math.log(x)-math.log(xs[lo]))/(math.log(xs[hi])-math.log(xs[lo]))
    return math.exp(math.log(ys[lo])+t*(math.log(ys[hi])-math.log(ys[lo])))
def jl(e_ev):  # форма Joy-Luo, k=0.85
    return (1.0/e_ev)*math.log(1.166*(e_ev+0.85*I_ev)/I_ev)
# максимум формы JL
emax = max((jl(x), x) for x in [1.0*1.01**i for i in range(0,800)] if jl(x)>0)
print("JL max at %.1f eV, ratio to 1 keV %.3f" % (emax[1], emax[0]/jl(1000.0)))
def s_of(e_kev_, ext):
    if e_kev_ >= 1.0 or not ext: return interp_loglog(e_kev, s_cm, e_kev_)
    ev = e_kev_*1000.0
    f = jl(max(ev, emax[1]))
    return s_cm[0]*f/jl(1000.0)
def lyield(s, eta):
    inner=(s/ONS)*math.exp(-trap/s)
    return (1.0-eta*math.exp(-inner))/(1.0+s/birks)
def build(eta, ext):
    e_lo = 0.01 if ext else 1.0
    e_hi=3000.0
    steps=int(500*math.log10(e_hi/e_lo))
    grid=[e_lo*(e_hi/e_lo)**(i/float(steps)) for i in range(steps+1)]
    light=[lyield(s_of(e_lo,ext),eta)*e_lo]
    for a,b in zip(grid,grid[1:]):
        light.append(light[-1]+0.5*(lyield(s_of(a,ext),eta)+lyield(s_of(b,ext),eta))*(b-a))
    def rel(e): return interp_loglog(grid,light,e)/e
    norm=rel(661.657)
    return lambda e: rel(max(e,e_lo))/norm
cur0 = build(eta0, False); cur1 = build(eta0, True)
tab = dict(c.execute("select energy_kev, yield_rel from scint_electron_light_yield where material='NaI:Tl'").fetchall())
mx = max(abs(cur0(e)-y)/y for e,y in tab.items())
print("контроль: кривая в коде против таблицы, max отн. расх. %.2e" % mx)
print("%8s %8s %8s" % ("E кэВ","табл/без","с продолж."))
for e in [0.02,0.05,0.1,0.2,0.3,0.5,0.7,1.0,1.5,2,3,5,10,20,30,60,100,662]:
    print("%8.3f %8.4f %8.4f" % (e, cur0(e), cur1(e)))
# --- каскад EADL для иода (модуль)
Z=53
bind = dict(c.execute("select shell_id, binding_ev from eadl_binding where z=?", (Z,)).fetchall())
rad = {}; aug = {}
for v,f,p,en in c.execute("select vacancy_shell, from_shell, probability, energy_ev from eadl_radiative where z=?", (Z,)):
    rad.setdefault(v,[]).append((p,en,f))
for v,f,ej,p,en in c.execute("select vacancy_shell, from_shell, ejected_shell, probability, energy_ev from eadl_auger where z=?", (Z,)):
    aug.setdefault(v,[]).append((p,en,f,ej))
shells_sorted = sorted(bind.items(), key=lambda kv: -kv[1])
def absorb_shell(hv_ev):
    # самая глубокая открытая оболочка
    for sid,b in shells_sorted:
        if b < hv_ev: return sid
    return None
def cascade(shell, rng, pieces, first_rad=None, first_nonrad=False):
    """pieces: список энергий электронов (эВ). Возвращает ничего; фотоны — локально."""
    stack=[(shell, first_rad, first_nonrad)]
    while stack:
        s, fr, fnr = stack.pop()
        r = rad.get(s, []); a = aug.get(s, [])
        if not r and not a:
            pieces.append(bind.get(s,0.0)); continue
        sr = sum(p for p,_,_ in r); sa = sum(p for p,_,_,_ in a)
        if fr is not None:  # радиационный переход уже решён: from=fr
            for p,en,f in r:
                if f==fr: break
            # фотон локально
            hv=en; sh=absorb_shell(hv)
            if sh is None: pieces.append(hv)
            else:
                pieces.append(hv-bind[sh]); stack.append((sh,None,False))
            stack.append((fr,None,False)); continue
        u = rng.random()*( (sa) if fnr else (sr+sa) )
        if not fnr and u < sr:
            for p,en,f in r:
                u-=p
                if u<0: break
            hv=en; sh=absorb_shell(hv)
            if sh is None: pieces.append(hv)
            else:
                pieces.append(hv-bind[sh]); stack.append((sh,None,False))
            stack.append((f,None,False))
        else:
            if not fnr: u-=sr
            for p,en,f,ej in a:
                u-=p
                if u<0: break
            pieces.append(en); stack.append((f,None,False)); stack.append((ej,None,False))
omegaK = sum(p for p,_,_ in rad[1]); print("omega_K EADL %.4f" % omegaK)
def photon_light(E_kev, curve, split, rng, n=4000):
    """Средний свет/E поглощённого фотона в иоде: K-доля 0.85 выше края; ниже — L (L1/L2/L3 по 0.15/0.3/0.4) и M 0.15."""
    E=E_kev*1000.0
    tot=0.0
    for _ in range(n):
        u=rng.random()
        if E>bind[1] and u<0.85: sh=1
        else:
            u2=rng.random()
            sh = 3 if u2<0.18 else (5 if u2<0.48 else (6 if u2<0.87 else 8))
            if E<=bind[sh]: sh=8
        pe=E-bind[sh]
        pieces=[]
        if split:
            pieces.append(pe); cascade(sh, rng, pieces)
        else:
            # как сейчас: рентген (K или L по ω) уходит квантом и поглощается на месте (→ один электрон), остальное одним куском
            r=rad.get(sh,[]); sr=sum(p for p,_,_ in r)
            if rng.random()<sr:
                u=rng.random()*sr
                for p,en,f in r:
                    u-=p
                    if u<0: break
                pieces.append(E-en); pieces.append(en)   # квант поглощается: один электрон hv (как в модели без раздельного каскада)
            else:
                pieces.append(E)
        tot += sum(x*curve(x/1000.0) for x in pieces)/E
    return tot/n

if __name__=='__main__':
    rng=random.Random(1)
    
    print("\n%8s | %9s %9s %9s %9s" % ("E кэВ","табл,кусок","табл,кас","прод,кусок","прод,кас"))
    for e in [10,20,30,32,33,33.5,34,34.5,35,36,40,50,60,81,100,122,200,356,662]:
        a=photon_light(e,cur0,False,rng); b=photon_light(e,cur0,True,rng); c1=photon_light(e,cur1,False,rng); d=photon_light(e,cur1,True,rng)
        print("%8.1f | %9.4f %9.4f %9.4f %9.4f" % (e,a,b,c1,d))
