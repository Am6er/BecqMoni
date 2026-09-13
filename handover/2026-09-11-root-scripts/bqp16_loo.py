# -*- coding: utf-8 -*-
# П16: (1) Σχ² по группам «опор ≥ 3» / «опор ≤ 2»; (2) приёмка выбросом узла — остатки линий по плечам
# (измерение − модель на ИТОГОВОЙ шкале плеча, окно ±1 ПШПВ, `_spline_anchors.csv`), принята/отказ;
# (3) ловушка S95 Cd-109 и G1S16_Th228_P5.
import csv, glob, io, os, sys, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp16_out'
arms = sys.argv[1:] or ['lin', 'model-b1', 'lit-b1', 'model-free', 'lit-free']
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_runs.csv')):
        for row in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            r[row['spectrum']] = row
    return r
def anchors(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_anchors.csv')):
        for row in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            r[(row['spectrum'], row['component'], round(float(row['line_kev']), 1))] = row
    return r
def comps(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_components.csv')):
        for row in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            r[(row['spectrum'], row['component'])] = row
    return r
R = {a: runs(a) for a in arms}
A = {a: anchors(a) for a in arms}
keys = sorted(k for k in R[arms[0]] if R[arms[0]][k]['part'] == 'known' and not R[arms[0]][k]['error'])
multi = [k for k in keys if int(R[arms[0]][k]['anchors_used']) >= 3]
few = [k for k in keys if int(R[arms[0]][k]['anchors_used']) <= 2]
print('== Σχ²/ndf по группам (опоры по плечу lin): ≥ 3 опор — %d спектров, ≤ 2 — %d' % (len(multi), len(few)))
print('%-11s %10s %10s %10s' % ('плечо', 'все 42', '≥3 опор', '≤2 опор'))
for a in arms:
    s_all = sum(float(R[a][k]['chi2ndf']) for k in keys)
    s_m = sum(float(R[a][k]['chi2ndf']) for k in multi)
    s_f = sum(float(R[a][k]['chi2ndf']) for k in few)
    print('%-11s %10.1f %10.1f %10.1f' % (a, s_all, s_m, s_f))
print('≥3 опор:', ', '.join(multi))
print()
# (2) выброс узла
LINES = [
    ('G1S16_Eu152_P5', 'Eu-152', 121.8), ('G1S24_Eu152_P5', 'Eu-152', 121.8), ('G1S16_Eu152_P25', 'Eu-152', 121.8),
    ('G1S16_Eu152_P5', 'Eu-152', 244.7), ('G1S24_Eu152_P5', 'Eu-152', 244.7), ('G1S16_Eu152_P25', 'Eu-152', 244.7),
    ('G1S16_Eu152_P5', 'Eu-152', 344.3), ('G1S24_Eu152_P5', 'Eu-152', 344.3), ('G1S16_Eu152_P25', 'Eu-152', 344.3),
    ('G1S16_Eu152_P5', 'Eu-152', 964.1), ('G1S24_Eu152_P5', 'Eu-152', 964.1),
    ('G1S16_Eu152_P5', 'Eu-152', 1408.0), ('G1S24_Eu152_P5', 'Eu-152', 1408.0),
    ('G1S16_Eu152_P5', 'Eu-152', 40.1), ('G1S24_Eu152_P5', 'Eu-152', 40.1),
    ('G1S16_Ba133_P5', 'Ba-133', 81.0), ('G1S24_Ba133_P5', 'Ba-133', 81.0), ('G1S16_Ba133_P25', 'Ba-133', 81.0),
    ('G1S16_Ba133_P5', 'Ba-133', 31.0), ('G1S16_Ba133_P5', 'Ba-133', 302.9), ('G1S16_Ba133_P5', 'Ba-133', 356.0),
    ('G1S16_Th228_P5', 'Th-228', 77.1), ('G1S16_Th228_P5', 'Th-228', 238.6), ('G1S16_Th228_P5', 'Th-228', 583.2),
    ('G1S16_Th228_P5', 'Th-228', 860.6), ('G1S16_Th228_P5', 'Th-228', 2614.5),
    ('G1S24_Th228_P5', 'Th-228', 238.6), ('G1S24_Th228_P5', 'Th-228', 583.2), ('G1S24_Th228_P5', 'Th-228', 860.6), ('G1S24_Th228_P5', 'Th-228', 2614.5),
    ('G1S24_Th228_P25', 'Th-228', 238.6), ('G1S24_Th228_P25', 'Th-228', 583.2), ('G1S24_Th228_P25', 'Th-228', 860.6), ('G1S24_Th228_P25', 'Th-228', 2614.5),
    ('G1S24_Bi207_P5', 'Bi-207', 75.0), ('G1S24_Bi207_P5', 'Bi-207', 569.7), ('G1S24_Bi207_P5', 'Bi-207', 1063.7), ('G1S24_Bi207_P5', 'Bi-207', 1770.2),
    ('G1S16_Cd109_P5', 'Cd-109', 22.2), ('G1S16_Cd109_P5', 'Cd-109', 88.0),
]
print('== выброс узла: остаток «измерение − модель» линии на итоговой шкале плеча, кэВ (о — опора принята, x — отказ)')
print('%-17s %7s |' % ('спектр', 'линия') + ''.join('%14s' % a for a in arms))
for spec, comp, e in LINES:
    cells = []
    for a in arms:
        r = A[a].get((spec, comp, e))
        if r is None:
            cells.append('%14s' % '—')
            continue
        try:
            sh = float(r['shift_kev'])
        except ValueError:
            sh = float('nan')
        cells.append('%+9.2f %s%-3s' % (sh, 'о' if r['used'] == '1' else 'x', (r['refusal'] or '')[:3]))
    print('%-17s %7.1f |' % (spec, e) + ''.join(cells))
print()
# сводка по линиям, НЕ участвовавшим в привязке в lin (остатки по модулю, среднее по спектрам)
print('== |остаток| линий-контролей (121.8, 81, 244.7, 344.3, 583.2, 860.6), среднее по спектрам, кэВ')
for e in (121.8, 81.0, 244.7, 344.3, 583.2, 860.6):
    row = '%7.1f' % e
    for a in arms:
        vals = []
        for spec, comp, ee in LINES:
            if ee != e:
                continue
            r = A[a].get((spec, comp, e))
            if r is None:
                continue
            try:
                vals.append(abs(float(r['shift_kev'])))
            except ValueError:
                pass
        row += '%14s' % ('%.2f (n=%d)' % (sum(vals) / len(vals), len(vals)) if vals else '—')
    print(row)
print()
# (3) S95 и Th228_P5
C = {a: comps(a) for a in arms}
print('== ловушка S95 G1S16_Cd109_P5 и худший П10 G1S16_Th228_P5')
for a in arms:
    r = R[a]['G1S16_Cd109_P5']
    cd = C[a].get(('G1S16_Cd109_P5', 'Cd-109'))
    share = cd['share_pct'] if cd else '—'
    print('%-11s Cd109: χ²/ndf %s, доля Cd-109 %s %%, опор %s, подавлен %s | Th228_P5: χ²/ndf %s, опор %s' % (
        a, r['chi2ndf'], share, r['anchors_used'], r.get('suppressed', '?'), R[a]['G1S16_Th228_P5']['chi2ndf'], R[a]['G1S16_Th228_P5']['anchors_used']))
