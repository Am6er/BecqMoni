import sys, json
sys.path.insert(0, r"D:\BqMoni_Claude\p147\wt\tools\CORPUS\scripts")
import chains
print("DB", chains.DB, file=sys.stderr)
out={}
for root in ("238U","226RA","234TH"):
    c=chains.conn()
    out[root]={"br":chains.chain_branches(root,c)}
    lines=chains.chain_lines(root, kinds=('G','X'))
    out[root]["lines"]=[(r["nucid"],r["energy"],r["i_chain"]) for r in lines]
json.dump(out, open(sys.argv[1],"w"))
