# -*- coding: utf-8 -*-
# П24: сверка двух каталогов прогона ПО КЛЮЧУ (spectrum + прочие именующие графы), а не по номеру строки —
# чтобы вставка нового спектра (диск) не сдвигала счёт расхождений у соседей. Маска: ms, cpu_ms.
import csv, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
a, b = sys.argv[1], sys.argv[2]
mask = {'ms', 'cpu_ms'}
KEYS = {'runs': ['spectrum'], 'components': ['spectrum', 'component'], 'anchors': ['spectrum', 'line_kev', 'energy_kev', 'kev', 'nuclide'], 'limits': ['spectrum', 'component', 'nuclide']}
tot = dif = onlya = onlyb = 0
difspec = {}
for name in sorted(os.listdir(a)):
    if not name.endswith('.csv'): continue
    pa, pb = os.path.join(a, name), os.path.join(b, name)
    if not os.path.exists(pb): print('MISSING', name); continue
    kind = name.split('_')[-1][:-4]
    ra = list(csv.DictReader(io.open(pa, encoding='utf-8-sig', newline='')))
    rb = list(csv.DictReader(io.open(pb, encoding='utf-8-sig', newline='')))
    hdr = list(ra[0].keys()) if ra else (list(rb[0].keys()) if rb else [])
    keycols = [c for c in KEYS.get(kind, ['spectrum']) if c in hdr]
    def key(r, i, seen):
        k = tuple(r.get(c, '') for c in keycols)
        n = seen.get(k, 0); seen[k] = n + 1
        return k + (n,)
    sa, sb = {}, {}
    da = {key(r, i, sa): r for i, r in enumerate(ra)}
    db = {key(r, i, sb): r for i, r in enumerate(rb)}
    d = 0
    for k in da:
        if k not in db: onlya += 1; difspec.setdefault(k[0], set()).add(name + ':только в A'); continue
        x, y = dict(da[k]), dict(db[k])
        for m in mask: x.pop(m, None); y.pop(m, None)
        if x != y:
            d += 1
            cols = [c for c in x if x.get(c) != y.get(c)]
            difspec.setdefault(k[0], set()).add(name + ':' + ','.join(cols))
    for k in db:
        if k not in da: onlyb += 1; difspec.setdefault(k[0], set()).add(name + ':только в B')
    tot += len(ra); dif += d
    print('%-34s строк %4d / %4d  расхождений по ключу %d' % (name, len(ra), len(rb), d))
print('ИТОГО строк A %d, расхождений %d, только в A %d, только в B %d (ключи: %s)' % (tot, dif, onlya, onlyb, KEYS))
for s in sorted(difspec):
    print('  %s: %s' % (s, '; '.join(sorted(difspec[s]))[:300]))
