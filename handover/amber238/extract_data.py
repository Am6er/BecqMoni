# -*- coding: utf-8 -*-
"""Данные для docs/how-it-works.html (AMBER238): реальные разборы FSA витрины + сечения из matdb.sqlite.

Читает дампы FsaStackShot из tools/fsa_showcase/wd/out (каталог в .gitignore, его
наполняет сторож витрины tools/check_fsa_showcase.py): AS80_Th232_disk__infer_eq и
ASN16_Cs137_house__infer_eq / _peak / _compton (*.curves.csv, *.rates.csv). Сечения —
xcom_* из BecquerelMonitor/matdb.sqlite, открывается только на чтение (mode=ro).

Запуск: python -I extract_data.py <repo> <out.json>
затем:  python -I inject_data.py <repo>/docs/how-it-works.html <out.json>
"""
import csv, json, math, sqlite3, sys, os

repo, out_path = sys.argv[1], sys.argv[2]
OUT = os.path.join(repo, 'tools', 'fsa_showcase', 'wd', 'out')


def r3(x):
    if x == 0:
        return 0
    d = 3 - int(math.floor(math.log10(abs(x))))
    v = round(x, max(d, 0))
    return int(v) if v == int(v) else v


def rebin(stem, group, emin, emax, comps):
    rows = list(csv.DictReader(open(os.path.join(OUT, stem + '.curves.csv'), encoding='utf-8')))
    kev, meas, layers = [], [], {c: [] for c in comps}
    for i in range(0, len(rows) - group, group):
        chunk = rows[i:i + group]
        e = sum(float(r['keV']) for r in chunk) / group
        if e < emin or e > emax:
            continue
        kev.append(round(e, 1))
        meas.append(r3(max(0.0, sum(float(r['fit']) for r in chunk))))
        for c in comps:
            layers[c].append(r3(max(0.0, sum(float(r[c]) for r in chunk))))
    return kev, meas, layers


def rates(stem):
    res = {}
    for r in csv.DictReader(open(os.path.join(OUT, stem + '.rates.csv'), encoding='utf-8')):
        res.setdefault(r['section'], {})[r['name']] = r
    return res


data = {}

# Th-232, ториевый диск на AS80 (NaI 80x80)
th_comps = ['Ac-228', 'Tl-208', 'Pb-212', 'Bi-212', 'Ra-224', 'Th-228', 'Rn-220', 'Th-232',
            'Xray-Pb', 'Xray-Th', 'Xray-La']
kev, meas, layers = rebin('AS80_Th232_disk__infer_eq', 12, 15, 2750, th_comps)
rt = rates('AS80_Th232_disk__infer_eq')
data['th'] = {
    'kev': kev, 'meas': meas, 'layers': layers,
    'share': {c: round(float(rt['component'][c]['share_pct']), 2) for c in th_comps},
    'live': float(rt['meta']['live_time']['count_rate']),
    'chi2': round(float(rt['meta']['chi2ndf_pois']['count_rate']), 2),
    'gain': round(float(rt['meta']['gain']['count_rate']), 5),
    'act': round(float(rt['limit']['Th-232']['count_rate']), 1),
    'lim': round(float(rt['limit']['Th-232']['detection_limit_rate']), 2),
}

# Cs-137 в домике на ASN16: полный слой и два канала матрицы
cs_comps = ['Cs-137', 'Backscatter', 'Xray-Pb', 'Xray-Pb-L', 'pile-up']
kev, meas, layers = rebin('ASN16_Cs137_house__infer_eq', 8, 15, 800, cs_comps)
_, _, lp = rebin('ASN16_Cs137_house__infer_eq_peak', 8, 15, 800, ['Cs-137'])
_, _, lc = rebin('ASN16_Cs137_house__infer_eq_compton', 8, 15, 800, ['Cs-137'])
rt = rates('ASN16_Cs137_house__infer_eq')
data['cs'] = {
    'kev': kev, 'meas': meas, 'layers': layers,
    'peak': lp['Cs-137'], 'compton': lc['Cs-137'],
    'share': {c: round(float(rt['component'][c]['share_pct']), 2) for c in cs_comps},
    'live': float(rt['meta']['live_time']['count_rate']),
    'chi2': round(float(rt['meta']['chi2ndf_pois']['count_rate']), 2),
    'gain': round(float(rt['meta']['gain']['count_rate']), 5),
    'act': round(float(rt['component']['Cs-137']['count_rate']), 1),
    'lim': round(float(rt['component']['Cs-137']['detection_limit_rate']), 2),
}

