# -*- coding: utf-8 -*-
import sqlite3, glob, os
root = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor"
tot = 0
for f in sorted(glob.glob(os.path.join(root, '*.sqlite'))):
    u = 'file:' + f.replace(os.sep, '/').replace(' ', '%20') + '?mode=ro'
    c = sqlite3.connect(u, uri=True)
    ts = [r[0] for r in c.execute("select name from sqlite_master where type in ('table','view')")]
    tot += len(ts)
    hits = [t for t in ts if any(k in t.lower() for k in ('auger', 'estar', 'star_', 'thermal', 'fission'))]
    print(os.path.basename(f), len(ts), hits)
print('vsego tablic i predstavlenij:', tot)
