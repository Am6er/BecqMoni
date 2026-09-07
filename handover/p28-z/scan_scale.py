# -*- coding: utf-8 -*-
import xml.etree.ElementTree as ET, numpy as np, sys
p=sys.argv[1] if len(sys.argv)>1 else 'tools/CORPUS/corpus/spectra/AS80_Charoite.xml'
t=ET.parse(p); r=t.getroot()
es=r.find('.//EnergySpectrum'); bg=r.find('.//BackgroundEnergySpectrum')
c=np.array([int(x.text) for x in es.find('Spectrum').findall('DataPoint')],float)
lt=float(es.find('LiveTime').text)
b=np.array([int(x.text) for x in bg.find('Spectrum').findall('DataPoint')],float)
blt=float(bg.find('LiveTime').text)
co=[float(x.text) for x in es.find('EnergyCalibration').find('Coefficients')]
k=lt/blt
net=c-b*k
n=len(c); ch=np.arange(n,dtype=float)
E=co[0]+co[1]*ch+co[2]*ch**2
FW=float(sys.argv[2]) if len(sys.argv)>2 else 7.65
def sigma_kev(e): return FW/100.0*662.0*np.sqrt(np.maximum(e,1.0)/662.0)/2.355
LINES=[(241.997,7.43),(295.224,18.41),(351.932,35.60),(609.312,45.49),
       (768.356,4.89),(934.061,3.10),(1120.287,14.91),(1238.11,5.83),
       (1377.67,3.97),(1729.60,2.84),(1764.49,15.31),(1847.42,2.03),
       (2204.21,4.92),(2447.86,1.55),(1460.82,10.0)]
def zat(Eobs):
    # индекс канала по наблюдаемой энергии
    i=int(np.searchsorted(E,Eobs))
    if i<20 or i>n-25: return None
    s_kev=sigma_kev(Eobs)
    dEdch=co[1]+2*co[2]*i
    w=max(2,int(round(s_kev/dEdch)))
    lo,hi=i-w,i+w+1
    side=int(round(3*w))
    if i-side-w<0 or i+side+w>=n: return None
    base=0.5*(net[i-side-w:i-side+w+1].sum()+net[i+side-w:i+side+w+1].sum())
    s=net[lo:hi].sum()-base
    var=c[lo:hi].sum()+b[lo:hi].sum()*k*k+0.25*(c[i-side-w:i-side+w+1].sum()+b[i-side-w:i-side+w+1].sum()*k*k+c[i+side-w:i+side+w+1].sum()+b[i+side-w:i+side+w+1].sum()*k*k)
    return s/np.sqrt(max(var,1.0))
best=None
for B in np.arange(1.030,1.062,0.0005):
    for A in np.arange(-15,15,0.25):
        tot=0.0; cnt=0
        for (e,inten) in LINES:
            z=zat(A+B*e)
            if z is None: continue
            tot+=inten*z; cnt+=1
        if cnt<10: continue
        if best is None or tot>best[0]: best=(tot,A,B)
print('лучшая аффинная подгонка: E_набл = %.3f + %.5f * E_ист, суммарный вес %.1f'%(best[1],best[2],best[0]))
A,B=best[1],best[2]
print('%-10s %10s %8s %8s'%('E_ист','E_набл','z(как есть)','z(поправл.)'))
for (e,inten) in LINES:
    z0=zat(e); z1=zat(A+B*e)
    print('%-10.2f %10.2f %8s %8s'%(e,A+B*e,'%.2f'%z0 if z0 is not None else '-','%.2f'%z1 if z1 is not None else '-'))
