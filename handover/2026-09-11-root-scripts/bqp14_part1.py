# -*- coding: utf-8 -*-
# П14, часть 1: три ряда положений вершин — ДАННЫЕ (i), МОДЕЛЬ без привязки (ii), ТАБЛИЦА (iii).
# Источник (i) и (ii): дамп плеча p0 (--anchor-poly=0: опоры собраны, шкала не тронута) —
# <key>_chi.csv (keV по калибровке корпуса, fit = данные минус фон, model), одно окно на оба ряда,
# линейная подложка по крайним трём каналам, центроид и вершина параболой. Второй путь для (i)/(ii):
# _anchors.csv того же плеча (центры тяжести ядра и остатка+ядра в окне ±1 ПШПВ).
import csv, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp14_out'
arm = sys.argv[1] if len(sys.argv) > 1 else 'p0'
# (спектр, подпись линии, табличная энергия, окно кэВ)
LINES = [
    ('G1S16_Ba133_P5',  'K-Cs 31',   30.973, (22, 42)),
    ('G1S16_Ba133_P5',  '81.0',      80.998, (68, 96)),
    ('G1S16_Ba133_P5',  '356.0',    356.013, (336, 376)),
    ('G1S24_Ba133_P5',  'K-Cs 31',   30.973, (22, 42)),
    ('G1S24_Ba133_P5',  '81.0',      80.998, (68, 96)),
    ('G1S24_Ba133_P5',  '356.0',    356.013, (336, 376)),
    ('G1S16_Ba133_P25', 'K-Cs 31',   30.973, (22, 42)),
    ('G1S16_Ba133_P25', '81.0',      80.998, (68, 96)),
    ('G1S16_Ba133_P25', '356.0',    356.013, (336, 376)),
    ('G1S16_Eu152_P5',  'K-Sm 40',   40.117, (30, 52)),
    ('G1S16_Eu152_P5',  '121.8',    121.782, (105, 140)),
    ('G1S16_Eu152_P5',  '244.7',    244.697, (225, 265)),
    ('G1S16_Eu152_P5',  '344.3',    344.279, (322, 368)),
    ('G1S16_Eu152_P5',  '1408',    1408.013, (1350, 1470)),
    ('G1S24_Eu152_P5',  'K-Sm 40',   40.117, (30, 52)),
    ('G1S24_Eu152_P5',  '121.8',    121.782, (105, 140)),
    ('G1S24_Eu152_P5',  '244.7',    244.697, (225, 265)),
    ('G1S24_Eu152_P5',  '344.3',    344.279, (322, 368)),
    ('G1S24_Eu152_P5',  '1408',    1408.013, (1350, 1470)),
    ('G1S16_Th228_P5',  'K-Pb/Bi 77', 76.5,  (62, 96)),
    ('G1S16_Th228_P5',  '238.6',    238.632, (218, 260)),
    ('G1S16_Th228_P5',  '583.2',    583.187, (545, 625)),
    ('G1S16_Th228_P5',  '2614',    2614.511, (2500, 2740)),
    ('G1S24_Th228_P5',  'K-Pb/Bi 77', 76.5,  (62, 96)),
    ('G1S24_Th228_P5',  '238.6',    238.632, (218, 260)),
    ('G1S24_Th228_P5',  '583.2',    583.187, (545, 625)),
    ('G1S24_Th228_P5',  '2614',    2614.511, (2500, 2740)),
    # одиночные линии — контроль «модель ↔ таблица» по всей шкале
    ('G1S16_Am241_P5',  '59.5',      59.541, (48, 72)),
    ('G1S16_Cd109_P5',  '88.0',      88.034, (74, 102)),
    ('G1S16_Co57_P5',   '122.1',    122.061, (105, 140)),
    ('G1S16_Ce139_P5',  '165.9',    165.857, (145, 188)),
    ('G1S16_Cs137_P5',  '661.7',    661.657, (610, 715)),
    ('G1S16_Mn54_P5',   '834.8',    834.848, (775, 895)),
    ('G1S16_Co60_P5',   '1332.5',  1332.492, (1280, 1390)),
]
def load(key):
    p = os.path.join(root, arm + '_dump', key + '_chi.csv')
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))
def stats(kev, vals):
    b0 = sum(vals[:3]) / 3; b1 = sum(vals[-3:]) / 3
    n = len(vals)
    net = [vals[j] - (b0 + (b1 - b0) * j / (n - 1)) for j in range(n)]
    s = sum(net); c = sum(net[j] * kev[j] for j in range(n)) / s if s else float('nan')
    jm = max(range(n), key=lambda j: net[j])
    if 0 < jm < n - 1:
        y0, y1, y2 = net[jm - 1], net[jm], net[jm + 1]
        d = (y0 - 2 * y1 + y2)
        off = 0.5 * (y0 - y2) / d if d else 0.0
        top = kev[jm] + off * (kev[jm + 1] - kev[jm])
    else:
        top = kev[jm]
    return s, c, top
cache = {}
print('плечо %s. Все числа — кэВ по калибровке КОРПУСА; окно общее для данных и модели; ' % arm)
print('вершина — парабола по трём точкам над линейной подложкой, центроид — над той же подложкой.')
print('%-17s %-11s %8s | %8s %8s | %8s %8s | %7s %7s %7s' % ('spectrum', 'линия', 'таблица', 'дан.верш', 'дан.цент', 'мод.верш', 'мод.цент', 'д−т', 'м−т', 'д−м'))
rows = []
for key, name, tab, (lo, hi) in LINES:
    if key not in cache:
        try:
            cache[key] = load(key)
        except FileNotFoundError:
            cache[key] = None
    R = cache[key]
    if R is None:
        print('%-17s %-11s нет дампа' % (key, name)); continue
    sel = [i for i in range(len(R)) if lo <= float(R[i]['keV']) < hi]
    kev = [float(R[i]['keV']) for i in sel]
    dat = [float(R[i]['fit']) for i in sel]
    mod = [float(R[i]['model']) for i in sel]
    sd, cd, td = stats(kev, dat)
    sm, cm, tm = stats(kev, mod)
    rows.append((key, name, tab, td, cd, tm, cm))
    print('%-17s %-11s %8.2f | %8.2f %8.2f | %8.2f %8.2f | %+7.2f %+7.2f %+7.2f' % (key, name, tab, td, cd, tm, cm, td - tab, tm - tab, td - tm))
# второй путь: _anchors.csv плеча — центры в окне ±1 ПШПВ
print()
print('второй путь: %s/<грп>_spline_anchors.csv — центр ядра модели и центр (остаток+ядро) в ОДНОМ окне ±1 ПШПВ, доля ≥ 0.5' % arm)
print('%-17s %-9s %8s | %8s %8s %7s | %7s %7s | %s' % ('spectrum', 'компон.', 'линия', 'модель', 'измер.', 'сдвиг', 'м−т', 'и−т', 'принята/отказ'))
import glob
want = sorted(set(k for k, _, _, _ in LINES))
for p in sorted(glob.glob(os.path.join(root, arm, '*_spline_anchors.csv'))):
    for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
        if r['spectrum'] not in want or float(r['peak_share']) < 0.5:
            continue
        t = float(r['line_kev']); m = float(r['model_kev']); d = float(r['measured_kev'])
        print('%-17s %-9s %8.2f | %8.2f %8.2f %+7.2f | %+7.2f %+7.2f | %s %s' % (r['spectrum'], r['component'], t, m, d, d - m, m - t, d - t, 'принята' if r['used'] == '1' else 'отказ', r['refusal']))
