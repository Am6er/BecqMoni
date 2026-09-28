import csv,sys
rows=list(csv.DictReader(open(sys.argv[1],encoding='utf-8'),delimiter='\t'))
bad=[]
for r in rows:
    if not r['gamma_kev']: continue
    i511=float(r['annihilation_quanta']); ig=float(r['intensity_pct'])/100; f=float(r['p511_given_g']); b=float(r['pg_given_511']) if r['pg_given_511'] not in ('','NaN') else float('nan')
    if f<=0: continue
    jg=ig*f; j5=i511*b
    d=j5/jg-1 if jg>0 else float('nan')
    if abs(d)>1e-6: bad.append((r['nucid'],float(r['gamma_kev']),d,f,b))
print('линий с нарушением тождества >1e-6:',len(bad))
for x in sorted(bad,key=lambda t:t[2]): print('%-6s %9.3f  %+7.2f %%  P(511|g)=%.5f  P(g|511)=%.5f'%(x[0],x[1],100*x[2],x[3],x[4]))
