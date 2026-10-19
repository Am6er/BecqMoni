"""Наш FSA (снимок приложения FsaStackShot) на их спектрах, три варианта входа:
А — шкалы их страницы (своя у пробы и у фона); Б — одна шкала прибора у пробы и фона;
В — Б + фон × их r(E). Мера — (модель + наш фон)/данные, наш фон = данные − net;
net снимка обрезан нулём снизу, поэтому рядом — число каналов net = 0 в полосе."""
import csv, re, sys
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
L = 'D:/BqMoni_Claude/p338/'
BANDS = ((20, 30), (30, 40), (40, 50), (50, 60), (60, 70), (70, 80), (80, 90), (90, 100), (100, 120), (120, 150), (150, 200), (200, 300))
VAR = (('А', 'shot', 'spectra'), ('Б', 'b', 'spectra_b'), ('В', 'c', 'spectra_c'))


def raw(xml):
    t = open(xml, encoding='utf-8-sig').read(); i = t.index('<EnergySpectrum>'); j = t.index('</EnergySpectrum>', i)
    return np.array([float(x) for x in re.findall(r'<DataPoint>([-0-9.eE+]+)</DataPoint>', t[i:j])])


def curves(p):
    with open(p, encoding='utf-8') as f:
        r = csv.reader(f); h = next(r); a = np.array([list(map(float, x)) for x in r])
    return {k: a[:, i] for i, k in enumerate(h)}


def rates(p):
    out = {}
    with open(p, encoding='utf-8') as f:
        for row in csv.DictReader(f):
            if row['section'] == 'component':
                out[row['name']] = float(row['count_rate'])
            elif row['section'] == 'meta' and row['name'] in ('chi2ndf', 'chi2ndf_pois'):
                out[row['name']] = float(row['count_rate'])
    return out


for key, xml in (('berry', 'GS2020_berry_482g.xml'), ('th232', 'GS2020_Th232_oisn10.xml')):
    print(f'== {key}: (модель + наш фон)/данные [каналов net = 0]')
    res = {}
    for v, tag, sdir in VAR:
        d = curves(f'{L}fsa/{key}.{tag}.curves.csv'); e = d['keV']; net = d['net']; data = raw(f'{L}show/{sdir}/{xml}')[:len(e)]
        res[v] = (e, net, d['model'], data)
    print('   полоса кэВ | ' + ' | '.join(f'вариант {v}' for v, _, _ in VAR))
    for a, b in BANDS:
        cells = []
        for v, _, _ in VAR:
            e, net, model, data = res[v]; m = (e >= a) & (e < b)
            cells.append(f'{(model[m].sum() + data[m].sum() - net[m].sum()) / data[m].sum():6.3f} [{int((net[m] <= 0).sum()):2d}/{m.sum():2d}]')
        print(f'   {a:4d}-{b:<4d}  | ' + ' | '.join(cells))
    for v, tag, _ in VAR:
        r = rates(f'{L}fsa/{key}.{tag}.rates.csv')
        print(f'   вариант {v}: ' + ', '.join(f'{k} {val:.4g}' for k, val in r.items()))
