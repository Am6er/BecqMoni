import csv,sys,math,os
labs=sys.argv[1:]
specs=[('G1S16_Ba133_P5',18,50),('G1S24_Ba133_P5',18,50),('G1S16_Ba133_P25',18,50),('ASN16_Cs137',24,46),('G1S16_Cs137_P5',20,50),('G1S24_Cs137_P5',20,50),('G1S16_Cs137_P25',20,50),('G1S24_Cs137_P25',20,50),('RC103_Cs137_0cm',20,50),('G1S16_Eu152_P5',30,55),('G1S24_Eu152_P5',30,55),('ASN16_Lu176',50,75),('G1S16_Ce139_P5',25,45),('G1S16_Ce139_P25',25,45)]
print('spectrum'.ljust(17)+''.join(l.rjust(10) for l in labs)+'   best')
for spec,lo,hi in specs:
    vals=[]
    for lab in labs:
        p=(f'D:/BqMoni_Claude/p179/scan/{lab[2:]}/{spec}_curves.csv' if lab.startswith('T:') else f'D:/BqMoni_Claude/p179/scan/{lab}/{spec}_curves.csv')
        if not os.path.exists(p): vals.append(float('nan')); continue
        rows=list(csv.DictReader(open(p)))
        sel=[r for r in rows if lo<=float(r['keV'])<=hi]
        vals.append(sum((float(r['fit'])-float(r['model']))**2/max(float(r['model']),1) for r in sel))
    b=min(range(len(vals)),key=lambda i: vals[i] if vals[i]==vals[i] else 1e99)
    print(spec.ljust(17)+''.join(f"{v:10.0f}" for v in vals)+'   '+labs[b])
