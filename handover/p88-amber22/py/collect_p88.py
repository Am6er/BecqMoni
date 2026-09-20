# -*- coding: utf-8 -*-
r"""П88 (AMBER22): сводка прогонов FsaStackShot стенда П88 — по образцу collect.py П67, плюс столбцы про фон K-40.

Читает D:\BqMoni_Claude\p88\out\<спектр>__<сцена>__<плечо>\{rates.csv,dump.csv}:
  * A(Th-232), A(Ra-226) — `count_rate` строк ряда (равновесие ВКЛ), z; без связки — Ac-228 / Pb-212 / Tl-208 порознь;
  * χ²/ndf, χ²п, невязка модели, усиление/ноль привязки — `meta`;
  * невязка на пиках 238/338/583/911/2614 — model/net − 1 в ±1 ПШПВ вокруг центроида данных (как П67);
  * доля сплайна в модели 200–2800;
  * K-40 (только фон): сумма (fit − model) в ±30 кэВ у 1394 (куда шкала пробы P ставит K-40 самой пробы) и у 1461
    (куда перекалиброванный фон ставит свой K-40): дефект/поправка вычитания фона видна знаком и величиной.

    python collect_p88.py [--arm=asis|eq|noeqasis|noeq|all] [--csv=table.csv] [--only=<подстрока спектра>]
"""
import csv
import glob
import io
import math
import os
import re
import sys

OUT = r'D:\BqMoni_Claude\p88\out'
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


def band_resid(kev, fit, model, e, half=30.0):
    ids = [i for i, x in enumerate(kev) if e - half <= x < e + half]
    return sum(fit[i] - model[i] for i in ids) if ids else float('nan')


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
        if x['section'] == 'component' and x['name'] in ('Th-232', 'Ra-226', 'Tl-208', 'Pb-212', 'Ac-228', 'Bi-214', 'Pb-214', 'K-40'):
            r['A_' + x['name']] = float(x['count_rate']) if x['count_rate'] else float('nan')
            r['z_' + x['name']] = float(x['z']) if x['z'] else float('nan')
    dp = os.path.join(d, 'dump.csv')
    if os.path.exists(dp):
        rows = list(csv.DictReader(io.open(dp, encoding='utf-8-sig')))
        kev = [float(x['keV']) for x in rows]
        net = [float(x['net']) for x in rows]
        model = [float(x['model']) for x in rows]
        fit = [float(x['fit']) for x in rows] if 'fit' in rows[0] else net
        spl = [float(x['continuum_raw']) for x in rows] if 'continuum_raw' in rows[0] else None
        for e in PEAKS:
            r['pk%d' % int(e)] = peak_ratio(kev, net, model, e)
        if spl is not None:
            ids = [i for i, x in enumerate(kev) if 200 <= x < 2800]
            r['spline'] = 100.0 * sum(spl[i] for i in ids) / max(1e-9, sum(model[i] for i in ids))
        r['res1394'] = band_resid(kev, fit, model, 1394.0)
        r['res1461'] = band_resid(kev, fit, model, 1461.0)
        ids = [i for i, x in enumerate(kev) if 1000 <= x < 2000]
        r['band1000'] = 100.0 * sum(abs(fit[i] - model[i]) for i in ids) / max(1e-9, sum(abs(fit[i]) for i in ids))
    return r


