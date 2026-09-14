# -*- coding: utf-8 -*-
"""П42 13.09.2026, `AMBER22` — кто держит модель по полосам: net, model, model/net−1, доля сплайна и вклад
каждого образа (в % от модели) по полосам шкалы; по дампу FsaStackShot (--dump=).

    python handover/p42-amber22/bands.py <dump.csv> [lo:hi ...]
"""
import csv
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

DEF = [(15.84, 30), (30, 45), (45, 60), (60, 75), (75, 90), (90, 110), (110, 150), (150, 200), (200, 225), (225, 252),
       (252, 290), (290, 320), (320, 360), (360, 400), (400, 440), (440, 500), (500, 560), (560, 610), (610, 700),
       (700, 800), (800, 880), (880, 1000), (1000, 1200), (1200, 1500), (1500, 1700), (1700, 2000), (2000, 2400),
       (2400, 2550), (2550, 2700), (2700, 2800)]


def main():
    path = sys.argv[1]
    bands = DEF
    if len(sys.argv) > 2:
        bands = [tuple(float(v) for v in a.split(':')) for a in sys.argv[2:]]
    rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
    names = [k for k in rows[0].keys() if k not in ('ch', 'keV', 'net', 'model', 'continuum_raw')]
    kev = [float(r['keV']) for r in rows]
    net = [float(r['net']) for r in rows]
    model = [float(r['model']) for r in rows]
    spl = [float(r['continuum_raw']) for r in rows]
    comp = {n: [float(r[n]) for r in rows] for n in names}
    print('%-12s %9s %9s %7s %6s | %s' % ('полоса', 'net', 'model', 'm/n−1', 'сплайн', ' '.join('%7s' % n[:7] for n in names)))
    for lo, hi in bands:
        ids = [i for i, e in enumerate(kev) if lo <= e < hi]
        sn = sum(net[i] for i in ids)
        sm = sum(model[i] for i in ids)
        ss = sum(spl[i] for i in ids)
        parts = ' '.join('%6.1f%%' % (100.0 * sum(comp[n][i] for i in ids) / sm if sm else 0) for n in names)
        print('%-12s %9.0f %9.0f %+6.1f%% %5.1f%% | %s' % ('%g-%g' % (lo, hi), sn, sm, 100.0 * (sm / sn - 1) if sn else 0, 100.0 * ss / sm if sm else 0, parts))


if __name__ == '__main__':
    main()
