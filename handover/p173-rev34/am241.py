# -*- coding: utf-8 -*-
"""П173: Am-241 по прогонам — активность (decay_s), z, пол по кривой из library_note, χ²/ndf.
    python am241.py <dir1> <dir2> ...
Паспорт (манифест, распад учтён — журнал П164 §1.2): G1S16_*: 118 000 Бк ±5 % на 03.12.2013 → ≈117 500;
G1S24_*: 103 100 Бк ±3 % на 30.03.2021 → ≈102 560. У прочих паспорта нет — печатается только сдвиг.
"""
import csv, glob, os, re, sys

PASS = {'G1S16_Am241_P5': 117500.0, 'G1S16_Am241_P25': 117500.0, 'G1S24_Am241_P5': 102560.0}
pat = re.compile(r'ПОЛОМ ПО КРИВОЙ ([\d.]+)')

def load(d):
    comp, runs = {}, {}
    for f in glob.glob(os.path.join(d, '*_spline_components.csv')):
        for row in csv.DictReader(open(f, encoding='utf-8-sig')):
            if row['component'].startswith('Am-241') and 'Am241' in row['spectrum']:
                comp[row['spectrum']] = row
    for f in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for row in csv.DictReader(open(f, encoding='utf-8-sig')):
            m = pat.search(row.get('library_note', ''))
            runs[row['spectrum']] = (row['chi2ndf'], m.group(1) if m else '-')
    return comp, runs

dirs = sys.argv[1:]
data = [load(d) for d in dirs]
names = sorted(set().union(*[set(c) for c, _ in data]))
for k in names:
    print(k)
    for d, (c, r) in zip(dirs, data):
        row = c.get(k)
        if not row:
            print('   %-28s нет компоненты Am-241' % os.path.basename(d)); continue
        a = float(row['decay_s'])
        pas = PASS.get(k)
        dev = ('%+.1f %% паспорта' % (100 * (a / pas - 1))) if pas else ''
        chi, fl = r.get(k, ('-', '-'))
        print('   %-28s A=%10.1f Бк  z=%7s  χ²/ndf=%8s  пол=%6s кэВ  %s'
              % (os.path.basename(d), a, row['z'], chi, fl, dev))
