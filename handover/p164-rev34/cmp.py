# П164 — сравнение двух прогонов по *_spline_runs.csv (часть known): Σχ²/ndf, лучше/хуже, крупнейшие сдвиги.
# python cmp.py <A_dir> <B_dir> [--top=N] [--only=mini.csv]
import csv, glob, os, sys

def load(d, only=None):
    r = {}
    for f in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        with open(f, encoding='utf-8-sig') as fh:
            for row in csv.DictReader(fh):
                if row.get('part') != 'known':
                    continue
                if only is not None and row['spectrum'] not in only:
                    continue
                try:
                    r[row['spectrum']] = float(row['chi2ndf'])
                except (ValueError, KeyError):
                    pass
    return r

args = [a for a in sys.argv[1:] if not a.startswith('--')]
top = 10
only = None
for a in sys.argv[1:]:
    if a.startswith('--top='):
        top = int(a[6:])
    if a.startswith('--only='):
        with open(a[7:], encoding='utf-8-sig') as fh:
            only = set()
            for row in csv.reader(fh):
                if row and not row[0].startswith('#'):
                    only.add(row[0].strip())
a = load(args[0], only)
b = load(args[1], only)
common = sorted(set(a) & set(b))
sa = sum(a[k] for k in common)
sb = sum(b[k] for k in common)
print(f"A={args[0]}\nB={args[1]}")
print(f"nA={len(a)} nB={len(b)} common={len(common)}  sumA={sa:.4f}  sumB={sb:.4f}  d={sb - sa:+.4f} ({(sb / sa - 1) * 100:+.3f} %)")
d = sorted((b[k] - a[k], k) for k in common)
same = sum(1 for x, k in d if x == 0.0)
print(f"worse={sum(1 for x, k in d if x > 0)} better={sum(1 for x, k in d if x < 0)} same(bitwise)={same}")
print('--- worst')
for x, k in d[::-1][:top]:
    if x > 0:
        print(f"  {k:30s} {a[k]:9.4f} -> {b[k]:9.4f} {x:+8.4f}")
print('--- best')
for x, k in d[:top]:
    if x < 0:
        print(f"  {k:30s} {a[k]:9.4f} -> {b[k]:9.4f} {x:+8.4f}")
