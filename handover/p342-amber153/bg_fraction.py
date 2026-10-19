# -*- coding: utf-8 -*-
u"""П342 (AMBER153 (а)): какую долю счёта сосудного спектра составляет вычитаемый фон.

Для каждого спектра G1S в маринелли / Денте / Петри с паспортом: фон (нормирован на живое
время спектра, β = 1, как вычитает разбор, §3.6) против счёта спектра по полосам энергии.
Если ошибка фона δ (доля), то нетто полосы сдвигается на −δ·b/(n − b) — это верхняя оценка
сдвига вклада полосы в амплитуду. Пишет `bg_fraction.csv`.

Положительный контроль: спектр «фон против самого себя» (донор фона как проба) обязан дать
долю 1.000 во всех полосах — строка `CONTROL`.

    python handover/p342-amber153/bg_fraction.py
"""
import csv
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, HERE)
import corpus_xml as cx  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

SPECTRA = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'spectra')
PASSPORT = os.path.join(HERE, 'passport_rev47_full.csv')
BANDS = [(20, 40), (40, 70), (70, 100), (100, 200), (200, 400), (400, 700), (700, 1000),
         (1000, 1500), (1500, 2000), (2000, 2800)]


def vessels():
    out = []
    for name in sorted(os.listdir(SPECTRA)):
        key = name[:-4]
        if key.startswith('G1S') and any(s in key for s in ('_Mar', 'Denta', 'Petri')) and 'Coal' not in key:
            out.append(key)
    return out


def main():
    ratio = {}
    if os.path.isfile(PASSPORT):
        for r in csv.DictReader(io.open(PASSPORT, encoding='utf-8-sig')):
            ratio[r['spectrum']] = float(r['ratio'])
    rows = []
    hdr = ['spectrum', 'bg_file', 'source', 'rho', 'passport'] + ['%d-%d' % b for b in BANDS] + ['total']
    print('доля фона b/n по полосам, кэВ (фон × LT_спектра/LT_фона)')
    print('%-24s %-6s %5s %6s ' % ('спектр', 'проба', 'ρ', 'пасп.') +
          ' '.join('%9s' % ('%d-%d' % b) for b in BANDS) + '    всего')
    keys = vessels()
    for key in keys + ['CONTROL']:
        if key == 'CONTROL':
            d = cx.read(os.path.join(SPECTRA, keys[0] + '.xml'))
            d['fg'] = dict(d['bg'])
        else:
            d = cx.read(os.path.join(SPECTRA, key + '.xml'))
        fg, bg = d['fg'], d['bg']
        k = fg['live'] / bg['live']
        fr = []
        for e0, e1 in BANDS:
            n = cx.band(fg, e0, e1)
            b = cx.band(bg, e0, e1) * k
            fr.append(b / n if n else float('nan'))
        tot = sum(bg['counts']) * k / sum(fg['counts'])
        g = d['geo']
        src = g.get('SourceName', '') if key != 'CONTROL' else 'фон'
        rho = g.get('SourceDensity', '')
        pas = ratio.get(key, float('nan'))
        print('%-24s %-6s %5s %6.3f ' % (key, src.replace('ОИСН-', 'О'), rho[:5], pas) +
              ' '.join('%9.4f' % f for f in fr) + '   %6.4f' % tot)
        rows.append([key, d['bg_file'], src, rho, '%.4f' % pas] + ['%.5f' % f for f in fr] + ['%.5f' % tot])
    with io.open(os.path.join(HERE, 'bg_fraction.csv'), 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(hdr)
        w.writerows(rows)


if __name__ == '__main__':
    main()
