# П164 — окна вокруг линии: наш сырой отклик (G4RawProbe --out, последняя строка = пик) против гистограммы g4cf.
# python win.py <our.csv> <g4.log> <E_keV> <w_keV>[,<w2>...]
import sys, re, math

our_path, g4_path, e = sys.argv[1], sys.argv[2], float(sys.argv[3])
ws = [float(x) for x in sys.argv[4].split(',')]

rows = []
with open(our_path, encoding='utf-8-sig') as fh:
    next(fh)
    for line in fh:
        k, v = line.strip().split(',')[:2]
        rows.append((float(k), float(v)))
peak = rows[-1][1]
cont = rows[:-1]
total_our = sum(v for _, v in rows)

g4 = {}
nbins = binkev = decays = None
with open(g4_path, encoding='utf-8', errors='replace') as fh:
    for line in fh:
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m:
            nbins, binkev, decays = int(m.group(1)), float(m.group(2)), int(m.group(3))
            continue
        m = re.match(r'HIST\s+(\d+)\s+(\S+)', line)
        if m:
            g4[int(m.group(1))] = float(m.group(2))
        m = re.match(r'RESULT any=(\d+) eps_total=(\S+)', line)
        if m:
            g4_any, g4_total = int(m.group(1)), float(m.group(2))
        m = re.match(r'RESULT window=(\S+) counts=(\d+) eps=(\S+)', line)
        if m:
            g4_win05 = (int(m.group(2)), float(m.group(3)))
if decays is None:
    sys.exit('в логе g4cf нет HISTBEGIN')
vals = list(g4.values())
is_counts = all(abs(v - round(v)) < 1e-9 for v in vals[:50])


def g4sum(lo, hi):
    s = 0.0
    for i, v in g4.items():
        kev = i * binkev
        if lo <= kev <= hi:
            s += v
    return s


print(f'E={e} кэВ; ours: peak {peak:.5e}, total {total_our:.5e}; g4: decays {decays}, bin {binkev}, total {g4_total:.5e} (any {g4_any}), '
      f'окно ±0.5 {g4_win05[1]:.5e} ({g4_win05[0]} соб.)')
for w in ws:
    ours = peak + sum(v for k, v in cont if e - w <= k < e)
    c = g4sum(e - w, e + 0.5 * binkev + 1e-9)
    g = c / decays if is_counts else c
    sg = math.sqrt(c) / decays if is_counts else float('nan')
    print(f'  окно [E-{w:.3f}, E]: ours {ours:.5e}  g4 {g:.5e} ± {100 * sg / g:.2f} %  ours/g4 {ours / g:.4f}')
print(f'  полная: ours/g4 {total_our / g4_total:.4f}')
