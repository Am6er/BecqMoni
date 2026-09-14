# П56: невязка net − model дампов П42 (корпусный диск rev21 и сцена Amber) в окнах линий урана
import csv, sys, math
def load(path):
    rows=[]
    with open(path, newline='', encoding='utf-8') as f:
        r=csv.DictReader(f)
        for d in r:
            rows.append({k:(float(v) if k!='' else v) for k,v in d.items()})
    return rows
def fwhm(E, p=7.65):  # ПШПВ NaI ~ 7.65 % на 662, ∝ sqrt(E)
    return p/100*662*math.sqrt(E/662)
lines=[(63.3,'Th-234 63.3'),(92.6,'Th-234 92.4/92.8'),(143.8,'U-235 143.8'),(163.3,'U-235 163.3'),(185.7,'U-235 185.7 / Ra-226 186.2'),(205.3,'U-235 205.3'),
       (295.2,'Pb-214 295.2'),(351.9,'Pb-214 351.9'),(609.3,'Bi-214 609.3'),(766.4,'Pa-234m 766.4'),(1001.0,'Pa-234m 1001.0'),(1120.3,'Bi-214 1120.3'),(1460.8,'K-40 1460.8'),(1764.5,'Bi-214 1764.5')]
for path in sys.argv[1:]:
    rows=load(path)
    print('==',path, 'rows',len(rows))
    print('%-28s %8s %10s %10s %9s %8s %8s'%('линия','окно,кэВ','net','model','resid','resid%','z=res/sqrt(model)'))
    for E,name in lines:
        w=fwhm(E)
        sel=[r for r in rows if E-w<=r['keV']<=E+w]
        if not sel: continue
        n=sum(r['net'] for r in sel); m=sum(r['model'] for r in sel)
        res=n-m
        print('%-28s %8.1f %10.0f %10.0f %9.0f %7.1f%% %8.1f'%(name,2*w,n,m,res,100*res/max(m,1),res/math.sqrt(max(m,1))))
