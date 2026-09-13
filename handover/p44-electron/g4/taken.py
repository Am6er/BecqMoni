# -*- coding: utf-8 -*-
import sys; sys.path.insert(0,'.')
from cmp44 import read_ours, read_g4, band
sys.stdout.reconfigure(encoding='utf-8')
R='C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/handover/'
g4,g4c,dec=read_g4(R+'p26-amber22/g4/g4_AS80_th_disk_2614.511_air.log')
off=read_ours('g4/disk/ours_AS80_th_disk_2614.511_off.csv'); ec=read_ours('g4/disk/ours_AS80_th_disk_2614.511_ecomp.csv')
print('полоса     дефицит ВЫКЛ  дефицит ecomp  забрано, %  (на историю x1e3)')
tot=0; tot_ec=0
for lo,hi in [(0,100),(100,200),(200,300),(300,500),(500,600),(600,1000),(1000,1200),(1200,2000),(2000,2612)]:
    d0=band(g4,lo,hi)-band(off,lo,hi); d1=band(g4,lo,hi)-band(ec,lo,hi)
    tot+=d0; tot_ec+=d1
    print('%5d-%-5d %10.4f %12.4f %10.1f'%(lo,hi,1e3*d0,1e3*d1,100*(1-d1/d0) if d0 else 0))
print('всего 0-2612: %.4f -> %.4f, забрано %.1f %%'%(1e3*tot,1e3*tot_ec,100*(1-tot_ec/tot)))
