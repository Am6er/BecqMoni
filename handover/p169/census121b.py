import sys, os
sys.path.insert(0, r"D:\BqMoni_Claude\p147\wt\tools\nucdb")
import import_ensdf as ie
src = r"C:\LSRM\NuclideMaster\TCCFCALC\LIB\ENSDF2"
n_ec=n_bp=n_unk=0; bp_sets={}
for name in sorted(os.listdir(src)):
    if not name.upper().endswith(".ENX"): continue
    for ds in ie.parse_file(os.path.join(src,name)):
        pass
# re-scan with parser + TI
def scan(path):
    out=[]; cur=None; lev=None
    for raw in open(path,encoding="latin-1").read().split("\n"):
        line=raw.rstrip("\r")
        if len(line)<8: continue
        if len(line)>9 and line[8]=="*" and line[5]==" " and line[7]==" ":
            cur={"dsid":line[9:39].strip(),"Q":None,"Ep":0.0}; lev=None; continue
        if cur is None or line[5]!=" " or line[6]!=" ": continue
        k=line[7]
        if k=="P" and cur["Q"] is None: cur["Q"]=ie.num(line[64:74]); cur["Ep"]=ie.num(line[9:19]) or 0.0
        if k=="L": lev=ie.num(line[9:19])
        if k=="E":
            ib=ie.num(line[21:29]); iec=ie.num(line[31:39]); t=ie.num(line[64:74])
            if ib is None and iec is None and t is not None:
                out.append((cur["dsid"],cur["Q"],cur["Ep"],lev,t))
    return out
for name in sorted(os.listdir(src)):
    if not name.upper().endswith(".ENX"): continue
    for dsid,Q,Ep,lev,t in scan(os.path.join(src,name)):
        if Q is None or lev is None: n_unk+=1; bp_sets.setdefault(dsid,[0,0,0])[2]+=t; continue
        e0=Q+Ep-lev
        if e0<1022: n_ec+=1; bp_sets.setdefault(dsid,[0,0,0])[0]+=t
        else: n_bp+=1; bp_sets.setdefault(dsid,[0,0,0])[1]+=t
print("β+ запрещён (E0<1022):",n_ec," β+ разрешён:",n_bp," неизвестно (нет Q или уровня):",n_unk)
for k,v in bp_sets.items():
    if v[1] or v[2]: print("  %-28s TI чистый захват %.3f, β+ возможен %.3f, неизв %.3f"%(k,*v))
