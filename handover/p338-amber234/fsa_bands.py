"""Наш FSA на их спектрах: (модель + фон)/данные по полосам мягкой зоны — та же мера, что у их страницы.
Фон нашего разбора = данные − net (снимок FsaStackShot); у дампов FsaComponentDumpProbe «measured» сверяется с net."""
import sys, csv, re
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
F = 'D:/BqMoni_Claude/p338/fsa/'
G = 'D:/BqMoni_Claude/p338/show/spectra/'
BANDS = ((20, 30), (30, 40), (40, 50), (50, 60), (60, 70), (70, 80), (80, 90), (90, 100), (100, 120), (120, 150), (150, 200), (200, 300))


def counts(xml):
    t = open(xml, encoding='utf-8-sig').read()
    i = t.index('<EnergySpectrum>'); j = t.index('</EnergySpectrum>', i)
    return np.array([float(x) for x in re.findall(r'<DataPoint>([-0-9.eE+]+)</DataPoint>', t[i:j])])


def read(path, delim):
    with open(path, encoding='utf-8') as f:
        r = csv.reader(f, delimiter=delim); h = next(r); rows = [list(map(float, x)) for x in r]
    a = np.array(rows); return {k: a[:, i] for i, k in enumerate(h)}


for key, xml in (('berry', 'GS2020_berry_482g.xml'), ('th232', 'GS2020_Th232_oisn10.xml')):
    data = counts(G + xml)
    s = read(F + key + '.shot.curves.csv', ',')
    e = s['keV']; net = s['net']; bg = data[:len(net)] - net
    print(f'== {key}: (модель + фон)/данные; фон нашего разбора = данные − net снимка')
    cols = [('приложение', s['model'])]
    for floor in ('ship', '150'):
        try:
            d = read(F + f'{key}.dump.{floor}.csv', ';')
            dif = np.max(np.abs(d['measured'] - net[:len(d['measured'])])) if len(d['measured']) == len(net) else float('nan')
            cols.append((f'дамп, пол {floor} (|meas−net|max {dif:.1f})', d['model']))
        except FileNotFoundError:
            pass
    print('   полоса кэВ | данные | наш фон | доля фона | ' + ' | '.join(c[0] for c in cols))
    for a, b in BANDS:
        m = (e >= a) & (e < b); D = data[:len(e)][m].sum(); B = bg[m].sum()
        print(f'   {a:4d}-{b:<4d} {D:11.0f} {B:11.0f} {B / D:6.3f} | ' + ' | '.join(f'{(c[1][m].sum() + B) / D:6.3f}' for c in cols))
