# П70 (AMBER30): сверка rates.csv двух прогонов FsaStackShot — все столбцы, кроме share_pct, дословно.
#   python compare_rates.py <a.csv> <b.csv>
# Печатает число строк, число расхождений (кроме share_pct) и таблицу share_pct по компонентам.
import csv
import sys

SKIP = {'share_pct'}


def load(path):
    rows = {}
    with open(path, encoding='utf-8', newline='') as f:
        for r in csv.DictReader(f):
            rows[(r['section'], r['name'])] = r
    return rows


a, b = load(sys.argv[1]), load(sys.argv[2])
keys = sorted(set(a) | set(b))
diff = 0
for k in keys:
    ra, rb = a.get(k), b.get(k)
    if ra is None or rb is None:
        # раздел untied_tail есть только в плече Б — законно, считается отдельно
        if k[0] == 'untied_tail':
            continue
        print('ТОЛЬКО В', 'A' if rb is None else 'B', k)
        diff += 1
        continue
    for col in ra:
        if col in SKIP:
            continue
        if ra[col] != rb.get(col):
            print('РАСХОЖДЕНИЕ', k, col, ra[col], '!=', rb.get(col))
            diff += 1
print('строк A %d, B %d; расхождений (кроме share_pct): %d' % (len(a), len(b), diff))
print('share_pct:')
for k in keys:
    if k[0] != 'component':
        continue
    print('  %-14s A %-10s B %-10s' % (k[1], a.get(k, {}).get('share_pct', '-')[:9], b.get(k, {}).get('share_pct', '-')[:9]))
for k in keys:
    if k[0] == 'untied_tail':
        print('  хвост', k[1], 'A', a.get(k, {}).get('peak_counts', '-'), 'B', b.get(k, {}).get('peak_counts', '-'))
sys.exit(1 if diff else 0)
