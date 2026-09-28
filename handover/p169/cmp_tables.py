import sqlite3, sys
a=sqlite3.connect(sys.argv[1]); b=sqlite3.connect(sys.argv[2])
for t in ("ensdf_datasets","ensdf_levels","ensdf_gammas","ensdf_feedings"):
    ca=[r[1] for r in a.execute(f"pragma table_info({t})")]
    cb=[r[1] for r in b.execute(f"pragma table_info({t})")]
    common=[c for c in ca if c in cb]
    q=f"select {','.join(common)} from {t} order by 1,2,3"
    ra=a.execute(q).fetchall(); rb=b.execute(q).fetchall()
    sa=set(ra); sb=set(rb)
    print(t, "строк", len(ra), len(rb), "только в первой", len(sa-sb), "только во второй", len(sb-sa), "колонки+", [c for c in cb if c not in ca], "колонки-", [c for c in ca if c not in cb])
