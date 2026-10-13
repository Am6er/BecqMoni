import sqlite3, sys, os
sys.path.insert(0, r"D:\BqMoni_Claude\p147\wt\tools\nucdb")
import import_ensdf as ie
src = r"C:\LSRM\NuclideMaster\TCCFCALC\LIB\ENSDF2"
db = sqlite3.connect(r"file:D:\BqMoni_Claude\p169\db_backup\schemedb.sqlite?mode=ro", uri=True)
# existing E rows with both empty
n_e = db.execute("select count(*) from ensdf_feedings where kind='E'").fetchone()[0]
n_e_empty = db.execute("select count(*) from ensdf_feedings where kind='E' and intensity is null and intensity_ec is null").fetchone()[0]
print("E-записей", n_e, "пустых IB и IE", n_e_empty)
# raw scan for TI on E records
cnt=0; ti=0
per_ds={}
for name in sorted(os.listdir(src)):
    if not name.upper().endswith(".ENX"): continue
    dsid=None
    for raw in open(os.path.join(src,name),encoding="latin-1").read().split("\n"):
        line=raw.rstrip("\r")
        if len(line)<8: continue
        if len(line)>9 and line[8]=="*" and line[5]==" " and line[7]==" ":
            dsid=(line[:5].strip(), line[9:39].strip()); continue
        if line[5]!=" " or line[6]!=" " or line[7]!="E": continue
        ib=ie.num(line[21:29]); iec=ie.num(line[31:39]); t=ie.num(line[64:74])
        if ib is None and iec is None:
            cnt+=1
            if t is not None:
                ti+=1; per_ds.setdefault(dsid,[]).append(t)
print("в исходнике E пустых IB/IE", cnt, "из них с TI", ti, "наборов", len(per_ds))
