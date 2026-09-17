# -*- coding: utf-8 -*-
r"""П88: невязка ПО ПОЛОСАМ энергии для четырёх плеч одного спектра×сцены — где сидит расхождение fit − model
(Σ(fit − model) / Σ|fit| и Σ|fit − model| / Σ|fit| по полосам, из dump.csv FsaStackShot).

    python bands_p88.py <спектр> <сцена> [плечи через запятую]
"""
import csv
import io
import os
import sys

OUT = r'D:\BqMoni_Claude\p88\out'
BANDS = [(15, 60), (60, 100), (100, 200), (200, 400), (400, 700), (700, 1000), (1000, 1300), (1300, 1600), (1600, 2000), (2000, 2800)]


def load(d):
    rows = list(csv.DictReader(io.open(os.path.join(d, 'dump.csv'), encoding='utf-8-sig')))
    return [float(r['keV']) for r in rows], [float(r['fit']) for r in rows], [float(r['model']) for r in rows]


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    sp, scene = sys.argv[1], sys.argv[2]
    arms = sys.argv[3].split(',') if len(sys.argv) > 3 else ['asis', 'eq']
    specs = [sp + '_bg0', sp]
    print('%-14s | %s' % ('полоса, кэВ', ' | '.join('%-22s' % ('%s/%s' % (s.replace(sp, 'X'), a)) for s in specs for a in arms)))
    print('%-14s | %s' % ('', ' | '.join('%10s %11s' % ('Σ(f−m)/Σf', 'Σ|f−m|/Σf') for s in specs for a in arms)))
    data = {}
    for s in specs:
        for a in arms:
            d = os.path.join(OUT, '%s__%s__%s' % (s, scene, a))
            data[(s, a)] = load(d) if os.path.exists(os.path.join(d, 'dump.csv')) else None
    for lo, hi in BANDS:
        cells = []
        for s in specs:
            for a in arms:
                x = data[(s, a)]
                if x is None:
                    cells.append('%22s' % '—'); continue
                kev, fit, model = x
                ids = [i for i, k in enumerate(kev) if lo <= k < hi]
                sf = sum(abs(fit[i]) for i in ids) or 1e-9
                cells.append('%+9.1f %% %10.1f %%' % (100 * sum(fit[i] - model[i] for i in ids) / sf, 100 * sum(abs(fit[i] - model[i]) for i in ids) / sf))
        print('%-14s | %s' % ('%d–%d' % (lo, hi), ' | '.join(cells)))


if __name__ == '__main__':
    main()
