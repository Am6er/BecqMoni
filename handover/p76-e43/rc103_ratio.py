# -*- coding: utf-8 -*-
r"""П76 (E43) — изм/ожид двух контактных спектров RC103 по каталогам прогона (`RC103_spline_components.csv`,
`RC103_spline_runs.csv`): измеренная активность `decay_s` против паспорта на дату съёмки.

Паспорта — из П73 (журнал handover-2026-09-14-p73-v10-rc103-50mm.md): Cs-137 9.25 кБк на 02.01.2002, T½ 30.08 л
→ 5564.3 Бк на 23.01.2024 (RC103_Cs137_0cm, контакт); банка Lu₂O₃ 20 г → 919.1 Бк (`scripts/lu176_activity.py`,
Lu-176 практически вечен). Разделитель дробной части — точка.

    python handover/p76-e43/rc103_ratio.py <метка>=<каталог прогона> [<метка>=<каталог> …]
"""
import csv
import io
import os
import sys

PASSPORT = {
    'RC103_Cs137_0cm': ('Cs-137', 5564.3),
    'RC103_Lu176': ('Lu-176', 919.1),
}


def rows(path):
    if not os.path.isfile(path):
        return []
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def main():
    arms = [a.split('=', 1) for a in sys.argv[1:] if '=' in a]
    print(u'%-18s %-10s %10s %10s %10s %8s %8s %8s' % (u'спектр', u'плечо', u'паспорт,Бк', u'изм,Бк', u'изм/ожид', u'z', u'χ²/ndf', u'χ²п'))
    for key, (comp, passport) in PASSPORT.items():
        for label, d in arms:
            comps = rows(os.path.join(d, 'RC103_spline_components.csv'))
            runs = rows(os.path.join(d, 'RC103_spline_runs.csv'))
            meas = [r for r in comps if r['spectrum'] == key and r['component'] == comp]
            run = [r for r in runs if r['spectrum'] == key]
            if not meas or not run:
                print(u'%-18s %-10s нет строки в %s' % (key, label, d)); continue
            a = float(meas[0]['decay_s'])
            print(u'%-18s %-10s %10.1f %10.1f %10.3f %8s %8s %8s'
                  % (key, label, passport, a, a / passport, meas[0]['z'], run[0]['chi2ndf'], run[0]['chi2ndf_pois']))


if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass
    sys.exit(main())
