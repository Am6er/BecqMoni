# -*- coding: utf-8 -*-
"""П67 (AMBER22): сводка прогонов FsaStackShot по геометриям × плотностям.

Читает D:\\BqMoni_Claude\\p67\\out\\<спектр>__<сцена>__<плечо>\\{rates.csv,dump.csv,probe.txt}:
  * A(Th-232), A(Ra-226) — `count_rate` строк ряда (равновесие ВКЛ: у всех членов ряда одно число = распадов родителя в
    секунду), z ряда; χ²/ndf, χ²/ndf (пуассон), невязка модели, усиление/ноль — `meta`;
  * невязка на пиках — model/net − 1 в окне ±1 ПШПВ (7.65 %·√(662·E)) вокруг центроида ДАННЫХ (как П26/П42
    `arms_summary.py`: центроид — взвешенное среднее net в ±1 ПШПВ от табличного положения); доля сплайна
    (`continuum_raw`) в модели 200–2800.

    python collect.py [--arm=eq] [--csv=table.csv]
"""
import csv
import glob
import io
import math
import os
import re
import sys

OUT = r'D:\BqMoni_Claude\p67\out'
PEAKS = [238.632, 338.320, 583.187, 911.204, 2614.511]
FW662 = 0.0765 * 661.657


def fwhm(e):
    return FW662 * math.sqrt(e / 661.657)


def peak_ratio(kev, net, model, e):
    w = fwhm(e)
    ids = [i for i, x in enumerate(kev) if e - w <= x < e + w]
    if not ids:
        return float('nan')
    s = sum(max(net[i], 0.0) for i in ids)
    c = sum(kev[i] * max(net[i], 0.0) for i in ids) / s if s > 0 else e
    ids = [i for i, x in enumerate(kev) if c - w <= x < c + w]
    n = sum(net[i] for i in ids)
    m = sum(model[i] for i in ids)
    return m / n - 1.0 if n > 0 else float('nan')


def read_run(d):
    r = {'key': os.path.basename(d)}
    rp = os.path.join(d, 'rates.csv')
    if not os.path.exists(rp):
        return None
    rows = list(csv.DictReader(io.open(rp, encoding='utf-8-sig')))
    meta = {x['name']: float(x['count_rate']) for x in rows if x['section'] == 'meta'}
    r.update({'chi2ndf': meta.get('chi2ndf'), 'chi2pois': meta.get('chi2ndf_pois'), 'resid': meta.get('model_residual'),
              'gain': meta.get('gain'), 'offset': meta.get('offset_channels'), 'live': meta.get('live_time')})
    for x in rows:
        if x['section'] == 'component' and x['name'] in ('Th-232', 'Ra-226', 'Tl-208', 'Pb-212', 'Bi-214', 'Pb-214'):
            r['A_' + x['name']] = float(x['count_rate']) if x['count_rate'] else float('nan')
            r['z_' + x['name']] = float(x['z']) if x['z'] else float('nan')
            r['share_' + x['name']] = float(x['share_pct']) if x['share_pct'] else float('nan')
            r['chain_' + x['name']] = x['chain_root']
    dp = os.path.join(d, 'dump.csv')
    if os.path.exists(dp):
        rows = list(csv.DictReader(io.open(dp, encoding='utf-8-sig')))
        kev = [float(x['keV']) for x in rows]
        net = [float(x['net']) for x in rows]
        model = [float(x['model']) for x in rows]
        spl = [float(x['continuum_raw']) for x in rows] if 'continuum_raw' in rows[0] else None
        for e in PEAKS:
            r['pk%d' % int(e)] = peak_ratio(kev, net, model, e)
        if spl is not None:
            ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
            r['spline'] = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
    return r


def parse_key(key):
    m = re.match(r'(.+?)__AS80_p67_(contact|face81|edge93)_(r\d\d|p13)(?:_(ring\d\d))?__(\w+)$', key)
    if not m:
        return None
    sp, geom, dens, ring, arm = m.groups()
    rho = {'r28': 2.8, 'r33': 3.3, 'r38': 3.8, 'r42': 4.2, 'p13': 4.345}[dens]
    return sp, geom, dens, rho, ring or 'ring10', arm


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    arm_f = 'eq'
    csvp = None
    for a in sys.argv[1:]:
        if a.startswith('--arm='):
            arm_f = a[6:]
        elif a.startswith('--csv='):
            csvp = a[6:]
    runs = []
    for d in sorted(glob.glob(os.path.join(OUT, '*__*'))):
        pk = parse_key(os.path.basename(d))
        if not pk:
            continue
        r = read_run(d)
        if r is None:
            continue
        r['spectrum'], r['geom'], r['dens'], r['rho'], r['ring'], r['arm'] = pk
        runs.append(r)
    runs = [r for r in runs if r['arm'] == arm_f or arm_f == 'all']
    runs.sort(key=lambda r: (r['spectrum'], r['rho'], r['ring'], r['arm']))
    hdr = '%-16s %-7s %5s %-6s %-4s | %8s %6s %8s %6s | %7s %6s %6s | %s | %6s | %7s %7s' % (
        'спектр', 'геом', 'ρ', 'кольцо', 'пл', 'A(Th)Бк', 'z', 'A(Ra)Бк', 'z', 'χ²/ndf', 'χ²п', 'невяз', ' '.join('%7s' % ('пик%d' % int(e)) for e in PEAKS), 'сплайн', 'усил', 'ноль')
    print(hdr)
    out_rows = []
    for r in runs:
        line = '%-16s %-7s %5.2f %-6s %-4s | %8.1f %6.1f %8.1f %6.1f | %7.3f %6.2f %5.1f%% | %s | %5.1f%% | %7.4f %7.2f' % (
            r['spectrum'][:16], r['geom'], r['rho'], r['ring'], r['arm'],
            r.get('A_Th-232', float('nan')), r.get('z_Th-232', float('nan')),
            r.get('A_Ra-226', float('nan')), r.get('z_Ra-226', float('nan')),
            r['chi2ndf'] or float('nan'), r['chi2pois'] or float('nan'), 100 * (r['resid'] or float('nan')),
            ' '.join('%+6.1f%%' % (100 * r.get('pk%d' % int(e), float('nan'))) for e in PEAKS),
            r.get('spline', float('nan')), r['gain'] or float('nan'), r['offset'] or float('nan'))
        print(line)
        out_rows.append(r)
    if csvp:
        keys = ['spectrum', 'geom', 'rho', 'ring', 'arm', 'A_Th-232', 'z_Th-232', 'A_Ra-226', 'z_Ra-226', 'chi2ndf', 'chi2pois', 'resid',
                'pk238', 'pk338', 'pk583', 'pk911', 'pk2614', 'spline', 'gain', 'offset', 'live', 'key']
        with io.open(csvp, 'w', encoding='utf-8', newline='') as fh:
            w = csv.DictWriter(fh, fieldnames=keys, extrasaction='ignore')
            w.writeheader()
            for r in out_rows:
                w.writerow({k: (('%.6g' % r[k]) if isinstance(r.get(k), float) else r.get(k, '')) for k in keys})


if __name__ == '__main__':
    main()