def parse_key(key):
    m = re.match(r'(.+?)__AS80_(p67|p88)_(contact|face81|edge93)_(r\d\d|p13)(?:_(ring\d\d|al))?__(\w+)$', key)
    if not m:
        return None
    sp, lane, geom, dens, ring, arm = m.groups()
    rho = {'r28': 2.8, 'r33': 3.3, 'r38': 3.8, 'r42': 4.2, 'p13': 4.345}[dens]
    ring = {None: 'Fe1.0', 'ring05': 'Fe0.5', 'ring20': 'Fe2.0', 'al': 'Al1.0'}[ring]
    return sp, geom, dens, rho, ring, arm


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    arm_f = 'asis'
    csvp = None
    only = None
    for a in sys.argv[1:]:
        if a.startswith('--arm='):
            arm_f = a[6:]
        elif a.startswith('--csv='):
            csvp = a[6:]
        elif a.startswith('--only='):
            only = a[7:]
    runs = []
    for d in sorted(glob.glob(os.path.join(OUT, '*__*'))):
        pk = parse_key(os.path.basename(d))
        if not pk:
            continue
        if only and only not in pk[0]:
            continue
        r = read_run(d)
        if r is None:
            continue
        r['spectrum'], r['geom'], r['dens'], r['rho'], r['ring'], r['arm'] = pk
        runs.append(r)
    runs = [r for r in runs if r['arm'] == arm_f or arm_f == 'all']
    runs.sort(key=lambda r: (r['spectrum'], r['geom'], r['ring'], r['rho'], r['arm']))
    noeq = arm_f in ('noeq', 'noeqasis')
    if noeq:
        hdr = '%-20s %-7s %5s %-6s %-8s | %8s %8s %8s %6s | %8s %8s | %7s %6s %6s | %s | %6s' % (
            'спектр', 'геом', 'ρ', 'кольцо', 'пл', 'Ac-228', 'Pb-212', 'Tl-208', 'Pb/Tl', 'Pb-214', 'Bi-214', 'χ²/ndf', 'χ²п', 'невяз', ' '.join('%7s' % ('пик%d' % int(e)) for e in PEAKS), 'сплайн')
    else:
        hdr = '%-20s %-7s %5s %-6s %-8s | %8s %6s %8s %6s | %7s %6s %6s | %s | %6s | %7s %7s | %8s %8s %6s' % (
            'спектр', 'геом', 'ρ', 'кольцо', 'пл', 'A(Th)Бк', 'z', 'A(Ra)Бк', 'z', 'χ²/ndf', 'χ²п', 'невяз', ' '.join('%7s' % ('пик%d' % int(e)) for e in PEAKS), 'сплайн', 'усил', 'ноль', 'r1394', 'r1461', 'b1-2M')
    print(hdr)
    nan = float('nan')
    for r in runs:
        pk = ' '.join('%+6.1f%%' % (100 * r.get('pk%d' % int(e), nan)) for e in PEAKS)
        if noeq:
            pb, tl = r.get('A_Pb-212', nan), r.get('A_Tl-208', nan)
            line = '%-20s %-7s %5.2f %-6s %-8s | %8.1f %8.1f %8.1f %6.3f | %8.1f %8.1f | %7.3f %6.2f %5.1f%% | %s | %5.1f%%' % (
                r['spectrum'][:20], r['geom'], r['rho'], r['ring'], r['arm'], r.get('A_Ac-228', nan), pb, tl, pb / tl if tl else nan,
                r.get('A_Pb-214', nan), r.get('A_Bi-214', nan), r['chi2ndf'] or nan, r['chi2pois'] or nan, 100 * (r['resid'] or nan), pk, r.get('spline', nan))
        else:
            line = '%-20s %-7s %5.2f %-6s %-8s | %8.1f %6.1f %8.1f %6.1f | %7.3f %6.2f %5.1f%% | %s | %5.1f%% | %7.4f %7.2f | %8.0f %8.0f %5.1f%%' % (
                r['spectrum'][:20], r['geom'], r['rho'], r['ring'], r['arm'],
                r.get('A_Th-232', nan), r.get('z_Th-232', nan), r.get('A_Ra-226', nan), r.get('z_Ra-226', nan),
                r['chi2ndf'] or nan, r['chi2pois'] or nan, 100 * (r['resid'] or nan), pk, r.get('spline', nan), r['gain'] or nan, r['offset'] or nan,
                r.get('res1394', nan), r.get('res1461', nan), r.get('band1000', nan))
        print(line)
    if csvp:
        keys = ['spectrum', 'geom', 'rho', 'ring', 'arm', 'A_Th-232', 'z_Th-232', 'A_Ra-226', 'z_Ra-226', 'A_Ac-228', 'A_Pb-212', 'A_Tl-208', 'A_Pb-214', 'A_Bi-214',
                'chi2ndf', 'chi2pois', 'resid', 'pk238', 'pk338', 'pk583', 'pk911', 'pk2614', 'spline', 'gain', 'offset', 'res1394', 'res1461', 'band1000', 'live', 'key']
        with io.open(csvp, 'w', encoding='utf-8', newline='') as fh:
            w = csv.DictWriter(fh, fieldnames=keys, extrasaction='ignore')
            w.writeheader()
            for r in runs:
                w.writerow({k: (('%.6g' % r[k]) if isinstance(r.get(k), float) else r.get(k, '')) for k in keys})


if __name__ == '__main__':
    main()
