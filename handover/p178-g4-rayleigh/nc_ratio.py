# П178: наш/G4 для μ БЕЗ когерентного (phot+compt) по веществам сцены; сцены для g4cf norayl.
import sys, math, sqlite3
sys.path.insert(0, 'D:/BqMoni_Claude/p178/wt/handover/p175-g4-60kev')
xs = 'D:/BqMoni_Claude/p178/wt/handover/p175-g4-60kev/xs.txt'
scene = 'D:/BqMoni_Claude/p164/g4/scene.txt'
db = sqlite3.connect('file:C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/BecquerelMonitor/matdb.sqlite?mode=ro', uri=True)
NA = 0.60221408
def ours(z, ekev):
    aw = db.execute('select atomic_weight from xcom_elements where z=?', (z,)).fetchone()[0]
    rows = db.execute('select energy_ev, coherent_b, incoherent_b, photoelectric_b from xcom_cross_sections where z=? order by energy_ev', (z,)).fetchall()
    e = ekev * 1000
    for i in range(len(rows) - 1):
        if rows[i][0] <= e <= rows[i + 1][0] and rows[i + 1][0] > rows[i][0]:
            a, b = rows[i], rows[i + 1]
            f = math.log(e / a[0]) / math.log(b[0] / a[0])
            out = [math.exp(math.log(a[k]) + f * (math.log(b[k]) - math.log(a[k]))) * NA / aw for k in (1, 2, 3)]
            return dict(Rayl=out[0], compt=out[1], phot=out[2])
g4 = {}
for line in open(xs):
    p = line.split()
    if p and p[0] == 'XS': g4[(p[1], float(p[2]), p[3])] = float(p[5])
mats = {}; lines = open(scene, encoding='utf-8').read().splitlines()
for line in lines:
    p = line.split()
    if p and p[0] == 'mat':
        mats[p[1]] = (float(p[2]), [(int(t.split(':')[0]), float(t.split(':')[1])) for t in p[3:]])
scale = {}
for E in (32.0, 60.0):
    scale[E] = {}
    for m, (rho, parts) in mats.items():
        o = dict(phot=0, compt=0, Rayl=0)
        for z, w in parts:
            d = ours(z, E)
            for k in o: o[k] += w * d[k]
        rt = sum(o.values()) / g4[(m, E, 'total')]
        rn = (o['phot'] + o['compt']) / (g4[(m, E, 'phot')] + g4[(m, E, 'compt')])
        print(f'{m} E={E:5.1f} наш/G4 полное {rt:.6f}  без когерентного {rn:.6f}')
        scale[E][m] = rn
for E, tag in ((60.0, 'Bnc60'), (32.0, 'Cnc32')):
    out = []
    for line in lines:
        p = line.split()
        if p and p[0] == 'mat' and p[1] in ('m1', 'm3', 'm4', 'm5'):
            p[2] = f'{float(p[2]) * scale[E][p[1]]:.7g}'
            line = ' '.join(p)
        out.append(line)
    open(f'D:/BqMoni_Claude/p178/g4/scene_{tag}.txt', 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
    print('записано', tag)
