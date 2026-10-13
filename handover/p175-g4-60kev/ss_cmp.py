# П175: EPICS2014 оболочечные таблицы G4 (pe-ss-cs-Z, σ·E³) на 32/60 кэВ против фита G4 и XCOM.
import math, sys
root = 'C:/Users/moroz/source/repos/GEANT4/G4EMLOW8.8/livermore/phot_epics2014/'
def shells(z):
    tok = open(root + f'pe-ss-cs-{z}.dat').read().split()
    i = 0; out = []
    while i < len(tok):
        emin, emax, n, sid = float(tok[i]), float(tok[i+1]), int(tok[i+2]), tok[i+3]; i += 4
        pts = [(float(tok[i + 2*k]), float(tok[i + 2*k + 1])) for k in range(n)]; i += 2 * n
        out.append(pts)
    return out
def interp(pts, e):  # σ·E³ линейно по логарифму энергии? берём лог-лог
    if e < pts[0][0]: return 0.0
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        if x0 <= e <= x1:
            if y0 <= 0 or y1 <= 0: return y0 + (y1 - y0) * (e - x0) / (x1 - x0)
            f = math.log(e / x0) / math.log(x1 / x0)
            return math.exp(math.log(y0) + f * (math.log(y1) - math.log(y0)))
    return pts[-1][1]
xcom = {(17, 32.0): 102.5021, (17, 60.0): 14.38, (19, 32.0): 164.2893, (19, 60.0): 23.52, (13, 32.0): 31.8496, (13, 60.0): 4.285, (6, 32.0): 0.9183, (6, 60.0): 0.1131}
fit = {(17, 32.0): 103.5717, (17, 60.0): 14.0583, (19, 32.0): 166.0031, (19, 60.0): 22.9686, (13, 32.0): 32.1670, (13, 60.0): 4.1972, (6, 32.0): 0.9271, (6, 60.0): 0.1107}
for z in (6, 13, 17, 19):
    sh = shells(z)
    print(f'Z={z}: оболочек {len(sh)}, узлы K: ' + ' '.join(f'{p[0]*1000:.2f}' for p in sh[0]))
    for ek in (32.0, 60.0):
        e = ek / 1000
        s = sum(interp(p, e) for p in sh) / e ** 3
        print(f'   E={ek}: EPICS2014 табл. {s:.4f} б  XCOM {xcom[(z,ek)]:.4f} ({s/xcom[(z,ek)]:.4f})  фит G4 {fit[(z,ek)]:.4f} (фит/табл {fit[(z,ek)]/s:.4f})')

# развёртка: фит G4 (matdb epics_photo_fit = pe-low/pe-high) против таблицы EPICS2014 G4 по энергии
import sqlite3
db = sqlite3.connect('file:C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/BecquerelMonitor/matdb.sqlite?mode=ro', uri=True)
print('\nфит/таблица EPICS2014 (фотоэффект, у G4 — рабочая формула против собственных данных):')
Es = [10, 15, 20, 26.3, 32, 40, 50, 59.54, 60, 70, 80, 100]
print('Z   ' + ' '.join(f'{e:>6}' for e in Es))
for z in (6, 8, 13, 17, 19):
    sh = shells(z); ns = db.execute('select high_from_ev, low_from_ev from epics_photo_meta where z=?', (z,)).fetchone()
    row = []
    for ek in Es:
        e = ek / 1000
        tab = sum(interp(p, e) for p in sh) / e ** 3
        kind = 'high' if ek * 1000 >= ns[0] else 'low'
        a = db.execute('select a1_b,a2_b,a3_b,a4_b,a5_b,a6_b from epics_photo_fit where z=? and kind=? order by shell_seq desc limit 1', (z, kind)).fetchone()
        fit = sum(c / e ** (i + 1) for i, c in enumerate(a))
        row.append(f'{fit/tab:6.3f}' if e <= sh[0][-1][0] else '   -  ')
    print(f'{z:<3} ' + ' '.join(row))
