# -*- coding: utf-8 -*-
r"""П66 — приёмка сосудов ЧИСЛОМ: паспортные активности эталонов G1S в маринелли 1 л против
измеренных разбором (`decay_s` компонента в `<группа>_spline_components.csv` каталога прогона
`CorpusFsaProbe`) — до и после смены сосуда (плечи 0 / A / B).

Паспорт — из графы `why` манифеста («паспорт: <нуклид> A=<Бк/кг> dA=<%> DD-MM-YYYY»), масса
пробы — `data/lsrm_spectrum_geometry.csv` (`масса_г`), дата съёмки — `<SampleInfo><Time>` копии
спектра; паспортная активность приводится к дате съёмки (Cs-137 30.08 л, Am-241 432.6 л;
Ra-226/Th-232/K-40 — без поправки). Измеренное — `decay_s` строки компонента (у ряда со связкой
одно число на все члены; берётся строка корня ряда либо одиночного нуклида).
⚠ Паспорт первичен (решение Amber 14.08.2026): расхождение — свойство симуляции.
⚠ У смеси `G1S16_Mix_Mar` паспорт манифеста называет только Am-241 — сравнивается он.

    python handover/p66-rev23/passports.py <метка>=<каталог прогона> [<метка>=<каталог> …] [--csv=<файл>]
"""
import csv
import io
import math
import os
import re
import sys
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
CORPUS = os.path.join(REPO, 'tools', 'CORPUS', 'corpus')
GEO = os.path.join(REPO, 'tools', 'CORPUS', 'data', 'lsrm_spectrum_geometry.csv')

HALF_LIFE_Y = {'Cs-137': 30.08, 'Am-241': 432.6, 'Ra-226': 1600.0, 'Th-232': 1.405e10, 'K-40': 1.248e9}
# нуклид паспорта -> имя компонента разбора (корень ряда / одиночный)
COMPONENT = {'Cs-137': 'Cs-137', 'Am-241': 'Am-241', 'Ra-226': 'Ra-226', 'Th-232': 'Th-232', 'K-40': 'K-40'}
PASSPORT = re.compile(u'паспорт: (\\S+) A=([0-9.E+e]+) Бк/кг dA=([0-9.]+)% (\\d\\d)-(\\d\\d)-(\\d{4})')


def read_csv(path):
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def spectrum_time(key):
    t = io.open(os.path.join(CORPUS, 'spectra', key + '.xml'), encoding='utf-8-sig').read()
    m = re.search(r'<Time>([^<]*)</Time>', t)
    return datetime.strptime(m.group(1)[:19], '%Y-%m-%dT%H:%M:%S')


def measured(out_dir, det, key, comp):
    path = os.path.join(out_dir, det + '_spline_components.csv')
    if not os.path.isfile(path):
        return None, None
    for r in read_csv(path):
        if r['spectrum'] == key and r['component'] == comp:
            try:
                return float(r['decay_s']), float(r['share_pct'])
            except ValueError:
                return None, None
    return None, None


def main():
    arms = []
    csv_out = None
    for a in sys.argv[1:]:
        if a.startswith('--csv='):
            csv_out = a[6:]
        elif '=' in a:
            arms.append(tuple(a.split('=', 1)))
    manifest = {r['key']: r for r in read_csv(os.path.join(CORPUS, 'manifest.csv'))}
    index = read_csv(os.path.join(CORPUS, 'geometries', 'index.csv'))
    geo = {r[u'спектр']: r for r in read_csv(GEO)}
    keys = sorted(r['spectrum'] for r in index if r['geometry'].startswith('G1S_mar1l_') and r['spectrum'] in geo)
    rows = []
    print('%-20s %-7s %8s %8s %6s' % ('спектр', 'нуклид', 'масса,г', 'паспорт', 'dA,%') + ''.join('  %10s %6s' % (lab, 'изм/п') for lab, _ in arms))
    ratios = dict((lab, []) for lab, _ in arms)
    for key in keys:
        m = manifest[key]
        hit = PASSPORT.search(m['why'])
        if not hit:
            print('%-20s нет паспорта в манифесте' % key)
            continue
        nuc, a_kg, da, dd, mm, yy = hit.group(1), float(hit.group(2)), float(hit.group(3)), int(hit.group(4)), int(hit.group(5)), int(hit.group(6))
        mass_g = float(geo[key][u'масса_г'])
        t_pass = datetime(yy, mm, dd)
        t_meas = spectrum_time(key)
        years = (t_meas - t_pass).days / 365.25
        bq = a_kg * mass_g / 1000.0 * math.exp(-math.log(2.0) * years / HALF_LIFE_Y[nuc])
        line = '%-20s %-7s %8.0f %8.1f %6.1f' % (key, nuc, mass_g, bq, da)
        rec = dict(spectrum=key, nuclide=nuc, mass_g=mass_g, passport_bq=round(bq, 2), dA_pct=da, years=round(years, 2))
        for lab, d in arms:
            v, sh = measured(d, m['det'], key, COMPONENT[nuc])
            if v is None:
                line += '  %10s %6s' % ('—', '—')
                rec[lab + '_bq'] = ''
                rec[lab + '_ratio'] = ''
            else:
                line += '  %10.1f %6.3f' % (v, v / bq)
                rec[lab + '_bq'] = round(v, 2)
                rec[lab + '_ratio'] = round(v / bq, 4)
                ratios[lab].append(v / bq)
        print(line)
        rows.append(rec)
    print()
    for lab, _ in arms:
        rs = sorted(ratios[lab])
        if rs:
            med = rs[len(rs) // 2] if len(rs) % 2 else 0.5 * (rs[len(rs) // 2 - 1] + rs[len(rs) // 2])
            mean = sum(rs) / len(rs)
            sd = math.sqrt(sum((x - mean) ** 2 for x in rs) / max(1, len(rs) - 1))
            print('%-10s n=%d  изм/паспорт: медиана %.3f, среднее %.3f ± %.3f (СКО), мин %.3f, макс %.3f'
                  % (lab, len(rs), med, mean, sd, rs[0], rs[-1]))
    if csv_out and rows:
        with io.open(csv_out, 'w', encoding='utf-8', newline='') as fh:
            w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
    return 0


if __name__ == '__main__':
    sys.exit(main())