# Сечения XCOM из matdb.sqlite: NaI, Al (корпус), вода (проба), см2/г
db = sqlite3.connect('file:' + os.path.join(repo, 'BecquerelMonitor', 'matdb.sqlite') + '?mode=ro', uri=True)
NA = 0.6022140857  # 1e24 / моль, чтобы барны -> см2


def element(z):
    a = db.execute('select atomic_weight from xcom_elements where z=?', (z,)).fetchone()[0]
    rows = db.execute('select energy_ev, coherent_b, incoherent_b, photoelectric_b, pair_nuclear_b, pair_electron_b '
                      'from xcom_cross_sections where z=? order by energy_ev', (z,)).fetchall()
    return a, rows


def interp_loglog(rows, col, e_ev):
    for i in range(len(rows) - 1):
        e0, e1 = rows[i][0], rows[i + 1][0]
        if e0 <= e_ev <= e1:
            if e1 == e0:
                return rows[i][col]
            y0, y1 = rows[i][col], rows[i + 1][col]
            if y0 <= 0 or y1 <= 0:
                t = (e_ev - e0) / (e1 - e0)
                return y0 + (y1 - y0) * t
            t = math.log(e_ev / e0) / math.log(e1 / e0)
            return math.exp(math.log(y0) + (math.log(y1) - math.log(y0)) * t)
    return 0.0


def material(parts):
    """parts: [(z, массовая доля)] -> функция E кэВ -> (photo, compton, pair, total) см2/г"""
    els = [(element(z), w) for z, w in parts]

    def mu(e_kev):
        e = e_kev * 1000.0
        ph = co = pa = rl = 0.0
        for (a, rows), w in els:
            k = w * NA / a
            # над краем: берём точку чуть выше, чтобы не попасть на двойную запись края
            ph += k * interp_loglog(rows, 3, e)
            co += k * interp_loglog(rows, 2, e)
            pa += k * (interp_loglog(rows, 4, e) + interp_loglog(rows, 5, e))
            rl += k * interp_loglog(rows, 1, e)
        return ph, co, pa, ph + co + pa + rl
    return mu


grid = sorted(set([round(15 * (3200 / 15) ** (i / 63), 2) for i in range(64)] + [33.1, 33.25, 87.9, 88.1]))
mats = {
    'NaI': material([(11, 22.98977 / (22.98977 + 126.90447)), (53, 126.90447 / (22.98977 + 126.90447))]),
    'Al': material([(13, 1.0)]),
    'H2O': material([(1, 2 * 1.00794 / 18.01528), (8, 15.9994 / 18.01528)]),
}
data['xs'] = {'E': grid}
for name, f in mats.items():
    vals = [f(e) for e in grid]
    data['xs'][name] = {
        'ph': [r3(v[0]) for v in vals], 'co': [r3(v[1]) for v in vals],
        'pa': [r3(v[2]) for v in vals], 'tot': [r3(v[3]) for v in vals],
    }

with open(out_path, 'w', encoding='utf-8') as fh:
    json.dump(data, fh, ensure_ascii=False, separators=(',', ':'))
print('ok', os.path.getsize(out_path), 'байт; th bins', len(data['th']['kev']), 'cs bins', len(data['cs']['kev']))
print('NaI 662 keV', [mats['NaI'](662)], 'Al 60', mats['Al'](60))
