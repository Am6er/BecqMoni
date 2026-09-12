# Таблицы журнала П36 из selfabs.csv пробы MarinelliSelfAbsProbeE10.
#   python handover/p36-e10/tables.py handover/p36-e10/full/selfabs.csv
import csv, sys, math

path = sys.argv[1]
rows = []
with open(path, newline='', encoding='utf-8') as f:
    for r in csv.DictReader(f):
        rows.append(r)

scenes = []
for r in rows:
    if r['scene'] not in scenes:
        scenes.append(r['scene'])

def fmt(x, d=4):
    return ('%.' + str(d) + 'f') % x

for sc in scenes:
    sub = [r for r in rows if r['scene'] == sc]
    energies = sorted(set(float(r['E_keV']) for r in sub))
    fills = []
    for r in sub:
        key = (r['material'], float(r['rho']))
        if key not in fills:
            fills.append(key)
    fills.sort(key=lambda k: (k[0] != 'H2O', k[1]))
    print('\n### сцена %s' % sc)
    # Таблица 1: Cs_МК ± шум
    print('\nCs_МК = ε(воздух)/ε(ρ) (± шум МК в %, независимая оценка по квадратуре):\n')
    head = '| E, кэВ | ' + ' | '.join(('%s %.2f' % k) for k in fills) + ' |'
    print(head); print('|' + '---|' * (len(fills) + 1))
    for e in energies:
        cells = []
        for k in fills:
            r = next(x for x in sub if float(x['E_keV']) == e and x['material'] == k[0] and float(x['rho']) == k[1])
            cells.append('%s ±%s' % (fmt(float(r['Cs_mc'])), fmt(float(r['Cs_mc_err_pct']), 2)))
        print('| %.1f | %s |' % (e, ' | '.join(cells)))
    # Таблица 2: МК − ДЕБ(полное μ), %
    print('\nМК − ДЕБ (интеграл Дебертина, полное μ), %:\n')
    print(head); print('|' + '---|' * (len(fills) + 1))
    worst = 0.0
    for e in energies:
        cells = []
        for k in fills:
            r = next(x for x in sub if float(x['E_keV']) == e and x['material'] == k[0] and float(x['rho']) == k[1])
            d = float(r['d_mc_deb_total_pct']); worst = max(worst, abs(d))
            cells.append('%+.2f' % d)
        print('| %.1f | %s |' % (e, ' | '.join(cells)))
    print('\nнаибольшее |МК − ДЕБ| = %.2f %%' % worst)
    # Таблица 3: формула, если есть
    if any(r['Cs_formula'] for r in sub):
        print('\nМК − ФОРМ (формула (11) Jodłowski), % — только SiO₂:\n')
        ks = [k for k in fills if k[0] == 'SiO2']
        print('| E, кэВ | ' + ' | '.join(('ρ %.2f' % k[1]) for k in ks) + ' |'); print('|' + '---|' * (len(ks) + 1))
        worst = 0.0
        for e in energies:
            cells = []
            for k in ks:
                r = next(x for x in sub if float(x['E_keV']) == e and x['material'] == k[0] and float(x['rho']) == k[1])
                d = float(r['d_mc_formula_pct']); worst = max(worst, abs(d))
                cells.append('%+.2f' % d)
            print('| %.1f | %s |' % (e, ' | '.join(cells)))
        print('\nнаибольшее |МК − ФОРМ| = %.2f %%' % worst)
        print('\nCs_ФОРМ (формула (11)) для справки:\n')
        print('| E, кэВ | ' + ' | '.join(('ρ %.2f' % k[1]) for k in ks) + ' |'); print('|' + '---|' * (len(ks) + 1))
        for e in energies:
            cells = []
            for k in ks:
                r = next(x for x in sub if float(x['E_keV']) == e and x['material'] == k[0] and float(x['rho']) == k[1])
                cells.append(fmt(float(r['Cs_formula'])))
            print('| %.1f | %s |' % (e, ' | '.join(cells)))
