# -*- coding: utf-8 -*-
u"""П342 (AMBER153, остаток (б)): ряд Ra-226 без Pb-210 — плечи A0 / E0 / E1 по спектрам эталонов Ra-226.

A0 — штатно: ряд «Ra-226» одной амплитудой, Pb-210 46.5 кэВ навязан равновесным;
E0 — подряд «Rn-222» (до Pb-210) одной амплитудой + Ra-226 (186 кэВ) отдельным нуклидом, Pb-210 нет;
E1 — E0 + Pb-210 отдельным нуклидом со своей амплитудой.

Печатает разбор/паспорт по амплитуде подряда Rn-222 (в A0 — по амплитуде ряда; у членов она общая), долю
Pb-210 из E1 к паспорту Ra-226 на дату набора и ОЖИДАНИЕ нарастания Pb-210 от даты паспорта, если на дату
паспорта Pb-210 в эталоне не было: 1 − 2^(−t/22.20 г) (радон удержан; при эманации — меньше). χ² Пуассона.
Пишет `ra_chain.csv`.
"""
import csv
import datetime
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(REPO, 'tools', 'pie'))
import passport  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

T_PB210_Y = 22.20
ARMS = ('A0', 'E0', 'E1')


def comps(arm):
    out = {}
    d = os.path.join(REPO, 'tools', 'pie', 'out_p342_' + arm)
    for g in ('G1S16', 'G1S24'):
        for r in csv.DictReader(io.open(os.path.join(d, g + '_spline_components.csv'), encoding='utf-8-sig')):
            out[(r['spectrum'], r['component'])] = r
        for r in csv.DictReader(io.open(os.path.join(d, g + '_spline_runs.csv'), encoding='utf-8-sig')):
            out[(r['spectrum'], '__run')] = r
    return out


def main():
    refs = {}
    for key, nuc, a0, unit, da, d0, how in passport.references()[0]:
        if nuc == 'Ra-226' and key.startswith('G1S') and ('Mar' in key or 'Denta' in key or 'Petri' in key):
            text = passport._xml_head(key)
            d1 = passport.start_of(text)
            kind, dist, mass = passport.geometry_of(text)
            hl = passport.HalfLives().days('Ra-226')
            a_ref = a0 * 0.5 ** ((d1 - d0).total_seconds() / 86400.0 / hl) * mass / 1000.0
            refs[key] = (a_ref, d0, d1, da)
    data = {a: comps(a) for a in ARMS}
    rows = []
    print('%-24s %-10s %6s | %7s %7s %7s | %7s %7s %7s | %8s %8s | %7s %7s %7s' % (
        'спектр', 'паспорт', 't, лет', 'A0', 'E0', 'E1', 'ΔE0,%', 'ΔE1,%', 'Ra186E0', 'Pb210E1', 'ожид.',
        'χ²A0', 'χ²E0', 'χ²E1'))
    for key in sorted(refs):
        a_ref, d0, d1, da = refs[key]
        t_y = (d1 - d0).total_seconds() / 86400.0 / 365.2422
        exp_pb = 1.0 - 0.5 ** (t_y / T_PB210_Y)
        r = {}
        for a in ARMS:
            c = data[a]
            chain = c.get((key, 'Pb-214'))
            r[a] = float(chain['decay_s']) / a_ref if chain else float('nan')
        ra186 = data['E0'].get((key, 'Ra-226'))
        ra186 = float(ra186['decay_s']) / a_ref if ra186 else float('nan')
        pb = data['E1'].get((key, 'Pb-210'))
        if pb:
            pbv = float(pb['decay_s']) / a_ref
            pbs = float(pb['dt_decay_s'] or 'nan') / a_ref
        else:
            pbv, pbs = 0.0, float('nan')
        chi = [float(data[a][(key, '__run')]['chi2ndf_pois']) for a in ARMS]
        print('%-24s %s %6.1f | %7.3f %7.3f %7.3f | %+7.2f %+7.2f %7.3f | %5.3f±%.3f %8.3f | %7.3f %7.3f %7.3f' % (
            key, d0.strftime('%d.%m.%Y'), t_y, r['A0'], r['E0'], r['E1'], 100 * (r['E0'] / r['A0'] - 1),
            100 * (r['E1'] / r['A0'] - 1), ra186, pbv, pbs if pbs == pbs else 0.0, exp_pb, chi[0], chi[1], chi[2]))
        rows.append([key, d0.strftime('%Y-%m-%d'), d1.strftime('%Y-%m-%d'), '%.2f' % t_y, '%.1f' % a_ref,
                     '%.5f' % r['A0'], '%.5f' % r['E0'], '%.5f' % r['E1'], '%.5f' % ra186, '%.5f' % pbv,
                     '%.5f' % pbs, '%.4f' % exp_pb] + ['%.4f' % x for x in chi])
    with io.open(os.path.join(HERE, 'ra_chain.csv'), 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(['spectrum', 'passport_date', 'start', 'years', 'ref_bq', 'ratio_A0', 'ratio_E0_rn',
                    'ratio_E1_rn', 'ra186_E0', 'pb210_E1_over_ra', 'pb210_E1_sigma', 'pb210_expected_ingrowth',
                    'chi2pois_A0', 'chi2pois_E0', 'chi2pois_E1'])
        w.writerows(rows)


if __name__ == '__main__':
    main()
