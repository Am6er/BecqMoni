import sys, os, glob, io, csv
a, b = sys.argv[1], sys.argv[2]
IGN = {"ms", "cpu_ms"}
tot = 0; diff = 0; files = 0; timing_only = 0
for fa in sorted(glob.glob(os.path.join(a, "*.csv"))):
    fb = os.path.join(b, os.path.basename(fa))
    if not os.path.exists(fb):
        print("НЕТ В", b, os.path.basename(fa)); continue
    files += 1
    ra = list(csv.reader(io.open(fa, encoding="utf-8-sig")))
    rb = list(csv.reader(io.open(fb, encoding="utf-8-sig")))
    if ra[0] != rb[0]:
        print("ЗАГОЛОВОК РАЗОШЁЛСЯ", os.path.basename(fa)); diff += 1; continue
    hdr = ra[0]
    keep = [i for i, h in enumerate(hdr) if h not in IGN]
    n = max(len(ra), len(rb)); tot += n - 1
    for i in range(1, n):
        x = ra[i] if i < len(ra) else None
        y = rb[i] if i < len(rb) else None
        if x is None or y is None:
            diff += 1; print("ЛИШНЯЯ СТРОКА", os.path.basename(fa), i+1); continue
        if x == y: continue
        if [x[k] for k in keep] == [y[k] for k in keep]:
            timing_only += 1; continue
        diff += 1
        if diff <= 15:
            cols = [hdr[k] for k in range(min(len(x),len(y))) if x[k] != y[k]]
            print("%s:%d %s — столбцы %s" % (os.path.basename(fa), i+1, x[0], cols))
            for c in cols[:4]:
                k = hdr.index(c); print("    %s: A=%s | B=%s" % (c, x[k][:120], y[k][:120]))
print("файлов %d, строк %d, расхождений %d (только время ms/cpu_ms: %d, не считаются)" % (files, tot, diff, timing_only))
