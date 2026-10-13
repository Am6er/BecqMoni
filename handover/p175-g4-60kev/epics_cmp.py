# П175: фотоэффект на 32/60 кэВ — XCOM (matdb) против EPICS (таблица оболочек matdb) против фита G4 (low/high из matdb).
import sqlite3, math
c = sqlite3.connect('file:C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/BecquerelMonitor/matdb.sqlite?mode=ro', uri=True)
def loglog(pts, e):
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        if x0 <= e <= x1 and x1 > x0:
            if y0 <= 0 or y1 <= 0: return y0 + (y1 - y0) * (e - x0) / (x1 - x0)
            f = math.log(e / x0) / math.log(x1 / x0)
            return math.exp(math.log(y0) + f * (math.log(y1) - math.log(y0)))
    return 0.0
for z in (6, 9, 13, 17, 19, 53, 55):
    for ekev in (32.0, 59.5409, 60.0):
        e = ekev * 1000
        xc = loglog(c.execute('select energy_ev, photoelectric_b from xcom_cross_sections where z=? order by energy_ev', (z,)).fetchall(), e)
        ns = c.execute('select n_shells, high_from_ev, low_from_ev from epics_photo_meta where z=?', (z,)).fetchone()
        tab = 0.0
        for s in range(ns[0]):
            pts = c.execute('select energy_ev, cs_b from epics_photo_subshell where z=? and shell_seq=? order by energy_ev', (z, s)).fetchall()
            tab += loglog(pts, e)
        kind = 'high' if e >= ns[1] else 'low'
        row = c.execute('select a1_b,a2_b,a3_b,a4_b,a5_b,a6_b from epics_photo_fit where z=? and kind=? order by shell_seq desc limit 1', (z, kind)).fetchone()
        em = e / 1e6
        fit = sum(a / em ** (i + 1) for i, a in enumerate(row)) if row else float('nan')
        print(f'Z={z:2d} E={ekev:7.3f}  XCOM {xc:10.4f} б  EPICS-табл {tab:10.4f} ({tab/xc:.4f})  фит G4 {kind} {fit:10.4f} ({fit/xc:.4f})')
