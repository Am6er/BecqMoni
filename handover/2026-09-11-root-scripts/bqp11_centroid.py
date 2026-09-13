# -*- coding: utf-8 -*-
# Центроид (и вершина параболой) структуры в окне [lo,hi] кэВ: данные и модели плеч, после вычитания линейной подложки по краям окна
import csv, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp11_out'
key = sys.argv[1]; lo, hi = float(sys.argv[2]), float(sys.argv[3]); arms = sys.argv[4:]
def load(a):
    with io.open(os.path.join(root, a + '_dump', key + '_chi.csv'), encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))
D = {a: load(a) for a in arms}
A = D[arms[0]]
sel = [i for i in range(len(A)) if lo <= float(A[i]['keV']) < hi]
kev = [float(A[i]['keV']) for i in sel]
def stats(vals):
    # линейная подложка по крайним 3 каналам
    b0 = sum(vals[:3])/3; b1 = sum(vals[-3:])/3
    n = len(vals)
    net = [vals[j] - (b0 + (b1-b0)*j/(n-1)) for j in range(n)]
    s = sum(net); c = sum(net[j]*kev[j] for j in range(n))/s if s else float('nan')
    jm = max(range(n), key=lambda j: net[j])
    # вершина параболой по трём точкам
    if 0 < jm < n-1:
        y0, y1, y2 = net[jm-1], net[jm], net[jm+1]
        d = (y0 - 2*y1 + y2)
        off = 0.5*(y0 - y2)/d if d else 0.0
        top = kev[jm] + off*(kev[jm+1]-kev[jm])
    else:
        top = kev[jm]
    # ПШПВ по полувысоте
    half = net[jm]/2
    l = jm
    while l > 0 and net[l] > half: l -= 1
    r = jm
    while r < n-1 and net[r] > half: r += 1
    return s, c, top, kev[r]-kev[l]
print('%s, окно %.0f..%.0f кэВ: площадь над линейной подложкой, центроид, вершина (парабола), ПШПВ' % (key, lo, hi))
print('%-12s %12s %9s %9s %8s' % ('ряд','площадь','центроид','вершина','ПШПВ'))
s, c, t, w = stats([float(A[i]['fit']) for i in sel]); print('%-12s %12.0f %9.2f %9.2f %8.1f' % ('данные', s, c, t, w))
for a in arms:
    s, c, t, w = stats([float(D[a][i]['model']) for i in sel]); print('%-12s %12.0f %9.2f %9.2f %8.1f' % ('модель_'+a, s, c, t, w))
