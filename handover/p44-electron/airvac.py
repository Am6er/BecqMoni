# -*- coding: utf-8 -*-
import io, re, sys
sys.stdout.reconfigure(encoding='utf-8')
def read_g4(path):
    decays=None; h={}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m=re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m: decays=int(m.group(3))
        m=re.match(r'HIST (\d+) (\d+)', line)
        if m: h[int(m.group(1))]=int(m.group(2))
    return {k:v/decays for k,v in h.items()}, h, decays
def band(h,lo,hi): return sum(v for k,v in h.items() if lo<=k<hi)
root='C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/handover/p26-amber22/g4/'
for e in ('583.187','2614.511'):
    a,ac,da=read_g4(root+'g4_AS80_th_disk_%s_air.log'%e)
    v,vc,dv=read_g4(root+'g4_AS80_th_disk_%s_vacuum.log'%e)
    pk=int(round(float(e)))
    print('== %s: air/vacuum'%e)
    wa=sum(a.get(k,0) for k in range(pk-3,pk+1)); wv=sum(v.get(k,0) for k in range(pk-3,pk+1))
    print('  peak air/vac %+.2f %%  total %+.2f %%'%(100*(wa/wv-1),100*(sum(a.values())/sum(v.values())-1)))
    step=100 if pk>700 else 50; lo=0
    while lo<pk-3:
        hi=min(lo+step,pk-3)
        ba,bv,c=band(a,lo,hi),band(v,lo,hi),band(vc,lo,hi)
        print('  %4d-%-5d air/vac %+7.2f %%  vac share %.4f  noise %.2f %%'%(lo,hi,100*(ba/bv-1) if bv else 0,bv/sum(v.values()),100/c**0.5 if c else 0))
        lo=hi
