# -*- coding: utf-8 -*-
r"""П97 — сводка лестницы по выводу ladder.py: лучшие/худшие, RC103 отдельно, сдвиги состава.
  python handover/p97-physics19/ladder_summary.py <ladder_*.txt> [--top=8]
"""
import io
import re
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def main(argv):
    path = argv[0]
    top = 8
    for a in argv[1:]:
        if a.startswith('--top='):
            top = int(a[6:])
    rows = []
    for line in io.open(path, encoding='utf-8'):
        m = re.match(r'^(\S+)\s+(known|unknown)\s+([\d.]+)\s+([\d.]+)\s+([+-][\d.]+)\s+(.*)$', line)
        if m:
            rows.append((m.group(1), float(m.group(3)), float(m.group(4)), float(m.group(5)), m.group(6).strip()))
    rows.sort(key=lambda r: r[3])
    print(u'сдвинулось %d: улучшилось %d, ухудшилось %d, |Δ| ≤ 0.15 %%: %d, |Δ| ≥ 1 %%: %d'
          % (len(rows), sum(1 for r in rows if r[3] < 0), sum(1 for r in rows if r[3] > 0),
             sum(1 for r in rows if abs(r[3]) <= 0.15), sum(1 for r in rows if abs(r[3]) >= 1.0)))
    print(u'--- лучшие %d:' % top)
    for r in rows[:top]:
        print(u'%-26s %8.4f -> %8.4f %+7.3f %%  %s' % (r[0], r[1], r[2], r[3], r[4][:90]))
    print(u'--- худшие %d:' % top)
    for r in rows[-top:]:
        print(u'%-26s %8.4f -> %8.4f %+7.3f %%  %s' % (r[0], r[1], r[2], r[3], r[4][:90]))
    print(u'--- RC103, ASN16, AS80 (мелкие кристаллы и диск):')
    for r in rows:
        if r[0].startswith(('RC103', 'ASN16', 'AS80')):
            print(u'%-26s %8.4f -> %8.4f %+7.3f %%  %s' % (r[0], r[1], r[2], r[3], r[4][:140]))
    print(u'--- состав (share_pct > 0.05 п.п.):')
    n = 0
    for r in rows:
        if ';' in r[4]:
            n += 1
            print(u'%-26s %s' % (r[0], r[4].split(';', 1)[1].strip()[:220]))
    print(u'спектров со сдвигом состава: %d' % n)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
