# -*- coding: utf-8 -*-
u"""П342 (AMBER153, остаток (в)/(г)): разложение «разбор/паспорт» сосудов G1S на множители — ОДИН эталон в
разных сосудах и эпохах, разные нуклиды в одном сосуде. Источник — мерка корпуса на объявленной базе
(`passport_rev47_full.csv`, rev47 полная понятная, только чтение); эталон — из графы «источник»
`tools/CORPUS/data/lsrm_spectrum_geometry.csv`. Пишет `vessel_factors.txt` (печать).
"""
import csv
import io
import os
import re
import statistics
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def std_id(src):
    m = re.search(r'(420[-_ ]?7[-_ ]?\d+|420[-_ ]?17031|420_13036|РИСН ?№?379|AmTiCsEu)', src)
    return m.group(1).replace('_', '-').replace(' ', '-') if m else src


def main():
    pas = {r['spectrum']: r for r in csv.DictReader(io.open(os.path.join(HERE, 'passport_rev47_full.csv'),
                                                            encoding='utf-8-sig'))}
    geo = {r['спектр']: r for r in csv.DictReader(io.open(os.path.join(
        REPO, 'tools', 'CORPUS', 'data', 'lsrm_spectrum_geometry.csv'), encoding='utf-8-sig'))}
    tab = {}
    for key, p in pas.items():
        if not key.startswith('G1S') or p['group'] not in ('маринелли', 'цилиндр'):
            continue
        g = geo.get(key, {})
        vessel = 'Мар' if '_Mar' in key else ('Дента' if 'Denta' in key else 'Петри')
        tab[key] = dict(ep=key[:5], nuc=p['nuclide'], std=std_id(g.get('источник', '')), vessel=vessel,
                        mat=g.get('вещество', ''), r=float(p['ratio']))
    print('1. ОДИН эталон, сосуд против маринелли той же эпохи (цилиндр/маринелли)')
    by = defaultdict(dict)
    for k, t in tab.items():
        by[(t['ep'], t['std'], t['nuc'])][t['vessel']] = (t['r'], k)
    for (ep, std, nuc), v in sorted(by.items()):
        if 'Мар' in v and len(v) > 1:
            m = v['Мар'][0]
            print('  %s %-10s %-7s Мар %.3f  ' % (ep, std, nuc, m) +
                  '  '.join('%s %.3f (×%.3f)' % (ves, v[ves][0], v[ves][0] / m) for ves in ('Дента', 'Петри')
                            if ves in v))
    print()
    print('2. ОДИН эталон, эпоха G1S24 против G1S16 в маринелли')
    for (ep, std, nuc), v in sorted(by.items()):
        if ep == 'G1S24' and 'Мар' in v and ('G1S16', std, nuc) in by and 'Мар' in by[('G1S16', std, nuc)]:
            a, b = v['Мар'][0], by[('G1S16', std, nuc)]['Мар'][0]
            print('  %-10s %-7s G1S16 %.3f  G1S24 %.3f  ×%.3f' % (std, nuc, b, a, a / b))
    print()
    print('3. Разные нуклиды в ОДНОМ сосуде и эпохе, одна набивка (класс ОИСН): к Ra-226 того же класса')
    cls = defaultdict(dict)
    for k, t in tab.items():
        mat = 'ОИСН-16' if '16' in t['mat'] else ('ОИСН-06' if '06' in t['mat'] else t['mat'])
        cls[(t['ep'], t['vessel'], mat)][t['nuc'] + ' ' + t['std']] = t['r']
    for (ep, ves, mat), v in sorted(cls.items()):
        ra = [x for n, x in v.items() if n.startswith('Ra-226')]
        if not ra:
            continue
        print('  %s %-6s %-8s ' % (ep, ves, mat) + '  '.join('%s %.3f (/Ra %.3f)' % (n, x, x / ra[0])
                                                          for n, x in sorted(v.items())))


if __name__ == '__main__':
    main()
