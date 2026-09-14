# -*- coding: utf-8 -*-
# П16, часть 1: промах линий над хордами через ГАММА-линии — данные корпуса против трёх кривых r(E).
# Данные: плечо p0 П14 (--anchor-poly=0: опоры собраны, шкала не тронута) — <грп>_spline_anchors.csv,
# «измерение − модель» в одном окне ±1 ПШПВ (центроид остатка+ядра против центра ядра): окно общее,
# смещение окна вычитается. Второй ряд — «измерение − таблица» (как у П14 §2.2).
# python bqp16_part1.py [каталог p0]
import csv, glob, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bqp16_curves import CURVES, chord_miss

root = sys.argv[1] if len(sys.argv) > 1 else r'C:\Users\moroz\bqp14_out\p0'

# (спектр, компонент, (E1, E2) хорды, контрольные линии)
CHORDS = [
    ('G1S16_Eu152_P5',  'Eu-152', (121.782, 1408.013), [244.697, 344.279, 778.904, 964.057, 1112.076]),
    ('G1S24_Eu152_P5',  'Eu-152', (121.782, 1408.013), [244.697, 344.279, 778.904, 964.057, 1112.076]),
    ('G1S16_Eu152_P25', 'Eu-152', (121.782, 1408.013), [244.697, 344.279, 778.904, 964.057, 1112.076]),
    ('G1S16_Ba133_P5',  'Ba-133', (80.998, 356.013),   [302.851]),
    ('G1S24_Ba133_P5',  'Ba-133', (80.998, 356.013),   [302.851]),
    ('G1S16_Ba133_P25', 'Ba-133', (80.998, 356.013),   [302.851]),
    ('G1S16_Th228_P5',  'Th-228', (238.632, 2614.511), [510.77, 583.187, 727.33, 860.557]),
    ('G1S24_Th228_P5',  'Th-228', (238.632, 2614.511), [510.77, 583.187, 727.33, 860.557]),
    ('G1S24_Th228_P25', 'Th-228', (238.632, 2614.511), [510.77, 583.187, 727.33, 860.557]),
    ('G1S24_Bi207_P5',  'Bi-207', (569.698, 1063.656), [1770.228]),
    ('G1S16_Bi207_P5',  'Bi-207', (569.698, 1063.656), [1770.228]),
    ('G1S16_Co60_P5',   'Co-60',  (1173.228, 1332.492), []),
    # рентген — ОТДЕЛЬНОЙ строкой (B29): хорды П14 через K-серию
    ('G1S16_Ba133_P5',  'Ba-133', (30.973, 356.013),   [80.998, 302.851]),
    ('G1S24_Ba133_P5',  'Ba-133', (30.973, 356.013),   [80.998, 302.851]),
    ('G1S16_Ba133_P25', 'Ba-133', (30.973, 356.013),   [80.998, 302.851]),
    ('G1S16_Eu152_P5',  'Eu-152', (40.117, 1408.013),  [121.782, 244.697, 344.279, 964.057]),
    ('G1S24_Eu152_P5',  'Eu-152', (40.117, 1408.013),  [121.782, 244.697, 344.279, 964.057]),
    ('G1S16_Th228_P5',  'Th-228', (77.107, 2614.511),  [238.632, 583.187, 860.557]),
    ('G1S24_Th228_P5',  'Th-228', (77.107, 2614.511),  [238.632, 583.187, 860.557]),
]

anch = {}
for p in glob.glob(os.path.join(root, '*_spline_anchors.csv')):
    for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
        try:
            m = float(r['model_kev']); d = float(r['measured_kev'])
        except ValueError:
            continue
        if d != d or m != m:
            continue
        anch[(r['spectrum'], r['component'], round(float(r['line_kev']), 1))] = (m, d, float(r['peak_share']), float(r['z']), r['used'], r['refusal'])

def get(spec, comp, e):
    return anch.get((spec, comp, round(e, 1)))

print('каталог данных: %s' % root)
print('промах линии над хордой через две опоры, кэВ: данные (изм−мод, изм−таб) против трёх кривых r(E)')
print('%-17s %-12s %7s %6s %6s | %8s %8s | %8s %8s %8s | %s' % ('спектр', 'хорда', 'линия', 'доля', 'z', 'д(и−м)', 'д(и−т)', 'модель', 'litA', 'litB', 'опора'))
rows = []
for spec, comp, (e1, e2), ctrl in CHORDS:
    a1 = get(spec, comp, e1); a2 = get(spec, comp, e2)
    if a1 is None or a2 is None:
        print('%-17s %-12s нет опор хорды (%s / %s)' % (spec, '%g-%g' % (e1, e2), a1 is not None, a2 is not None))
        continue
    dm1 = a1[1] - a1[0]; dm2 = a2[1] - a2[0]      # изм − мод у опор
    dt1 = a1[1] - e1;    dt2 = a2[1] - e2         # изм − таб у опор
    for e in ctrl:
        a = get(spec, comp, e)
        if a is None:
            print('%-17s %-12s %7.1f нет кандидата' % (spec, '%g-%g' % (e1, e2), e))
            continue
        t = (e - e1) / (e2 - e1)
        dm = (a[1] - a[0]) - (dm1 + (dm2 - dm1) * t)
        dt = (a[1] - e) - (dt1 + (dt2 - dt1) * t)
        pred = [chord_miss(CURVES[k], e, e1, e2) for k in ('model', 'litA', 'litB')]
        rows.append((spec, e1, e2, e, dm, dt, pred, a[2], a[3]))
        print('%-17s %-12s %7.1f %6.2f %6.0f | %+8.2f %+8.2f | %+8.2f %+8.2f %+8.2f | %s%s' % (
            spec, '%g-%g' % (e1, e2), e, a[2], a[3], dm, dt, pred[0], pred[1], pred[2], 'принята' if a[4] == '1' else 'отказ', (' ' + a[5]) if a[5] else ''))

# сводка: по линии (усреднение по спектрам), только гамма-хорды и доля ≥ 0.4
print()
print('сводка по линиям (гамма-хорды, доля ядра ≥ 0.4): среднее д(и−м) по спектрам, предсказания, отношение данные/кривая')
from collections import defaultdict
agg = defaultdict(list)
for spec, e1, e2, e, dm, dt, pred, share, z in rows:
    if e1 < 60 or share < 0.4:
        continue
    agg[(e1, e2, e)].append((dm, dt, pred))
print('%-14s %7s %3s | %8s %8s | %8s %8s %8s | %6s %6s %6s' % ('хорда', 'линия', 'n', 'д(и−м)', 'д(и−т)', 'модель', 'litA', 'litB', 'д/мод', 'д/litA', 'д/litB'))
for (e1, e2, e), lst in sorted(agg.items()):
    n = len(lst)
    dm = sum(x[0] for x in lst) / n; dt = sum(x[1] for x in lst) / n
    p = lst[0][2]
    ratio = ['%6.2f' % (dm / v) if abs(v) > 0.05 else '   —  ' for v in p]
    print('%-14s %7.1f %3d | %+8.2f %+8.2f | %+8.2f %+8.2f %+8.2f | %s' % ('%g-%g' % (e1, e2), e, n, dm, dt, p[0], p[1], p[2], ' '.join(ratio)))
