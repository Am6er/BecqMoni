# -*- coding: utf-8 -*-
"""
П102 (`D50`, п. (а) дописки П101). Слабые гамма-линии поставки SandiaDecay (`sandia.decay.xml`,
выход на распад родителя = I·branchRatio, сложено по переходам), которых НЕТ в `nucdb.decay_radiations`
(тип G, допуск 0.5 кэВ) — то есть которых нет и в библиотеке FSA. Для каждого названного родителя:
число линий Sandia, число из них без строки в `decay_radiations` и их Σ I, отдельно по порогам
I ≥ 0.01 % и I ≥ 0.1 %; сильнейшие отсутствующие — списком.

    python d50_missing_lines.py <sandia.decay.xml> <nucdb.sqlite> <out.md> Eu152:152EU Lu176:176LU ...

Ничего не пишет в базы.
"""
import sys, sqlite3
import xml.etree.ElementTree as ET
from collections import defaultdict

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass

XML, NUC, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
WANT = [a.split(':') for a in sys.argv[4:]]
NS = '{sandia.decay.xsd}'
TOL = 0.5

nuc = sqlite3.connect('file:%s?mode=ro' % NUC.replace('\\', '/'), uri=True)


def our_lines(nucid):
    rows = nuc.execute("select energy, intensity from decay_radiations where parent_nucid=? and type_a='G'",
                       (nucid,)).fetchall()
    out = []
    for e, i in rows:
        try:
            out.append((float(str(e).split()[0]), float(str(i).split()[0]) if i not in (None, '') else 0.0))
        except ValueError:
            continue
    return out


def main():
    root = ET.parse(XML).getroot()
    want = {s: n for s, n in WANT}
    lines = defaultdict(lambda: defaultdict(float))
    for tr in root.iter(NS + 'transition'):
        parent = tr.get('parent')
        if parent not in want:
            continue
        try:
            br = float(tr.get('branchRatio') or 0.0)
        except ValueError:
            continue
        for g in tr:
            if g.tag == NS + 'gamma':
                lines[parent][float(g.get('energy'))] += 100.0 * float(g.get('intensity')) * br
    out = ['# `D50` (а) — линии SandiaDecay вне `decay_radiations` (П102, 18.09.2026)', '',
           'Допуск сопоставления %.1f кэВ; I — %% на распад родителя (Sandia).' % TOL, '',
           '| родитель | линий Sandia (I ≥ 0.01 %) | у нас (тип G) | нет у нас, I ≥ 0.01 % | Σ I их, % | нет у нас, I ≥ 0.1 % | Σ I их, % | сильнейшие отсутствующие (кэВ: I %) |',
           '|---|---|---|---|---|---|---|---|']
    for sym, nucid in WANT:
        ours = our_lines(nucid)
        sl = [(e, i) for e, i in lines[sym].items() if i >= 0.01]
        missing = [(e, i) for e, i in sl if not any(abs(e - oe) < TOL for oe, _ in ours)]
        m01 = [(e, i) for e, i in missing if i >= 0.1]
        top = sorted(missing, key=lambda t: -t[1])[:8]
        out.append('| %s (%s) | %d | %d | %d | %.3f | %d | %.3f | %s |' % (
            sym, nucid, len(sl), len(ours), len(missing), sum(i for _, i in missing), len(m01),
            sum(i for _, i in m01), '; '.join('%.1f: %.3f' % t for t in top)))
    with open(OUT, 'w', encoding='utf-8') as f:
        f.write('\n'.join(out) + '\n')
    print('\n'.join(out))


if __name__ == '__main__':
    main()
