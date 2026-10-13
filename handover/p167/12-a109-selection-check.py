import re,math
s=open('spectra/RC103_Th232WT20.xml',encoding='utf-8-sig').read()
def block(tag):
    return re.search('<%s>(.*?)</%s>'%(tag,tag),s,re.S).group(1)
fg=block('EnergySpectrum'); bg=block('BackgroundEnergySpectrum')
def parse(b):
    co=[float(x) for x in re.findall('<Coefficient>(.*?)</Coefficient>',re.search('<Coefficients>(.*?)</Coefficients>',b,re.S).group(1))]
    sp=[int(x) for x in re.findall('<DataPoint>(.*?)</DataPoint>',re.search('<Spectrum>(.*?)</Spectrum>',b,re.S).group(1))]
    lt=float(re.search('<LiveTime>(.*?)</LiveTime>',b).group(1))
    return co,sp,lt
cf,sf,lf=parse(fg); cb,sb,lb=parse(bg)
E=lambda c,x: sum(c[i]*x**i for i in range(len(c)))
def edges(c,n):
    ce=[E(c,i) for i in range(n)]
    e=[0]*(n+1)
    for i in range(1,n): e[i]=0.5*(ce[i-1]+ce[i])
    e[0]=ce[0]-0.5*(ce[1]-ce[0]); e[n]=ce[n-1]+0.5*(ce[n-1]-ce[n-2]); return e
n=len(sf); ef=edges(cf,n); eb=edges(cb,len(sb))
reb=[0.0]*n
for j,v in enumerate(sb):
    a,b=eb[j],eb[j+1]
    for i in range(n):
        ov=min(b,ef[i+1])-max(a,ef[i])
        if ov>0: reb[i]+=v*ov/(b-a)
k=lf/lb
def ch_of(c,e):
    # bisection
    lo,hi=-50.0,len(sb)+50.0
    for _ in range(80):
        m=(lo+hi)/2
        if E(c,m)<e: lo=m
        else: hi=m
    return (lo+hi)/2
old=sum(sb[round(ch_of(cb,E(cf,i)))] for i in range(591,779))
print('k',k,'rebinned adj',sum(reb[591:779])*k,'nearest adj',old*k)
