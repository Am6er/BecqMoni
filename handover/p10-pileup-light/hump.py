import csv, io, os, sys
def load(path):
    rows = list(csv.DictReader(io.open(path, encoding='utf-8-sig')))
    e = [float(r['keV']) for r in rows]
    return rows, e
def smooth(v, w):
    n = len(v); out = [0.0]*n
    for i in range(n):
        a = max(0, i-w); b = min(n, i+w+1)
        out[i] = sum(v[a:b])/(b-a)
    return out
def stats(e, v, lo, hi, base_lo, base_hi):
    # linear baseline between base_lo..base_hi means over +-3 channels
    def near(x):
        idx = min(range(len(e)), key=lambda i: abs(e[i]-x)); return sum(v[max(0,idx-3):idx+4])/len(v[max(0,idx-3):idx+4])
    b0, b1 = near(base_lo), near(base_hi)
    best = None; num = 0.0; den = 0.0
    for i in range(len(e)):
        if lo <= e[i] <= hi:
            base = b0 + (b1-b0)*(e[i]-base_lo)/(base_hi-base_lo)
            y = v[i] - base
            if best is None or y > best[1]: best = (e[i], y)
            if y > 0: num += e[i]*y; den += y
    return best[0], (num/den if den else float('nan')), den
spec = sys.argv[1]; arms = sys.argv[2].split(','); w = int(sys.argv[3])
lo, hi, blo, bhi = 1290, 1370, 1260, 1400
for a in arms:
    p = "tools/pie/out_p10_pileup_%s/curves/%s_curves.csv" % (a, spec)
    if not os.path.exists(p): print(a, "—"); continue
    rows, e = load(p)
    fit = smooth([float(r['fit']) for r in rows], w)
    model = smooth([float(r['model']) for r in rows], w)
    line = ["%-10s" % a]
    m, c, area = stats(e, fit, lo, hi, blo, bhi); line.append("измерение: max %.1f цт %.1f (площадь %.0f)" % (m, c, area))
    m, c, area = stats(e, model, lo, hi, blo, bhi); line.append("модель: max %.1f цт %.1f (%.0f)" % (m, c, area))
    if 'pile-up' in rows[0]:
        pu = smooth([float(r['pile-up']) for r in rows], w)
        m, c, area = stats(e, pu, lo, hi, blo, bhi); line.append("образ наложений: max %.1f цт %.1f (%.0f)" % (m, c, area))
    print(" | ".join(line))
