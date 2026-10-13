# П175: μ/ρ по каналам — наша таблица XCOM (matdb, лог-лог по каналу, как PartialCrossSections)
# против арбитра Geant4 option4 (g4xs). python xs_cmp.py <xs.txt> <scene.txt>
import sys, math, sqlite3, re
xs, scene = sys.argv[1], sys.argv[2]
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
g4 = {}; g4z = {}
for line in open(xs):
    p = line.split()
    if p and p[0] == 'XS': g4[(p[1], float(p[2]), p[3])] = float(p[5])
    if p and p[0] == 'XSZ': g4z[(int(p[1]), float(p[2]), p[3])] = float(p[4])
mats = {}
for line in open(scene):
    p = line.split()
    if p and p[0] == 'mat':
        mats[p[1]] = (float(p[2]), [(int(t.split(':')[0]), float(t.split(':')[1])) for t in p[3:]])
names = dict(m0='CsI', m1='PTFE', m2='воздух', m3='Al', m4='ПЭ', m5='KCl')
Es = sorted({k[1] for k in g4})
print('вещество E кэВ | phot наш/G4 | compt наш/G4 | Rayl наш/G4 | total наш  G4  наш/G4')
for m, (rho, parts) in mats.items():
    for E in Es:
        o = dict(phot=0, compt=0, Rayl=0)
        for z, w in parts:
            d = ours(z, E)
            for k in o: o[k] += w * d[k]
        r = {k: o[k] / g4[(m, E, k)] for k in o}
        ot = sum(o.values()); gt = g4[(m, E, 'total')]
        print(f'{names.get(m,m):7s} {E:7.3f} | {r["phot"]:.4f} | {r["compt"]:.4f} | {r["Rayl"]:.4f} | {ot:.5f} {gt:.5f} {ot/gt:.4f}  (μ·ρ наш {ot*rho:.4f}/см)')
print()
for z in sorted({k[0] for k in g4z}):
    for E in Es:
        d = ours(z, E)
        s = ' '.join(f'{k} {d[k]/g4z[(z,E,k)]:.4f}' for k in ('phot','compt','Rayl'))
        print(f'Z={z:2d} {E:7.3f} {s} total {sum(d.values())/g4z[(z,E,"total")]:.4f}')
