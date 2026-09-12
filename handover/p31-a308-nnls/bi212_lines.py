# -*- coding: utf-8 -*-
# П31: какие излучения Bi-212 ниже 50 кэВ несёт nucdb (только чтение, mode=ro) — к находке о 25 кэВ на корпусном диске
import sqlite3, os, sys
sys.stdout.reconfigure(encoding='utf-8')
p = os.path.abspath('BecquerelMonitor/nucdb.sqlite').replace(os.sep, '/')
con = sqlite3.connect('file:%s?mode=ro' % p, uri=True)
cur = con.cursor()
tabs = [r[0] for r in cur.execute("select name from sqlite_master where type='table'")]
print([t for t in tabs if 'radiat' in t.lower() or 'line' in t.lower()][:10])
cols = [r[1] for r in cur.execute('pragma table_info(decay_radiations)')]
print(cols)
q = "select * from decay_radiations where energy_kev < 50 and energy_kev > 5 and (nucid like '%212BI%' or nuclide like '%Bi-212%' or parent like '%212BI%') order by energy_kev"
try:
    for r in cur.execute(q): print(r)
except Exception as e:
    print('ERR', e)
    for r in cur.execute("select * from decay_radiations limit 3"): print(r)
