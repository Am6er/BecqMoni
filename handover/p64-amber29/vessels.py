# -*- coding: utf-8 -*-
"""П64 (AMBER29), п. 6 — два сосуда: ε(А)/ε(Б) по узлам кривой и по линиям, и как меняются
активности п. 1 при смене сосуда.

Кривые — из узла <Efficiency><Curve> копий спектров, куда их положил `CorpusEffProbe`
(`coalA_e01.xml` — сцена А ОМАСН, `coalB_e01.xml` — сцена Б корпусный); активности — из
`activities_A_noeq.csv` / `activities_B_noeq.csv` (`collect_rates.py`).

    python handover/p64-amber29/vessels.py <spectra_ab> <каталог activities> [--out=<csv>]
"""
import csv
import io
import math
import os
import re
import sys

import numpy as np

LINES = [242.0, 295.2, 351.9, 609.3, 1120.3, 1764.5, 2614.5]


def curve_of(path):
    s = io.open(path, encoding='utf-8-sig').read()
    m = re.search(r'<Efficiency>.*?<Curve>(.*?)</Curve>.*?<ComputeStamp>(.*?)</ComputeStamp>', s, re.S)
    if not m:
        raise SystemExit('нет кривой в ' + path)
    pts = re.findall(r'<Energy>([^<]+)</Energy><Efficiency>([^<]+)</Efficiency><ErrorPercent>([^<]+)</ErrorPercent>', m.group(1))
    e = np.array([float(a) for a, _, _ in pts])
    eff = np.array([float(b) for _, b, _ in pts])
    err = np.array([float(c) for _, _, c in pts])
    return e, eff, err, m.group(2)


def interp_log(e, eff, x):
    return math.exp(np.interp(math.log(x), np.log(e), np.log(np.maximum(eff, 1e-30))))


def main():
    sp, adir = sys.argv[1], sys.argv[2]
    out = None
    for a in sys.argv[3:]:
        if a.startswith('--out='):
            out = a[6:]
    eA, fA, sA, stA = curve_of(os.path.join(sp, 'coalA_e01.xml'))
    eB, fB, sB, stB = curve_of(os.path.join(sp, 'coalB_e01.xml'))
    print('клеймо А: %s\nклеймо Б: %s' % (stA, stB))
    lines = ['section,energy_kev,eff_A,eff_B,ratio_A_over_B,err_pct']
    print('%8s %12s %12s %8s %6s' % ('E, кэВ', 'ε(А)', 'ε(Б)', 'А/Б', 'σ,%'))
    for i in range(len(eA)):
        if eA[i] < 30:
            continue
        j = int(np.argmin(np.abs(eB - eA[i])))
        r = fA[i] / fB[j]
        pe = math.sqrt(sA[i] ** 2 + sB[j] ** 2)
        print('%8.1f %12.5g %12.5g %8.4f %6.2f' % (eA[i], fA[i], fB[j], r, pe))
        lines.append('node,%.3f,%.6g,%.6g,%.5f,%.3f' % (eA[i], fA[i], fB[j], r, pe))
    print('по линиям:')
    for x in LINES:
        a, b = interp_log(eA, fA, x), interp_log(eB, fB, x)
        print('  %7.1f кэВ: ε(А) %.5g, ε(Б) %.5g, А/Б = %.4f' % (x, a, b, a / b))
        lines.append('line,%.1f,%.6g,%.6g,%.5f,' % (x, a, b, a / b))
    # активности
    def load(p):
        with io.open(p, encoding='utf-8-sig') as fh:
            return {r['key']: r for r in csv.DictReader(fh)}
    A = load(os.path.join(adir, 'activities_A_noeq.csv'))
    B = load(os.path.join(adir, 'activities_B_noeq.csv'))
    print('активности Б/А по съёмкам (равновесные), Pb-214 и Bi-214, и Bi/Pb в обоих:')
    for nuc in ('Pb-214', 'Bi-214', 'Pb-212', 'Ra-226', 'Tl-208'):
        qs = []
        for k in sorted(A):
            if not k.startswith('coal_e'):
                continue
            a, b = A[k], B[k]
            if a[nuc + '_det'] == '1' and b[nuc + '_det'] == '1' and float(a[nuc + '_bq']) > 0:
                q = float(b[nuc + '_bq']) / float(a[nuc + '_bq'])
                sq = q * math.sqrt((float(a[nuc + '_err']) / float(a[nuc + '_bq'])) ** 2 + (float(b[nuc + '_err']) / float(b[nuc + '_bq'])) ** 2)
                qs.append((q, sq))
        if qs:
            w = np.array([1 / s ** 2 for _, s in qs])
            m = sum(q * wi for (q, _), wi in zip(qs, w)) / w.sum()
            print('  %-7s Б/А = %.4f ± %.4f (взвешенно по %d съёмкам; разброс %.4f…%.4f)'
                  % (nuc, m, math.sqrt(1 / w.sum()), len(qs), min(q for q, _ in qs), max(q for q, _ in qs)))
            lines.append('activity_B_over_A,%s,,,%.5f,%.5f' % (nuc, m, math.sqrt(1 / w.sum())))
    for tag, T in (('A', A), ('B', B)):
        rs = []
        for k in sorted(T):
            if k.startswith('coal_e') and T[k]['Pb-214_det'] == '1' and T[k]['Bi-214_det'] == '1':
                pb, bi = float(T[k]['Pb-214_bq']), float(T[k]['Bi-214_bq'])
                q = bi / pb
                sq = q * math.sqrt((float(T[k]['Pb-214_err']) / pb) ** 2 + (float(T[k]['Bi-214_err']) / bi) ** 2)
                rs.append((q, sq))
        w = np.array([1 / s ** 2 for _, s in rs])
        m = sum(q * wi for (q, _), wi in zip(rs, w)) / w.sum()
        print('  сцена %s: Bi-214/Pb-214 = %.4f ± %.4f (%d съёмок)' % (tag, m, math.sqrt(1 / w.sum()), len(rs)))
        lines.append('BiPb_%s,,,,%.5f,%.5f' % (tag, m, math.sqrt(1 / w.sum())))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
