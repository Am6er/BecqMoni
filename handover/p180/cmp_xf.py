# П180: сверка xray_fluorescence двух баз: какие столбцы отличаются и где
import sqlite3, sys
a = sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True); b = sqlite3.connect('file:' + sys.argv[2] + '?mode=ro', uri=True)
cols = [d[1] for d in a.execute('pragma table_info(xray_fluorescence)')]
print('схема одинакова:', a.execute("select sql from sqlite_master where name='xray_fluorescence'").fetchone() == b.execute("select sql from sqlite_master where name='xray_fluorescence'").fetchone())
ra = {r[0]: r for r in a.execute('select * from xray_fluorescence')}; rb = {r[0]: r for r in b.execute('select * from xray_fluorescence')}
print('элементов', len(ra), len(rb), 'одни и те же:', set(ra) == set(rb))
diff = {}
for z in ra:
    for i, c in enumerate(cols):
        if ra[z][i] != rb[z][i]: diff.setdefault(c, []).append(z)
for c, zs in diff.items(): print('столбец', c, 'отличается у', len(zs), 'элементов')
print('Z   kb было   kb стало   Δ, кэВ')
for z in sorted(ra):
    if ra[z][9] != rb[z][9] and z in (29, 47, 50, 53, 55, 56, 57, 58, 64, 71, 72, 74, 82, 83): print('%3d %9.3f %9.3f %+7.3f' % (z, ra[z][9]/1000, rb[z][9]/1000, (rb[z][9]-ra[z][9])/1000))
print('не изменились kb:', [z for z in sorted(ra) if ra[z][9] == rb[z][9]])
