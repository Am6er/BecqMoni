# П56: net (проба − фон·k) на равномерной сетке кэВ (перебинирование по границам каналов, без ступенек АЦП)
import sys, numpy as np
sys.path.insert(0, r'D:\BqMoni_Claude\p56\py')
from spec import Spec, rebin
args=[a for a in sys.argv[1:] if not a.startswith('--')]
opt={a.split('=')[0][2:]:float(a.split('=')[1]) for a in sys.argv[1:] if a.startswith('--')}
S=Spec(args[0], args[1] if len(args)>1 else None)
lo,hi,step=opt.get('lo',130),opt.get('hi',240),opt.get('step',2.0)
edges=np.arange(lo,hi+step/2,step)
raw=rebin(S, edges)
k=S.live/S.bg.live if S.bg else 0
bg=rebin(S.bg, edges)*k if S.bg else np.zeros_like(raw)
print('live',round(S.live,1),'bg live',round(S.bg.live,1) if S.bg else None,'k',round(k,5))
print('%8s %10s %10s %10s %8s'%('кэВ','raw','bg*k','net','σ'))
for i in range(len(raw)):
    print('%8.1f %10.0f %10.0f %10.0f %8.0f'%(edges[i], raw[i], bg[i], raw[i]-bg[i], np.sqrt(raw[i]+bg[i]*k)))
