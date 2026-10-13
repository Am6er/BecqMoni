# -*- coding: utf-8 -*-
"""П93: ПОЛНАЯ таблица diff эталона витрины FSA (`T260`) — прежний эталон против нового,
без обрезки по 60 строк, которую делает сторож при печати.

    python handover/p93-s176/showcase_diff.py <каталог прежнего эталона> <каталог нового> > showcase_diff.md

Сравнивает `bands` (полосы энергии × компонент/мерка), `rates` (component/...), `head_lines`
(строки окна отчёта) — числа с допуском rel 1e-9 / abs 1e-6 (как у сторожа), строки дословно.
Печатает по паре: число расхождений, все строки |Δ| ≥ 1 % и сводку остальных, крупнейшие.
"""
import io, json, os, sys, glob
for s in (sys.stdout,): s.reconfigure(encoding='utf-8', errors='replace')
A, B = sys.argv[1], sys.argv[2]
REL, ABS = 1e-9, 1e-6

def walk(prefix, x, out):
    if isinstance(x, dict):
        for k, v in x.items(): walk(prefix + (str(k),), v, out)
    elif isinstance(x, list):
        for i, v in enumerate(x): walk(prefix + (str(i),), v, out)
    else:
        out[prefix] = x

def close(a, b):
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        return abs(a - b) <= max(ABS, REL * max(abs(a), abs(b)))
    return a == b

total = 0
print('# Витрина FSA: прежний эталон (%s) → новый (%s)\n' % (os.path.basename(os.path.normpath(A)), os.path.basename(os.path.normpath(B))))
print('| пара | расхождений | из них abs Δ ≥ 1 % | крупнейшие по abs Δ % |')
print('|---|---|---|---|')
details = []
for fa in sorted(glob.glob(os.path.join(A, '*.json'))):
    name = os.path.basename(fa)[:-5]
    fb = os.path.join(B, os.path.basename(fa))
    with io.open(fa, encoding='utf-8') as f: da = json.load(f)
    with io.open(fb, encoding='utf-8') as f: db = json.load(f)
    ra, rb = {}, {}
    for sec in ('bands', 'rates', 'head_lines'):
        walk((sec,), da.get(sec), ra); walk((sec,), db.get(sec), rb)
    diffs = []
    for k in sorted(set(ra) | set(rb)):
        x, y = ra.get(k), rb.get(k)
        if k not in ra or k not in rb or not close(x, y):
            if isinstance(x, (int, float)) and isinstance(y, (int, float)) and not isinstance(x, bool):
                pct = (y - x) / abs(x) * 100 if x else float('inf')
                diffs.append((k, x, y, pct))
            else:
                diffs.append((k, x, y, None))
    total += len(diffs)
    big = [d for d in diffs if d[3] is not None and abs(d[3]) >= 1.0 and d[3] != float('inf')]
    top = sorted([d for d in diffs if d[3] is not None and d[3] != float('inf')], key=lambda d: -abs(d[3]))[:4]
    print('| %s | %d | %d | %s |' % (name.replace('__', ' / '), len(diffs), len(big),
          '; '.join('%s %+.2f %%' % ('/'.join(d[0][1:]), d[3]) for d in top) if top else '—'))
    details.append((name, diffs, big))
print('\nвсего расхождений: %d\n' % total)
for name, diffs, big in details:
    if not diffs: continue
    print('## %s — %d расхождений\n' % (name.replace('__', ' / '), len(diffs)))
    print('| раздел | ключ | было | стало | Δ % |')
    print('|---|---|---|---|---|')
    for k, x, y, pct in sorted(diffs, key=lambda d: (-(abs(d[3]) if d[3] not in (None, float('inf')) else 1e9))):
        if pct is None:
            print('| %s | %s | `%s` | `%s` | текст |' % (k[0], '/'.join(k[1:]), str(x)[:80], str(y)[:80]))
        else:
            print('| %s | %s | %.6g | %.6g | %+.3f |' % (k[0], '/'.join(k[1:]), x, y, pct))
    print()
