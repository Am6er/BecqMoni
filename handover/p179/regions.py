# вклад областей энергии в Σ(fit−model)²/model (грубо, пирсон по каналам) — база против новой таблицы
import csv,sys
bands=[(0,30),(30,40),(40,60),(60,100),(100,200),(200,400),(400,700),(700,3000)]
def load(p):
    out=[0.0]*len(bands)
    for r in csv.DictReader(open(p)):
        e=float(r['keV']); f=float(r['model']); n=float(r['fit'])
        if f<=0: continue
        for i,(a,b) in enumerate(bands):
            if a<=e<b: out[i]+=(n-f)**2/max(f,1.0)
    return out
print('spectrum'.ljust(18)+''.join(('%d-%d'%b).rjust(18) for b in bands))
for s in sys.argv[1].split(','):
    a=load(f'D:/BqMoni_Claude/p179/curves/base/{s}_curves.csv'); b=load(f'D:/BqMoni_Claude/p179/curves/t179/{s}_curves.csv')
    print(s.ljust(18)+''.join(('%.0f→%.0f'%(x,y)).rjust(18) for x,y in zip(a,b)))
