import sys,re
import numpy as np
def load(p):
    h=None;step=None;dec=None
    for line in open(p,encoding='utf-8',errors='replace'):
        if line.startswith('HISTBEGIN'):
            m=re.search(r'bins=(\d+) bin_kev=([\d.]+) decays=(\d+)',line)
            h=np.zeros(int(m.group(1)));step=float(m.group(2));dec=int(m.group(3))
        elif line.startswith('HIST ') and h is not None:
            _,i,c=line.split(); h[int(i)]=float(c)
    return h,step,dec
if __name__=='__main__':
    h,step,dec=load(sys.argv[1])
    idx=np.argsort(h)[::-1][:40]
    for i in sorted(idx): print(f"{i*step:9.3f} {h[i]:8.0f}")
