# П56: кривая эффективности сцены из узла <Efficiency><Curve> спектра (корпусный AS80_Th232Medal.xml — AS80_th_disk, phys=18)
import re, numpy as np
def load_curve(path):
    t=open(path,encoding='utf-8').read()
    m=re.search(r'<Efficiency>.*?<Curve>(.*?)</Curve>', t, re.S)
    pts=re.findall(r'<Energy>([^<]+)</Energy><Efficiency>([^<]+)</Efficiency>', m.group(1))
    E=np.array([float(a) for a,b in pts]); eff=np.array([float(b) for a,b in pts])
    stamp=re.search(r'<ComputeStamp>([^<]*)', t).group(1)
    return E, eff, stamp
class Eff:
    def __init__(self, path):
        self.E, self.eff, self.stamp = load_curve(path)
    def __call__(self, E):
        return float(np.exp(np.interp(np.log(E), np.log(self.E), np.log(np.maximum(self.eff,1e-30)))))
if __name__=='__main__':
    import sys
    e=Eff(sys.argv[1]); print(e.stamp)
    for E in (63.3,92.6,143.8,163.3,185.7,205.3,238.6,295.2,351.9,583.2,609.3,766.4,911.2,969.0,1001.0,1120.3,1460.8,1764.5,2614.5):
        print('%8.1f %.5f'%(E,e(E)))
