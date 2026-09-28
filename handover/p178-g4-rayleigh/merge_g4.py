# П178: слить гистограммы нескольких логов g4cf (один шаг бина) в один лог того же вида.
import sys, re
out, logs = sys.argv[1], sys.argv[2:]
h = {}; dec = 0; bins = bk = None
for p in logs:
    for l in open(p, encoding='utf-8', errors='replace'):
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', l)
        if m: bins, bk = m.group(1), m.group(2); dec += int(m.group(3)); continue
        m = re.match(r'HIST\s+(\d+)\s+(\d+)', l)
        if m: h[int(m.group(1))] = h.get(int(m.group(1)), 0) + int(m.group(2))
with open(out, 'w', encoding='utf-8') as f:
    f.write(f'HISTBEGIN bins={bins} bin_kev={bk} decays={dec}\n')
    for i in sorted(h): f.write(f'HIST {i} {h[i]}\n')
    tot = sum(h.values())
    f.write(f'RESULT any={tot} eps_total={tot/dec:.6e}\n')
    f.write(f'RESULT window=0.5 counts=0 eps=0\n')
print(out, dec)
