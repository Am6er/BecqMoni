# -*- coding: utf-8 -*-
u"""П342 (AMBER153, остаток (в)): равновесен ли ряд тория у эталонов — члены ряда своими амплитудами.

N0 — штатно (ряд одной амплитудой, `S70`); N1 — `--no-equilibrium`: у Ac-228 (ветвь Ra-228, 338/911/969 кэВ),
Pb-212 (ветвь Th-228, 239 кэВ), Bi-212 и Tl-208 (583/2614 кэВ) — свои амплитуды. Отношение к паспорту Th-232 на
начало набора (масса по сосуду файла — как у мерки корпуса). Для сравнения — ОЖИДАНИЕ, если бы эталон был
сделан из ХИМИЧЕСКИ ОЧИЩЕННОГО тория в дату паспорта (Ra-228 = 0, Th-228 = Th-232): Бейтман Th-232 → Ra-228
(5.75 г) → Th-228 (1.912 г). Пишет `th_chain.csv`.
"""
import csv
import io
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(REPO, 'tools', 'pie'))
import passport  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

L_RA = math.log(2) / 5.75
L_TH = math.log(2) / 1.912


def purified(t):
    u"""(Ra-228/Th-232, Th-228/Th-232) через t лет после очистки тория (Ra-228 = 0, Th-228 = 1 в t = 0)."""
    ra = 1.0 - math.exp(-L_RA * t)
    # dTh/dt = λ_Th (Ra − Th), Ra = 1 − e^{−λ_Ra t}, Th(0) = 1
    th = 1.0 - L_TH / (L_TH - L_RA) * math.exp(-L_RA * t) + (L_TH / (L_TH - L_RA)) * math.exp(-L_TH * t)
    return ra, th


def comps(arm):
    out = {}
    d = os.path.join(REPO, 'tools', 'pie', 'out_p342_' + arm)
    for g in ('G1S16', 'G1S24'):
        for r in csv.DictReader(io.open(os.path.join(d, g + '_spline_components.csv'), encoding='utf-8-sig')):
            out[(r['spectrum'], r['component'])] = r
    return out


def main():
    n0, n1 = comps('N0'), comps('N1')
    hl = passport.HalfLives()
    rows = []
    print('%-24s %-10s %5s %6s | %6s | %6s %6s %6s %6s | %6s | очищ.: Ra-228 Th-228' % (
        'спектр', 'паспорт', 't,лет', 'нукл.', 'ряд', 'Ac228', 'Pb212', 'Bi212', 'Tl208', 'Ac/Pb'))
    for key, nuc, a0, unit, da, d0, how in passport.references()[0]:
        if nuc not in ('Th-232', 'Th-228') or not key.startswith('G1S'):
            continue
        if (key, 'Pb-212') not in n1:
            continue
        text = passport._xml_head(key)
        d1 = passport.start_of(text)
        kind, dist, mass = passport.geometry_of(text)
        a_ref = a0 * 0.5 ** ((d1 - d0).total_seconds() / 86400.0 / hl.days(nuc))
        if unit == 'Бк/кг':
            a_ref *= mass / 1000.0
        t = (d1 - d0).total_seconds() / 86400.0 / 365.2422
        chain = float(n0[(key, nuc)]['decay_s']) / a_ref if (key, nuc) in n0 else float('nan')

        def m(c):
            r = n1.get((key, c))
            return float(r['decay_s']) / a_ref if r else float('nan')

        ac, pb, bi, tl = m('Ac-228'), m('Pb-212'), m('Bi-212'), m('Tl-208')
        acpb = ac / pb if pb == pb and ac == ac else float('nan')
        ra_p, th_p = purified(t) if nuc == 'Th-232' else (float('nan'), float('nan'))
        print('%-24s %s %5.1f %6s | %6.3f | %6.3f %6.3f %6.3f %6.3f | %6.3f | %6.3f %6.3f' % (
            key, d0.strftime('%d.%m.%Y'), t, nuc, chain, ac, pb, bi, tl, acpb, ra_p, th_p))
        rows.append([key, nuc, d0.strftime('%Y-%m-%d'), d1.strftime('%Y-%m-%d'), '%.2f' % t, '%.2f' % a_ref,
                     '%.5f' % chain, '%.5f' % ac, '%.5f' % pb, '%.5f' % bi, '%.5f' % tl, '%.5f' % acpb,
                     '%.4f' % ra_p, '%.4f' % th_p])
    with io.open(os.path.join(HERE, 'th_chain.csv'), 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(['spectrum', 'nuclide', 'passport_date', 'start', 'years', 'ref_bq', 'chain_N0', 'ac228_N1',
                    'pb212_N1', 'bi212_N1', 'tl208_N1', 'ac_over_pb', 'purified_ra228', 'purified_th228'])
        w.writerows(rows)


if __name__ == '__main__':
    main()
