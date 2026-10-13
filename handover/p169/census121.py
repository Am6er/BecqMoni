import sys, os
sys.path.insert(0, r"D:\BqMoni_Claude\p147\wt\tools\nucdb")
import import_ensdf as ie
src = r"C:\LSRM\NuclideMaster\TCCFCALC\LIB\ENSDF2"
rows=[]
for name in sorted(os.listdir(src)):
    if not name.upper().endswith(".ENX"): continue
    cur=None
    for raw in open(os.path.join(src,name),encoding="latin-1").read().split("\n"):
        line=raw.rstrip("\r")
        if len(line)<8: continue
        if len(line)>9 and line[8]=="*" and line[5]==" " and line[7]==" ":
            cur={"file":name,"nucid":line[:5].strip(),"dsid":line[9:39].strip(),"ib":0,"ie":0,"ti":0,"nti":0,"flags":set(),"lev":None}
            rows.append(cur); continue
        if cur is None or line[5]!=" " or line[6]!=" ": continue
        if line[7]=="L": cur["lev"]=line[9:19].strip()
        if line[7]!="E": continue
        ib=ie.num(line[21:29]); iec=ie.num(line[31:39]); t=ie.num(line[64:74])
        cur["ib"]+=ib or 0; cur["ie"]+=iec or 0
        if ib is None and iec is None and t is not None:
            cur["ti"]+=t; cur["nti"]+=1
            fl=line[76:78].strip()+("?" if line[79:80]=="?" else "")
            cur["flags"].add((cur["lev"],t,fl))
tot=0
for r in rows:
    if r["nti"]==0: continue
    tot+=r["nti"]
    allf=r["ib"]+r["ie"]+r["ti"]
    print("%-6s %-26s IB=%7.3f IE=%7.3f TI=%7.3f потеря %5.1f%% n=%d %s" %(r["nucid"],r["dsid"],r["ib"],r["ie"],r["ti"],100*r["ti"]/allf if allf else 0,r["nti"], sorted(r["flags"],key=str)[:4]))
print("всего записей", tot)
