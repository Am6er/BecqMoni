# -*- coding: utf-8 -*-
# П17: положение K-рентгена и промах 81/121.8 над хордой — из <грп>_spline_anchors.csv плеч a/b.
# shift_kev = измерено − модель (до линейной привязки; промах над хордой к ней инвариантен).
import csv, glob, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp17_out'
arms = sys.argv[1:] or ['a', 'b']

def load(arm):
    out = {}
    for p in glob.glob(os.path.join(root, arm, '*_spline_anchors.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out.setdefault(row['spectrum'], {})[round(float(row['line_kev']), 1)] = row
    return out

def shift(d, e):
    r = d.get(e)
    return None if r is None else float(r['shift_kev'])

def chord_miss(d, lo, hi, mid):
    a, b, m = shift(d, lo), shift(d, hi), shift(d, mid)
    if a is None or b is None or m is None:
        return None
    return m - (a + (mid - lo) / (hi - lo) * (b - a))

data = {arm: load(arm) for arm in arms}
# 1. K-опоры: остаток (изм − мод) и принятость
print('K-рентген и линии низа: shift = измерено − модель, кэВ; в скобках used/отказ')
rows = [
    ('G1S16_Cs137_P5', [32.2, 661.7]), ('G1S24_Cs137_P5', [32.2, 661.7]),
    ('G1S16_Ba133_P5', [31.0, 81.0, 302.9, 356.0]), ('G1S24_Ba133_P5', [31.0, 81.0, 302.9, 356.0]),
    ('G1S16_Ba133_P25', [31.0, 81.0, 302.9, 356.0]),
    ('G1S16_Eu152_P5', [40.1, 121.8, 244.7, 344.3, 1408.0]), ('G1S24_Eu152_P5', [40.1, 121.8, 244.7, 344.3, 1408.0]),
    ('G1S16_Eu152_P25', [40.1, 121.8, 244.7, 344.3, 1408.0]),
    ('G1S16_Cd109_P5', [22.1, 88.0]), ('G1S24_Cd109_P5', [22.1, 88.0]),
    ('G1S16_Am241_P5', [26.3, 59.5]), ('G1S16_Co57_P5', [122.1, 136.5]),
]
for spec, lines in rows:
    parts = []
    for e in lines:
        cell = []
        for arm in arms:
            d = data[arm].get(spec, {})
            r = d.get(e)
            if r is None:
                # ближайшая линия в ±0.3 кэВ
                near = [k for k in d if abs(k - e) <= 0.3]
                r = d[near[0]] if near else None
            cell.append('-' if r is None else '%+.2f(%s%s)' % (float(r['shift_kev']), r['used'], (':' + r['refusal']) if r['refusal'] else ''))
        parts.append('%6.1f: %s' % (e, ' / '.join(cell)))
    print('%-18s %s' % (spec, ' | '.join(parts)))

print()
print('промах над хордой (изм − мод), кэВ, плечи %s:' % ' / '.join(arms))
for spec, lo, hi, mid in [('G1S16_Ba133_P5', 31.0, 356.0, 81.0), ('G1S24_Ba133_P5', 31.0, 356.0, 81.0), ('G1S16_Ba133_P25', 31.0, 356.0, 81.0),
                          ('G1S16_Ba133_P5', 31.0, 356.0, 302.9), ('G1S24_Ba133_P5', 31.0, 356.0, 302.9),
                          ('G1S16_Eu152_P5', 40.1, 1408.0, 121.8), ('G1S24_Eu152_P5', 40.1, 1408.0, 121.8), ('G1S16_Eu152_P25', 40.1, 1408.0, 121.8),
                          ('G1S16_Eu152_P5', 40.1, 1408.0, 344.3), ('G1S24_Eu152_P5', 40.1, 1408.0, 344.3),
                          ('G1S16_Eu152_P5', 121.8, 1408.0, 344.3), ('G1S24_Eu152_P5', 121.8, 1408.0, 344.3), ('G1S16_Eu152_P25', 121.8, 1408.0, 344.3),
                          ('G1S16_Eu152_P5', 121.8, 1408.0, 964.1), ('G1S24_Eu152_P5', 121.8, 1408.0, 964.1),
                          ('G1S16_Th228_P5', 238.6, 2614.5, 583.2), ('G1S24_Th228_P5', 238.6, 2614.5, 583.2), ('G1S16_Th228_P25', 238.6, 2614.5, 583.2),
                          ('G1S16_Th228_P5', 238.6, 2614.5, 860.6), ('G1S24_Th228_P5', 238.6, 2614.5, 860.6)]:
    vals = []
    for arm in arms:
        d = data[arm].get(spec, {})
        def near(e):
            ks = [k for k in d if abs(k - e) <= 0.3]
            return ks[0] if ks else e
        m = chord_miss(d, near(lo), near(hi), near(mid))
        vals.append('-' if m is None else '%+.2f' % m)
    print('%-18s %6.1f над хордой %6.1f↔%6.1f: %s' % (spec, mid, lo, hi, ' / '.join(vals)))

# 2. Σ по всем спектрам: сумма |shift| принятых опор, число принятых
print()
for arm in arms:
    n = used = 0; s = 0.0
    for spec, d in data[arm].items():
        for e, r in d.items():
            n += 1
            if r['used'] == '1':
                used += 1; s += abs(float(r['shift_kev']))
    print('%s: опор в файлах %d, принято %d, Σ|shift| принятых %.1f кэВ' % (arm, n, used, s))
