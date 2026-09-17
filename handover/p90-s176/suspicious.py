# -*- coding: utf-8 -*-
# П90: носители, у которых выбранный переход схемы противоречит поставке по главным партнёрам
# (f-взвешенное среднее |log(P/f)| по найденным партнёрам > 1.5) — кандидаты на «слитые» линии поставки.
import sqlite3, csv, sys, os
REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
HERE = os.path.dirname(os.path.abspath(__file__))
import math
from collections import defaultdict
for _s in (sys.stdout,): _s.reconfigure(encoding='utf-8', errors='replace')
nuc=sqlite3.connect('file:%s?mode=ro' % os.path.join(REPO,'BecquerelMonitor','nucdb.sqlite').replace(os.sep,'/'),uri=True)
ref={}
for r in csv.DictReader(open(os.path.join(HERE,'ref_pairs.csv'),encoding='utf-8'),delimiter=';'):
    ref[(r['nucid'],float(r['E_A']),float(r['E_B']))]=(float(r['P']),int(r['from_seq']))
tot_w=0; sus_w=0
for nucid in sys.argv[1:]:
    I={e:i for e,i in nuc.execute("select energy_kev,intensity_pct from v_gamma_coincidence_line where nucid=? and isomer=0",(nucid,))}
    by=defaultdict(list)
    for a,b,f in nuc.execute("select energy_kev,coinc_energy_kev,fraction from v_gamma_coincidence where nucid=? and isomer=0",(nucid,)):
        by[a].append((b,f,ref[(nucid,round(a,3),round(b,3))]))
    for a,lst in by.items():
        w=sum(I.get(a,0)*min(f,1) for b,f,(p,fs) in lst); tot_w+=w
        found=[(b,f,p) for b,f,(p,fs) in lst if p>0]
        if not found: continue
        num=sum(min(f,1)*abs(math.log(p/f)) for b,f,p in found); den=sum(min(f,1) for b,f,p in found)
        m=num/den
        sumP=sum(f for b,f,(p,fs) in lst)
        if m>1.5:
            sus_w+=w
            print('%s %9.3f (I=%.3f%%, вес I·Σf=%.4f, Σf поставки=%.2f) среднее |log(P/f)|=%.2f  главные: %s'%(nucid,a,I.get(a,0),w,sumP,m,
                  ', '.join('%.1f f=%.3f P=%.3f'%(b,f,p) for b,f,p in sorted(found,key=lambda t:-t[1])[:3])))
print('вес всех носителей %.3f, подозрительных %.3f (%.2f %%)'%(tot_w,sus_w,100*sus_w/tot_w))
