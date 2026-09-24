import re,glob,os,sys
d=r"C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/tools/CORPUS/corpus/spectra"
rows=[]
for f in sorted(glob.glob(d+"/*.xml")):
    t=open(f,encoding="utf-8",errors="replace").read()
    # first EnergySpectrum block = sample
    m=re.search(r"<TotalPulseCount>([^<]*)</TotalPulseCount>.*?<MeasurementTime>([^<]*)</MeasurementTime>.*?<LiveTime>([^<]*)</LiveTime>",t,re.S)
    if not m: rows.append((os.path.basename(f),None));continue
    n,T,L=float(m.group(1)),float(m.group(2)),float(m.group(3))
    rows.append((os.path.basename(f),n,T,L))
cnt={'LT0':0,'LT=T':0,'LT<T':0,'LT>T':0}
for r in rows:
    if r[1] is None: print(r[0],"no match");continue
    f,n,T,L=r
    if L<=0: k='LT0'
    elif abs(L-T)<1e-9*max(T,1): k='LT=T'
    elif L<T: k='LT<T'
    else: k='LT>T'
    cnt[k]+=1
    tau=(T-L)/n*1e6 if (L>0 and n>0) else float('nan')
    rate=n/(L if L>0 else T) if T>0 else float('nan')
    print(f"{f:40s} {k:5s} N={n:.0f} T={T:.1f} LT={L:.3f} rate={rate:.1f}cps tau_eff={tau:.3f}us Rtau_eff={rate*tau*1e-6 if tau==tau else float('nan'):.2e}")
print(cnt)
