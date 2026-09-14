# -*- coding: utf-8 -*-
# bqp12_check.py <каталог out П12> — контроль 3: chi2ndf против четырёх плеч П9, до 4 знаков
import csv,io,glob,os,sys
sys.stdout.reconfigure(encoding='utf-8')
def load(d):
    out={}
    for f in glob.glob(os.path.join(d,'*_runs.csv')):
        for r in csv.DictReader(io.open(f,encoding='utf-8-sig')):
            out[r['spectrum']]=r
    return out
def num(s):
    try: return float(s)
    except: return None
a=load(sys.argv[1])
tot=sum(num(r['chi2ndf']) or 0 for r in a.values() if r['part']=='known')
print('%s: спектров %d, Σ chi2ndf (known) %.4f' % (sys.argv[1], len(a), tot))
for arm in 'abvg':
    p=load('C:/Users/moroz/bqp9_out/'+arm)
    n=0;same=0;maxd=0;worst=''
    for k,r in a.items():
        x=num(r['chi2ndf']); y=num(p[k]['chi2ndf']) if k in p else None
        if x is None or y is None: continue
        n+=1; d=abs(x-y)
        if d>maxd: maxd=d; worst=k
        same+= d<5e-5
    print('  против bqp9 %s: спектров %d, сошлись до 4 знаков %d, max|Δ| %.4f (%s)' % (arm,n,same,maxd,worst))
