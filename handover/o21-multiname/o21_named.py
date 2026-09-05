# -*- coding: utf-8 -*-
"""Поимённые сцены к выбору окна: америций (`S64` случай «а») и наследники
снятых плутониевых (`A227`) — что попадёт в список при гандикапе HAND."""
import csv, sys, collections, os
sys.path.insert(0, os.path.abspath('tools/CORPUS/scripts/c1'))
import truth
sys.stdout.reconfigure(encoding='utf-8')
MINY, MAXMISS = 0.1, 1.5
HAND = float(sys.argv[3]) if len(sys.argv) > 3 else 0.10

scene = truth.load()
lab = {}
for r in csv.DictReader(open(sys.argv[1], encoding='utf-8-sig', newline='')):
    lab[(r['spectrum'], r['peak_kev'])] = r
riv = collections.defaultdict(list)
for r in csv.DictReader(open(sys.argv[2], encoding='utf-8-sig', newline='')):
    riv[(r['spectrum'], r['peak_kev'])].append(r)

def names(k, w):
    cs = sorted((c for c in riv.get(k, [])
                 if (float(c['cand_intensity_pct']) == 0.0
                     or float(c['cand_intensity_pct']) >= MINY)
                 and float(c['cand_miss_fwhm']) <= MAXMISS),
                key=lambda c: float(c['cand_miss_kev']))
    out = []
    if cs:
        base = float(cs[0]['cand_miss_fwhm'])
        for c in cs:
            if float(c['cand_miss_fwhm']) - base > HAND + 1e-9: continue
            if c['cand_name'] not in out: out.append(c['cand_name'])
    if w in out: out.remove(w)
    return [w] + out

def show(title, pred):
    print(title)
    n = mult = 0
    for k, r in sorted(lab.items()):
        w = r['nuclide']
        if not w or not pred(k, r): continue
        n += 1
        nm = names(k, w)
        if len(nm) > 1: mult += 1
        print('  %-24s пик %9s ПШПВ %7s -> %s' % (k[0], k[1], r['fwhm_kev'], ' / '.join(nm)))
    print('  ИТОГО %d, из них со списком длиннее одного %d' % (n, mult))
    print()

print('гандикап = %.2f ПШПВ' % HAND)
print()
show('(`S64`, случай «а») спектры америция — все подписи ниже 80 кэВ:',
     lambda k, r: 'Am241' in k[0] and float(r['peak_kev']) < 80)
show('(`A227`) наследники снятых плутониевых — U-235 145.0:',
     lambda k, r: r['nuclide'] == 'U-235' and r['line_kev'] == '145.000')
show('(`A227`) наследники снятых плутониевых — I-131 364.0:',
     lambda k, r: r['nuclide'] == 'I-131' and r['line_kev'] == '364.000')
