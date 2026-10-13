# П164 — пол полосы (из library_note) и χ² по спектрам в нескольких прогонах.
# python floors.py <dir1> <dir2> ... [--spectra=a,b,c]
import csv, glob, os, re, sys

dirs = [a for a in sys.argv[1:] if not a.startswith('--')]
want = None
for a in sys.argv[1:]:
    if a.startswith('--spectra='):
        want = a[len('--spectra='):].split(',')
pat = re.compile(r'ПОЛОМ ПО КРИВОЙ ([\d.]+)')


def load(d):
    r = {}
    for f in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        with open(f, encoding='utf-8-sig') as fh:
            for row in csv.DictReader(fh):
                if row.get('part') != 'known':
                    continue
                m = pat.search(row.get('library_note', ''))
                r[row['spectrum']] = (float(row['chi2ndf']), m.group(1) if m else '-', row)
    return r


runs = [load(d) for d in dirs]
names = want or sorted(runs[0])
print('spectrum'.ljust(28) + ''.join(os.path.basename(d.rstrip('/\\')).ljust(22) for d in dirs))
for k in names:
    cells = []
    for r in runs:
        if k in r:
            cells.append(f"{r[k][0]:8.4f} пол {r[k][1]:>7s}".ljust(22))
        else:
            cells.append('-'.ljust(22))
    print(k.ljust(28) + ''.join(cells))
