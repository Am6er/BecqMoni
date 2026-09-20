# -*- coding: utf-8 -*-
"""П93 (`S176`): сводка среза сумм-пиков из лога `FsaCascadeProbe --describe`.

    python handover/p93-s176/sumcut_tally.py <лог> [<лог2> ...]

По каждому компоненту: сумм в образе / посчитано (в образе + «не в образе»),
Σ площади в образе, Σ всего, доля; отдельно — где стоит пара 444+1086 Eu-152.
Читает разделы «Coincidence sum peaks» и «посчитано, но в образ НЕ идёт».
"""
import io, re, sys
for s in (sys.stdout,): s.reconfigure(encoding='utf-8', errors='replace')
ROW = re.compile(r'^\s+([0-9.]+)\s+(\S+)\s+(\S+)\s+([0-9.]+E[+-]?\d+)(\s+\(не в образе[^)]*\))?\s*$')
def tally(path):
    comp = None; rows = {}
    with io.open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.rstrip('\r\n')
            if line.startswith('компонент: '):
                comp = line[len('компонент: '):].strip(); rows.setdefault(comp, [])
                continue
            m = ROW.match(line)
            if m and comp is not None:
                e, parts, nuc, area, dropped = m.groups()
                rows[comp].append((float(e), parts, nuc, float(area), bool(dropped)))
    print(path)
    print('  %-10s %6s %6s %12s %12s %8s   %s' % ('компонент', 'образ', 'всего', 'Σ образ', 'Σ всего', 'доля %', 'слабейшая в образе / сильнейшая вне'))
    for comp, rs in rows.items():
        if not rs: continue
        kept = [r for r in rs if not r[4]]; drop = [r for r in rs if r[4]]
        sk = sum(r[3] for r in kept); sa = sk + sum(r[3] for r in drop)
        wk = min(kept, key=lambda r: r[3]) if kept else None
        sd = max(drop, key=lambda r: r[3]) if drop else None
        print('  %-10s %6d %6d %12.4E %12.4E %8.3f   %s / %s' % (
            comp, len(kept), len(rs), sk, sa, 100.0 * sk / sa if sa else 0.0,
            ('%s %.3E' % (wk[1], wk[3])) if wk else '-', ('%s %.3E' % (sd[1], sd[3])) if sd else '-'))
        for r in rs:
            if r[1] in ("443.97+1085.86", "1085.86+443.97"):
                print('      444+1086: %s %.3E %s' % (r[1], r[3], 'НЕ В ОБРАЗЕ' if r[4] else 'в образе'))
for p in sys.argv[1:]:
    tally(p)
