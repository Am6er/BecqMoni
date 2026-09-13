# -*- coding: utf-8 -*-
import io, re, sys, os
sys.stdout.reconfigure(encoding='utf-8')
R='C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/handover/'
def read_g4(path):
    decays=None; h={}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m=re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m: decays=int(m.group(3))
        m=re.match(r'HIST (\d+) (\d+)', line)
        if m: h[int(m.group(1))]=int(m.group(2))
    return {k:v/decays for k,v in h.items()}, decays
def read_ours(path):
    h={}
    for line in io.open(path, encoding='utf-8-sig'):
        p=line.strip().split(',')
        if len(p)!=2 or p[0]=='keV': continue
        h[int(round(float(p[0])))]=float(p[1])
    return h
def band(h,lo,hi): return sum(v for k,v in h.items() if lo<=k<hi)
g4d,_=read_g4(R+'p26-amber22/g4/g4_AS80_th_disk_2614.511_air.log')
ours=read_ours(R+'p42-amber22/g4/ours17_AS80_th_disk_2614.511.csv')
g4b,_=read_g4(R+'p27-electron-transport/g4/g4_AS80_bare_gap5_2614.511.log')
tg=sum(g4d.values()); to=sum(ours.values()); tb=sum(g4b.values())
print('total: g4 disk %.5f ours %.5f  g4 bare %.5f'%(tg,to,tb))
print('band    g4disk/tot  ours/tot  ours/g4   g4bare/tot')
for lo in range(0,400,20):
    hi=lo+20
    a=band(g4d,lo,hi); b=band(ours,lo,hi); c=band(g4b,lo,hi)
    print('%4d-%-4d %9.5f %9.5f %+8.2f%% %9.5f'%(lo,hi,a/tg,b/to,100*(b/a-1) if a else 0,c/tb))
# absolute per-history
print('absolute per history (x1e3): g4disk ours diff(g4-ours)')
for lo in range(0,400,50):
    hi=lo+50
    a=band(g4d,lo,hi); b=band(ours,lo,hi)
    print('%4d-%-4d %8.4f %8.4f %8.4f'%(lo,hi,1e3*a,1e3*b,1e3*(a-b)))
