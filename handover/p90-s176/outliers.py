# -*- coding: utf-8 -*-
import sqlite3, csv, sys, os
REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
HERE = os.path.dirname(os.path.abspath(__file__))
for _s in (sys.stdout,): _s.reconfigure(encoding='utf-8', errors='replace')
nuc=sqlite3.connect('file:%s?mode=ro' % os.path.join(REPO,'BecquerelMonitor','nucdb.sqlite').replace(os.sep,'/'),uri=True)
ref={}
for r in csv.DictReader(open(os.path.join(HERE,'ref_pairs.csv'),encoding='utf-8'),delimiter=';'):
    ref[(r['nucid'],float(r['E_A']),float(r['E_B']))]=(float(r['P']),int(r['from_seq']))
lo,hi=float(sys.argv[1]),float(sys.argv[2])
for nucid in sys.argv[3:]:
    I={e:i for e,i in nuc.execute("select energy_kev,intensity_pct from v_gamma_coincidence_line where nucid=? and isomer=0",(nucid,))}
    rows=[]
    for a,b,f in nuc.execute("select energy_kev,coinc_energy_kev,fraction from v_gamma_coincidence where nucid=? and isomer=0",(nucid,)):
        p,fs=ref[(nucid,round(a,3),round(b,3))]
        rows.append((a,b,f,p,fs))
    n=len(rows); out=[r for r in rows if r[3]<0 or not lo<=r[3]/r[2]<=hi]
    print('=== %s: пар %d, вне [%g,%g] или поставочных: %d'%(nucid,n,lo,hi,len(out)))
    for a,b,f,p,fs in sorted(out,key=lambda r:-I.get(r[0],0)*min(r[2],1)):
        r=p/f if p>0 else float('nan')
        print('  %9.3f (I=%7.3f) -> %9.3f (I=%7.3f) Sandia=%.5f схема=%.5f отн=%.3f seq=%d  вес I·f=%.4f'%(a,I.get(a,-1),b,I.get(b,-1),f,p,r,fs,I.get(a,0)*min(f,1)))
