# -*- coding: utf-8 -*-
"""П66 14.09.2026 (копия П51) — состав двух каталогов прогона: у каких спектров СМЕНИЛСЯ СПИСОК компонент
(`*_spline_components.csv`), и что это за компоненты — нуклиды или приборные образы (Xray-*, Esc-*, Ann-511,
pile-up, DE-/SE-…, backscatter…). Доли/z при том же списке не считаются сменой состава (их читает per_spectrum.py).

    python handover/p66-rev23/comp_sets.py tools/pie/out_rev21_full tools/pie/out_p51_full_a [--part=known]
"""
import csv, glob, io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
parts = {r['spectrum']: r['part'] for r in csv.DictReader(io.open(os.path.join(ROOT, 'tools/CORPUS/corpus/parts.csv'), encoding='utf-8-sig', newline=''))}
IMAGE = re.compile(r'^(Xray-|Esc-|Ann-|pile|DE-|SE-|back|Brems|Sum-|Comp|Cont|spline|bg)', re.I)


def comps(d):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_components.csv')):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            out.setdefault(r['spectrum'], {})[r['component']] = r
    return out


A, B = sys.argv[1], sys.argv[2]
part = None
for a in sys.argv[3:]:
    if a.startswith('--part='):
        part = a.split('=', 1)[1]
ca, cb = comps(A), comps(B)
spectra = sorted(set(ca) | set(cb))
if part:
    spectra = [s for s in spectra if parts.get(s) == part]
changed = 0; nuc_changed = 0; total_rows = 0; share_moved = 0
print(u'A=%s B=%s часть=%s: спектров %d' % (os.path.basename(A), os.path.basename(B), part or u'все', len(spectra)))
for s in spectra:
    xa, xb = ca.get(s, {}), cb.get(s, {})
    total_rows += len(xa)
    only_a = sorted(set(xa) - set(xb)); only_b = sorted(set(xb) - set(xa))
    for k in set(xa) & set(xb):
        if xa[k]['share_pct'] != xb[k]['share_pct'] or xa[k]['z'] != xb[k]['z']:
            share_moved += 1
    if only_a or only_b:
        changed += 1
        nuc = [k for k in only_a + only_b if not IMAGE.match(k)]
        if nuc:
            nuc_changed += 1
        def fmt(keys, src):
            return ', '.join(u'%s (%s %%, z %s)' % (k, src[k]['share_pct'], src[k]['z']) for k in keys)
        print(u'  %-24s %-8s %s−[%s] +[%s]' % (s, parts.get(s, '?'), u'НУКЛИД ' if nuc else u'образ  ', fmt(only_a, xa), fmt(only_b, xb)))
print(u'итого: список компонент сменился у %d из %d спектров (из них с нуклидом: %d); строк компонент в A: %d; '
      u'доля/z сдвинулись у %d общих компонент' % (changed, len(spectra), nuc_changed, total_rows, share_moved))
