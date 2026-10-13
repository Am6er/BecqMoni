# сравнение двух прогонов по chi2ndf, часть known/unknown; печать: изменилось, суммы, лучше/хуже, все строки
import csv,glob,sys,os
def load(d,part):
    out={}
    for f in glob.glob(os.path.join(d,'*_runs.csv')):
        for r in csv.DictReader(open(f,encoding='utf-8-sig')):
            if r['part']==part:
                try: out[r['spectrum']]=float(r['chi2ndf'])
                except ValueError: pass
    return out
part=sys.argv[3] if len(sys.argv)>3 else 'known'
a=load(sys.argv[1],part); b=load(sys.argv[2],part)
ch=[s for s in a if s in b and abs(b[s]-a[s])>1e-9]
better=sum(1 for s in ch if b[s]<a[s]); worse=len(ch)-better
print(f'part={part} n={len(a)} changed={len(ch)} better={better} worse={worse} sumA={sum(a.values()):.4f} sumB={sum(b[s] for s in a if s in b):.4f} delta={sum(b[s]-a[s] for s in a if s in b):+.4f}')
for s in sorted(ch,key=lambda s: -(b[s]-a[s])):
    print(f"{s:28s} {a[s]:9.4f} {b[s]:9.4f} {b[s]-a[s]:+8.4f} {100*(b[s]/a[s]-1):+7.2f}%")
