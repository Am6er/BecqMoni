# П175: сравнение нашего сырого отклика (G4RawProbe --out) с гистограммой g4cf по полосам.
# python bands.py <our.csv> <g4.log> [границы через запятую]
import sys, re, math
our, g4p = sys.argv[1], sys.argv[2]
edges = [float(x) for x in (sys.argv[3] if len(sys.argv) > 3 else '0,20,27,33,40,50,55,58,59.45,60.05').split(',')]
rows = []
with open(our, encoding='utf-8-sig') as fh:
    next(fh)
    for line in fh:
        k, v = line.strip().split(',')[:2]
        rows.append((float(k), float(v)))
peakE, peak = rows[-1]
cont = rows[:-1]
g4 = {}; dec = None
for line in open(g4p, encoding='utf-8', errors='replace'):
    m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
    if m: bk = float(m.group(2)); dec = int(m.group(3)); continue
    m = re.match(r'HIST\s+(\d+)\s+(\d+)', line)
    if m: g4[int(m.group(1))] = int(m.group(2))
def band(lo, hi):
    o = sum(v for k, v in cont if lo <= k < hi) + (peak if lo <= peakE < hi else 0.0)
    c = sum(v for i, v in g4.items() if lo <= i * bk < hi)
    return o, c / dec, (math.sqrt(c) / c if c else float('nan'))
print(f'{"полоса, кэВ":>16} {"наша":>11} {"G4":>11} {"±G4":>6} {"наша/G4":>8} {"разность":>11}')
to = tg = 0
for lo, hi in zip(edges[:-1], edges[1:]):
    o, g, s = band(lo, hi); to += o; tg += g
    print(f'{lo:7.2f}–{hi:7.2f} {o:11.4e} {g:11.4e} {100*s:5.2f}% {o/g if g else float("nan"):8.4f} {o-g:+11.3e}')
print(f'{"итого":>16} {to:11.4e} {tg:11.4e}        {to/tg:8.4f} {to-tg:+11.3e}')
