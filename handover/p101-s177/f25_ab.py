# -*- coding: utf-8 -*-
"""П101: сравнение перечня F25 (сумм-пики и CF линий) двух логов `FsaCascadeProbe --describe`.

    python f25_ab.py <cf_a.log> <cf_b.log> [--nuclide=Eu-152] [--min=1e-7]

Печатает по нуклиду: суммы (слагаемые, площадь А, Б, Δ %, в образе/срез), CF линий А/Б, и сводку:
сколько сумм сдвинулось, Σ площади в образе А/Б.
"""
import io, re, sys
for s in (sys.stdout,): s.reconfigure(encoding='utf-8', errors='replace')
alog, blog = sys.argv[1], sys.argv[2]
nuclide = None; minarea = 1e-7
for a in sys.argv[3:]:
    if a.startswith('--nuclide='): nuclide = a[10:]
    elif a.startswith('--min='): minarea = float(a[6:])

def f25(path):
    sums = {}; cf = {}; order = []
    comp = None
    with io.open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            m = re.match(r'компонент: (\S+)', line.strip())
            if m: comp = m.group(1); continue
            m = re.match(r'\s+([\d.]+)\s+([\d.+]+)\s+(\S+)\s+([\d.E+-]+)(\s+\(не в образе: (\S+)\))?\s*$', line)
            if m:
                key = (m.group(3), m.group(2))
                sums[key] = dict(apparent=float(m.group(1)), area=float(m.group(4)), dropped=m.group(6))
                if key not in order: order.append(key)
                continue
            m = re.match(r'\s+([\d.]+)\s+(\S+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.E+-]+)\s*$', line)
            if m and comp:
                cf[(m.group(2), float(m.group(1)))] = (float(m.group(3)), float(m.group(4)), float(m.group(5)), float(m.group(6)))
    return sums, cf, order

sa, cfa, oa = f25(alog); sb, cfb, ob = f25(blog)
nucs = sorted(set(k[0] for k in sa) | set(k[0] for k in sb))
if nuclide: nucs = [nuclide]
for n in nucs:
    keys = [k for k in oa if k[0] == n] + [k for k in ob if k[0] == n and k not in oa]
    keys.sort(key=lambda k: -max(sa.get(k, {}).get('area', 0), sb.get(k, {}).get('area', 0)))
    print('\n## %s: сумм А %d, Б %d' % (n, sum(1 for k in sa if k[0] == n), sum(1 for k in sb if k[0] == n)))
    print('| E вид., кэВ | слагаемые | А, на распад | Б, на распад | Δ % | образ А → Б |')
    print('|---|---|---|---|---|---|')
    moved = 0; ia = ib = 0.0
    for k in keys:
        a = sa.get(k); b = sb.get(k)
        aa = a['area'] if a else 0.0; bb = b['area'] if b else 0.0
        if max(aa, bb) < minarea: continue
        if a and not a['dropped']: ia += aa
        if b and not b['dropped']: ib += bb
        d = (bb / aa - 1) * 100 if aa > 0 else float('nan')
        if abs(bb - aa) > 1e-12 * max(aa, bb, 1e-300): moved += 1
        st = '%s → %s' % ('·' if a and not a['dropped'] else ('×' + (a['dropped'] or '') if a else '—'),
                           '·' if b and not b['dropped'] else ('×' + (b['dropped'] or '') if b else '—'))
        print('| %.2f | %s | %.4e | %.4e | %+.2f | %s |' % ((b or a)['apparent'], k[1], aa, bb, d, st))
    print('\nсдвинулось %d; Σ площади в образе А %.4e, Б %.4e (%+.2f %%)' % (moved, ia, ib, (ib / ia - 1) * 100 if ia else float('nan')))
    lines = sorted(set(k for k in cfa if k[0] == n) | set(k for k in cfb if k[0] == n), key=lambda k: k[1])
    if lines:
        print('\n| линия, кэВ | CF А | CF Б | вынос А / Б | влёт А / Б |')
        print('|---|---|---|---|---|')
        for k in lines:
            a = cfa.get(k); b = cfb.get(k)
            if not a or not b: continue
            print('| %.2f | %.4f | %.4f | %.4f / %.4f | %.4f / %.4f |' % (k[1], a[0], b[0], a[1], b[1], a[2], b[2]))
