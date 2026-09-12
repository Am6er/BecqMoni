# -*- coding: utf-8 -*-
# П27: сводка полос для ПРОИЗВОЛЬНОГО набора наших файлов против одного лога Geant4.
#   python bands_cmp.py <g4.log> <ours1.csv> [<ours2.csv> ...]
# Печатает: пик Δ %, полная Δ %, полосы в долях E, а при E ≈ 59.5 — ещё бины 13–25 / 26–31 /
# 32–36 / 37–42 / 43–54 / 55–59 (потолок формы A72).
import io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')


def read_ours(path):
    h = {}
    for line in io.open(path, encoding='utf-8-sig'):
        p = line.strip().split(',')
        if len(p) != 2 or p[0] == 'keV':
            continue
        h[int(round(float(p[0])))] = float(p[1])
    return h


def read_g4(path):
    d = None
    h = {}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m:
            d = int(m.group(3))
        m = re.match(r'HIST (\d+) (\d+)', line)
        if m:
            h[int(m.group(1))] = int(m.group(2))
    return {k: v / d for k, v in h.items()}


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


def pct(a, b):
    return '%+.2f' % (100.0 * (a / b - 1.0)) if b else '—'


g4 = read_g4(sys.argv[1])
low = max(g4) < 100
head = '| файл | пик | полная | 0–25 %E | 25–50 | 50–75 | 75–100 |'
if low:
    head += ' 13–25 | 26–31 | 32–36 | 37–42 | 43–54 | 55–59 |'
print(head)
print('|' + '---|' * (head.count('|') - 1))
for f in sys.argv[2:]:
    o = read_ours(f)
    p = max(o)
    cells = [os.path.basename(f), pct(o.get(p, 0.0), g4.get(p, 0.0)), pct(sum(o.values()), sum(g4.values()))]
    for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
        lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3)
        cells.append(pct(band(o, lo_i, hi_i), band(g4, lo_i, hi_i)))
    if low:
        for lo, hi in ((13, 26), (26, 32), (32, 37), (37, 43), (43, 55), (55, 60)):
            cells.append(pct(band(o, lo, hi), band(g4, lo, hi)))
    print('| ' + ' | '.join(cells) + ' |')
