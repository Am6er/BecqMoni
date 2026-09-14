import sys, numpy as np
sys.path.insert(0, r'D:\BqMoni_Claude\p56\py')
from spec import Spec
S=Spec(sys.argv[1], sys.argv[2] if len(sys.argv)>2 and not sys.argv[2].startswith('--') else None)
lo=float([a for a in sys.argv if a.startswith('--lo=')][0][5:]) if any(a.startswith('--lo=') for a in sys.argv) else 130
hi=float([a for a in sys.argv if a.startswith('--hi=')][0][5:]) if any(a.startswith('--hi=') for a in sys.argv) else 240
step=float([a for a in sys.argv if a.startswith('--step=')][0][7:]) if any(a.startswith('--step=') for a in sys.argv) else 2.0
net,bgc=S.net()
print('live',S.live,'bg',S.bg.live if S.bg else None,'bgfile',S.bgfile, 'coef',S.coef)
E=lo
print('%8s %10s %10s %10s'%('кэВ','raw','bg*k','net'))
while E<hi:
    sel=(S.keV>=E)&(S.keV<E+step)
    print('%8.1f %10.0f %10.0f %10.0f'%(E, S.counts[sel].sum(), bgc[sel].sum(), net[sel].sum()))
    E+=step
