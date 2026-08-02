# -*- coding: utf-8 -*-
"""Что в спектре ЕСТЬ: разрешение и присутствие диагностических линий.

Второй шаг отбора в корпус, после library_inventory.py. По имени файла судить
нельзя — «Чароит в домике.xml» может оказаться пустым, а «Фон дома.xml» нести
весь ториевый ряд, — поэтому содержимое меряется.

Две вещи, на которых первая версия этого скрипта сломалась и которые здесь
сделаны иначе:

1. **Фон под линией берётся ЛОКАЛЬНО, из крыльев, а не как огибающая SNIP.**
   При миллионах отсчётов остаток SNIP даёт z > 8 практически в любом окне, и
   первая версия пометила каждый спектр как содержащий всё сразу — торий,
   радий, уран, цезий и европий одновременно. Здесь под линией проводится
   прямая по двум крыльям (±1.6…3.2 FWHM), и кроме значимости считается
   отношение чистой площади к подложке: линия засчитывается при z >= 6 И
   доле >= 4 %.
2. **Разрешение меряется, а не задаётся.** Корпус охватывает от 0.2 % до 15 %
   на 662 кэВ; фиксированное окно поиска либо режет германий, либо на
   1024-канальном приборе ловит соседей. Ширина берётся по полувысоте у самой
   значимой из опорных линий, дальше масштабируется как sqrt(E).

Запуск:  python library_screen.py [--in=inventory.json] [--out=screen.json] [--labels]
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))

# Диагностические линии: по ним определяется, что в образце.
LINES = [
    ('Pb210', 46.5), ('Am241', 59.54), ('U238a', 63.3), ('Th234', 92.5),
    ('Lu176a', 88.34), ('Lu176b', 201.83), ('Lu176c', 306.78),
    ('Cs137', 661.66), ('K40', 1460.82),
    ('Co60a', 1173.2), ('Co60b', 1332.5), ('Na22', 1274.5),
    ('Ti44a', 78.3), ('Sc44', 1157.0), ('I131', 364.5), ('Sm153', 103.2),
    ('Eu152a', 121.78), ('Eu152b', 344.3), ('Eu152c', 1408.0),
    ('Th_238', 238.63), ('Th_338', 338.32), ('Th_583', 583.19),
    ('Th_911', 911.20), ('Th_2614', 2614.51), ('Th_1588', 1588.20),
    ('Ra_295', 295.22), ('Ra_352', 351.93), ('Ra_609', 609.32),
    ('Ra_1120', 1120.29), ('Ra_1764', 1764.49), ('Ra_2204', 2204.10),
    ('U235_143', 143.76), ('U235_205', 205.31), ('Pa234m', 1001.03),
]

# Сильные и обычно одиночные линии — по ним меряется разрешение.
PROBES = [2614.51, 661.66, 1460.82, 1332.5, 583.19, 609.32, 351.93, 88.34, 59.54]

# Группировка линий в «что за нуклид/цепочка» для итоговой метки.
GROUPS = {
    'Th': ['Th_238', 'Th_338', 'Th_583', 'Th_911', 'Th_2614'],
    'Ra': ['Ra_295', 'Ra_352', 'Ra_609', 'Ra_1120', 'Ra_1764'],
    'U5': ['U235_143', 'U235_205'], 'U8': ['U238a', 'Th234', 'Pa234m'],
    'Cs': ['Cs137'], 'K': ['K40'], 'Am': ['Am241'],
    'Lu': ['Lu176a', 'Lu176b', 'Lu176c'], 'Co': ['Co60a', 'Co60b'],
    'Ti': ['Ti44a', 'Sc44'], 'Na': ['Na22'],
    'Eu': ['Eu152a', 'Eu152b', 'Eu152c'], 'I': ['I131'], 'Sm': ['Sm153'],
}

Z_MIN = 6.0          # значимость чистой площади над локальной подложкой
PTB_MIN = 0.04       # и её доля от самой подложки — без этого при 10^8
                     # отсчётов значимой становится любая рябь континуума


def load(path, idx):
    rd = ET.parse(path).getroot().findall('ResultDataList/ResultData')[idx]
    es = rd.find('EnergySpectrum')
    counts = np.array([int(d.text) for d in es.findall('Spectrum/DataPoint')], dtype=float)
    ecal = np.array([float(x.text) for x in
                     es.findall('EnergyCalibration/Coefficients/Coefficient')])
    return counts, ecal


def net_over_wings(e, c, e0, fwhm):
    """(z, чистая площадь / подложка, смещение центроида) для линии e0."""
    inner = (e >= e0 - fwhm) & (e <= e0 + fwhm)
    left = (e >= e0 - 3.2 * fwhm) & (e <= e0 - 1.6 * fwhm)
    right = (e >= e0 + 1.6 * fwhm) & (e <= e0 + 3.2 * fwhm)
    if inner.sum() < 2 or left.sum() < 1 or right.sum() < 1:
        return None
    xs = np.concatenate([e[left], e[right]])
    ys = np.concatenate([c[left], c[right]])
    try:
        k, b = np.polyfit(xs, ys, 1)
    except Exception:
        return None
    base = k * e[inner] + b
    net = float((c[inner] - base).sum())
    var = float(c[inner].sum()) + float(ys.sum()) * (inner.sum() / max(len(ys), 1)) ** 2
    if var <= 0:
        return None
    weights = np.maximum(c[inner] - base, 0.0)
    centroid = float((e[inner] * weights).sum() / weights.sum()) if weights.sum() > 0 else e0
    return (round(net / np.sqrt(var), 1),
            round(net / max(float(base.sum()), 1.0), 3),
            round(centroid - e0, 2))


def measure_fwhm(e, c, e0):
    """Полуширина по полувысоте над прямой, проведённой по краям окна."""
    half = 0.14 * e0
    m = (e > e0 - half) & (e < e0 + half)
    if m.sum() < 5:
        return None
    ee, cc = e[m], c[m]
    base = np.linspace(cc[:2].mean(), cc[-2:].mean(), len(cc))
    smooth = np.convolve(cc - base, np.array([0.25, 0.5, 0.25]), mode='same')
    i = int(np.argmax(smooth))
    if smooth[i] <= 0 or i == 0 or i == len(smooth) - 1:
        return None
    level = smooth[i] / 2.0
    a, b = i, i
    while a > 0 and smooth[a] > level:
        a -= 1
    while b < len(smooth) - 1 and smooth[b] > level:
        b += 1
    if smooth[a] > level or smooth[b] > level:
        return None
    lo = np.interp(level, [smooth[a], smooth[a + 1]], [ee[a], ee[a + 1]])
    hi = np.interp(level, [smooth[b], smooth[b - 1]], [ee[b], ee[b - 1]])
    width = hi - lo
    if width <= 0 or width > 0.5 * e0:
        return None
    return float(width), float(ee[i])


def screen(row):
    counts, ecal = load(row['path'], row['idx'])
    n = len(counts)
    if n < 256 or counts.sum() < 2000 or len(ecal) < 2:
        return dict(rel=row['rel'], idx=row['idx'], err='слишком мало')
    ch = np.arange(n, dtype=float)
    e = sum(c * ch ** i for i, c in enumerate(ecal))
    if not np.all(np.diff(e) > 0):
        return dict(rel=row['rel'], idx=row['idx'], err='немонотонная калибровка')

    best = None
    for e0 in PROBES:
        if e0 < e[4] or e0 > e[-5]:
            continue
        seen = net_over_wings(e, counts, e0, 0.09 * np.sqrt(662.0 * e0))
        if seen is None or seen[0] < 15 or seen[1] < 0.03:
            continue
        measured = measure_fwhm(e, counts, e0)
        if measured is None:
            continue
        if best is None or seen[0] > best[0]:
            best = (seen[0], e0, measured[0])
    res_a = best[2] / np.sqrt(best[1]) if best else 0.065 * np.sqrt(662.0)
    res_a = float(np.clip(res_a, 0.02, 4.0))
    bin_kev = float(np.median(np.diff(e)))

    z = {}
    for name, e0 in LINES:
        if e0 < e[4] or e0 > e[-5]:
            z[name] = None
            continue
        z[name] = net_over_wings(e, counts, e0, max(res_a * np.sqrt(e0), 2.0 * bin_kev))

    return dict(
        rel=row['rel'], path=row['path'], idx=row['idx'], nrd=row['nrd'],
        device=row['device'], guid=row['guid'], sample=row['sample'],
        channels=n, live=row['fg']['live'], counts=int(counts.sum()),
        has_bg=bool(row['bg']), emin=round(float(e[0]), 1), emax=round(float(e[-1]), 1),
        binw=round(bin_kev, 3),
        fwhm662=round(float(res_a * np.sqrt(662.0) / 662.0 * 100), 2),
        res_src=(round(best[1], 1), round(best[2], 2)) if best else None,
        z=z)


def label(row):
    """«Th5/5 Ra2/5 K1/1» — что нашлось из каждой группы."""
    out = []
    for group, names in GROUPS.items():
        available = [nm for nm in names if row['z'].get(nm) is not None]
        if not available:
            continue
        hits = sum(1 for nm in available
                   if row['z'][nm][0] >= Z_MIN and row['z'][nm][1] >= PTB_MIN)
        if hits:
            out.append('%s%d/%d' % (group, hits, len(available)))
    return ' '.join(out)


def main():
    src = os.path.join(HERE, 'inventory.json')
    out = os.path.join(HERE, 'screen.json')
    for arg in sys.argv[1:]:
        if arg.startswith('--in='):
            src = arg.split('=', 1)[1]
        if arg.startswith('--out='):
            out = arg.split('=', 1)[1]
    rows = json.load(open(src, encoding='utf-8'))['rows']
    result = []
    for row in rows:
        try:
            result.append(screen(row))
        except Exception as ex:
            result.append(dict(rel=row['rel'], idx=row['idx'], err=str(ex)))
    with open(out, 'w', encoding='utf-8') as fh:
        json.dump(result, fh, ensure_ascii=False, indent=1)
    print('обработано %d, ошибок %d -> %s' % (
        len(result), sum(1 for r in result if 'err' in r), out))
    if '--labels' in sys.argv:
        for r in sorted((x for x in result if 'err' not in x), key=lambda x: x['rel']):
            print('%-56s %5dch %5.2f%% %7s %s%s' % (
                r['rel'][:56] + ('#%d' % r['idx'] if r['nrd'] > 1 else ''),
                r['channels'], r['fwhm662'],
                '%.1fM' % (r['counts'] / 1e6) if r['counts'] >= 1e6
                else '%.0fk' % (r['counts'] / 1e3),
                'фон ' if r['has_bg'] else '    ', label(r)))


if __name__ == '__main__':
    main()
