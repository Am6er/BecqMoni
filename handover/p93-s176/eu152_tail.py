# -*- coding: utf-8 -*-
"""П93: сколько сумм-пиков нужно Eu-152 на G1S_point5 для доли 95…99.5 % Σ и что лежит в хвосте за 96."""
import io, re, sys
for s in (sys.stdout,): s.reconfigure(encoding='utf-8', errors='replace')
ROW = re.compile(r'^\s+([0-9.]+)\s+(\S+)\s+(\S+)\s+([0-9.]+E[+-]?\d+)(\s+\(не в образе[^)]*\))?\s*$')
XR = ('39.52', '40.12', '45.52', '46.58', '5.63', '6.06', '6.71', '7.18', '8.30', '7.46')
for path in sys.argv[1:]:
    comp = None; rows = []
    for line in io.open(path, encoding='utf-8', errors='replace'):
        if line.startswith('компонент: '): comp = line[11:].strip(); continue
        m = ROW.match(line)
        if m and comp == 'Eu-152': rows.append((float(m.group(4)), m.group(2), bool(m.group(5))))
    rows.sort(key=lambda r: -r[0]); tot = sum(r[0] for r in rows)
    cum = 0; marks = {}
    for i, r in enumerate(rows, 1):
        cum += r[0]
        for q in (0.95, 0.97, 0.98, 0.99, 0.995):
            if q not in marks and cum >= q * tot: marks[q] = i
    print(path, 'всего', len(rows), 'Σ %.4E' % tot)
    print('  пар для доли:', {k: v for k, v in sorted(marks.items())})
    for n in (24, 48, 64, 96, 128, 160, 200, 250):
        print('  при %3d парах: доля %.2f %%' % (n, 100 * sum(r[0] for r in rows[:n]) / tot))
    tail = rows[96:]
    xr = [r for r in tail if any(('+' + p) in r[1] or r[1].startswith(p + '+') for p in XR)]
    print('  хвост за 96: %d пар, Σ %.3E (%.2f %%), из них с рентгеном/низкой линией %d пар Σ %.3E' % (
        len(tail), sum(r[0] for r in tail), 100 * sum(r[0] for r in tail) / tot, len(xr), sum(r[0] for r in xr)))
    print('  первые 10 хвоста:', [(r[1], '%.2E' % r[0]) for r in tail[:10]])
    print('  троек в образе %d, всего %d' % (sum(1 for r in rows[:96] if r[1].count('+') == 2), sum(1 for r in rows if r[1].count('+') == 2)))
