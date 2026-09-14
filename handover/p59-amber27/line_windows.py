# -*- coding: utf-8 -*-
"""П59 (AMBER27): что модель кладёт в окна характерных линий — по `FsaStackShot --dump=`.

    python handover/p59-amber27/line_windows.py <каталог с curves_*.csv> [--out=<csv>]

Для каждого прогона и каждой линии: Σ net (измерение за вычетом фона), Σ model, Σ(model − net)
и в единицах σ = sqrt(Σ net) (грубо, без фона) в окне ±2·ПШПВ(E), ПШПВ(E) = 6.66 %·√(662·E) кэВ
(модель ASN16, `DS_Fwhm662`; для AS80 — 7.65 %). Плечо «равновесие ВКЛ» должно показать, где
связка ряда навязала линии отсутствующих членов (Ra-226 186, Pb-210 46.5, Ac-228 338/911/969).
"""
import csv
import glob
import io
import math
import os
import sys

LINES = [('Pb-210 46.5', 46.54), ('Ra-226 186', 186.21), ('Pb-212 239', 238.63), ('Pb-214 295', 295.22),
         ('Ac-228 338', 338.32), ('Pb-214 352', 351.93), ('Tl-208 583', 583.19), ('Bi-214 609', 609.32),
         ('Bi-212 727', 727.33), ('Ac-228 911', 911.2), ('Ac-228 969', 968.97), ('Bi-214 1120', 1120.29),
         ('Bi-214 1764', 1764.49), ('Tl-208 2614', 2614.51)]


def fwhm(e, pct):
    return pct / 100.0 * math.sqrt(662.0 * e)


def main():
    d = sys.argv[1]
    out = None
    for a in sys.argv[2:]:
        if a.startswith('--out='):
            out = a[6:]
    lines = ['run,line,keV,window_keV,sum_net,sum_model,model_minus_net,in_sigma']
    for f in sorted(glob.glob(os.path.join(d, 'curves_*.csv'))):
        key = os.path.basename(f)[7:-4]
        pct = 7.65 if key.startswith('as80') else 6.66
        rows = []
        with io.open(f, encoding='utf-8', newline='') as fh:
            for r in csv.DictReader(fh):
                rows.append((float(r['keV']), float(r['net']), float(r['model'])))
        for name, e in LINES:
            w = 2.0 * fwhm(e, pct)
            sn = sm = 0.0
            for kev, net, model in rows:
                if e - w <= kev <= e + w:
                    sn += net
                    sm += model
            sig = math.sqrt(abs(sn)) if sn != 0 else float('nan')
            lines.append('%s,%s,%.2f,±%.1f,%.0f,%.0f,%.0f,%.1f' % (
                key, name, e, w, sn, sm, sm - sn, (sm - sn) / sig if sig == sig and sig > 0 else float('nan')))
    print('\n'.join(lines))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
