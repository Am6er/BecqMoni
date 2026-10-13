# П164 — медиана ErrorPercent узлов записанных кривых: по всем узлам и по узлам >= порога (AMBER128).
# python spread.py <spectra_dir> [emin=40]
import os, re, sys, statistics
d = sys.argv[1]
emin = float(sys.argv[2]) if len(sys.argv) > 2 else 40.0
seen = {}
for name in sorted(os.listdir(d)):
    if not name.endswith('.xml'):
        continue
    buf = open(os.path.join(d, name), 'rb').read().decode('utf-8', 'replace')
    i = buf.find('<Efficiency><Guid>')
    if i < 0:
        continue
    m = re.search(r'<Name>(.*?)</Name>', buf[i:i + 300])
    curve = m.group(1) if m else name
    if curve in seen:
        continue
    pts = re.findall(r'<ROIEfficiencyData><Energy>([^<]+)</Energy><Efficiency>([^<]+)</Efficiency><ErrorPercent>([^<]+)</ErrorPercent>', buf[i:])
    e = [(float(a), float(c)) for a, b, c in pts]
    if not e:
        continue
    allm = statistics.median([c for _, c in e])
    hi = [c for a, c in e if a >= emin]
    him = statistics.median(hi) if hi else float('nan')
    lo = [c for a, c in e if a < emin]
    seen[curve] = (len(e), len(lo), allm, him, name)
print(f'{"кривая":26s} узлов  <{emin:g}   медиана_все  медиана_>={emin:g}')
for k, (n, nlo, a, h, name) in sorted(seen.items(), key=lambda kv: -kv[1][2]):
    flag = '  ⛔>5' if a > 5 else ''
    flag2 = '  >5!' if h > 5 else ''
    print(f'{k:26s} {n:5d} {nlo:5d}   {a:8.2f}     {h:8.2f}{flag}{flag2}')
