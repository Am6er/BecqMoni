# -*- coding: utf-8 -*-
# П32 12.09.2026, `A64`: ГДЕ внутри полосы «край…пик» сидит перебор — подполосы одного лога Geant4
# против одного или нескольких наших CSV (то же чтение, что cmp_p32.py; бины ВКЛЮЧИТЕЛЬНО).
#   python subbands.py <g4.log> <ours1.csv> [<ours2.csv> ...]
# Подполосы выбираются по энергии узла (последний бин нашего CSV): для 662 — 478…520 / 521…560 /
# 561…600 / 601…625 / 626…640 (K-вылет иода 633.5) / 641…655 / 656…660; для 1332.5 — 1119…1180 /
# 1181…1240 / 1241…1280 / 1281…1298 / 1299…1310 (K-вылет 1304) / 1311…1325 / 1326…1331.
import io, math, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')


def read_ours(path):
    h = {}
    n = None
    for line in io.open(path, encoding='utf-8-sig'):
        p = line.strip().split(',')
        if len(p) != 2 or p[0] == 'keV':
            continue
        h[int(round(float(p[0])))] = float(p[1])
    txt = path[:-4] + '.txt'
    if os.path.isfile(txt):
        for line in io.open(txt, encoding='utf-8-sig', errors='replace'):
            m = re.search(r'историй (\d+),', line)
            if m:
                n = int(m.group(1))
    return h, n


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
    return {k: v / d for k, v in h.items()}, d


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k <= hi)


def cell(a, na, b, nb):
    if not b or not a:
        return '—'
    d = 100.0 * (a / b - 1.0)
    s = 100.0 * math.sqrt(1.0 / (na * a) + 1.0 / (nb * b))
    return '%+.2f (σ %.2f)' % (d, s)


g4, decays = read_g4(sys.argv[1])
subs = {
    662: ((478, 520), (521, 560), (561, 600), (601, 625), (626, 640), (641, 655), (656, 660), (478, 660)),
    1333: ((1119, 1180), (1181, 1240), (1241, 1280), (1281, 1298), (1299, 1310), (1311, 1325), (1326, 1331), (1119, 1331)),
}
first = True
for f in sys.argv[2:]:
    o, n = read_ours(f)
    p = max(o)
    sb = subs[662 if p < 1000 else 1333]
    if first:
        print('| файл | ' + ' | '.join('%d…%d' % s for s in sb) + ' |')
        print('|---|' + '---|' * len(sb))
        first = False
    cells = [cell(band(o, lo, hi), n, band(g4, lo, hi), decays) for lo, hi in sb]
    print('| %s | %s |' % (os.path.basename(f), ' | '.join(cells)))
print()
print('доля подполосы от полосы край…пик у G4: ' + ', '.join(
    '%d…%d %.1f %%' % (lo, hi, 100.0 * band(g4, lo, hi) / band(g4, sb[-1][0], sb[-1][1])) for lo, hi in sb[:-1]))
