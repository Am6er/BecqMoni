# П30 (S43): сводка плеч β / γ=ε против контроля — обеими метриками (chi2ndf решателя и chi2ndf_pois отчётной) по понятной части.
import csv,glob,io,sys,statistics
sys.stdout.reconfigure(encoding='utf-8')
def load(d):
    r={}
    for f in glob.glob(d+'/*_spline_runs.csv'):
        for x in csv.DictReader(io.open(f,encoding='utf-8-sig',newline='')):
            if x['part']=='known' and x['chi2ndf']!='ERROR': r[x['spectrum']]=x
    return r
def comps(d):
    r={}
    for f in glob.glob(d+'/*_spline_components.csv'):
        for x in csv.DictReader(io.open(f,encoding='utf-8-sig',newline='')):
            r.setdefault(x['spectrum'],[]).append(x)
    return r
c=load(sys.argv[1]); cc=comps(sys.argv[1])
print('%-14s %9s %9s %9s | %9s %9s %9s | %s' % ('плечо','Σχ²_реш','медиана','луч/хуж/ровно','Σχ²_pois','Δ %','луч/хуж/ровно','recall/фантомы: строки components'))
def line(name,a,ac):
    s1=sum(float(v['chi2ndf']) for v in c.values()); s2=sum(float(v['chi2ndf']) for v in a.values())
    p1=sum(float(v['chi2ndf_pois']) for v in c.values()); p2=sum(float(v['chi2ndf_pois']) for v in a.values())
    b=w=e=0; bp=wp=ep=0
    for k in c:
        d=float(a[k]['chi2ndf'])-float(c[k]['chi2ndf']); 
        if d<-0.0005: b+=1
        elif d>0.0005: w+=1
        else: e+=1
        d=float(a[k]['chi2ndf_pois'])-float(c[k]['chi2ndf_pois'])
        if d<-0.0005: bp+=1
        elif d>0.0005: wp+=1
        else: ep+=1
    med=statistics.median(float(v['chi2ndf']) for v in a.values())
    ncomp=sum(len(v) for v in ac.values())
    print('%-14s %9.3f %9.2f %4d/%3d/%3d | %9.1f %+8.2f %4d/%3d/%3d | компонентов %d' % (name,s2,med,b,w,e,p2,100*(p2/p1-1),bp,wp,ep,ncomp))
line('ctrl',c,cc)
for d in sys.argv[2:]:
    line(d.split('out_p30_mini_')[-1],load(d),comps(d))
