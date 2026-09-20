# -*- coding: utf-8 -*-
"""Мера плеча ВЫКЛ → ВКЛ по спектрам (П83, AMBER42).

    python ab_report.py <OFF> <ON> [--part=known] [--share-thr=0.5]

Печатает markdown: (1) χ²/ndf ВЫКЛ → ВКЛ → Δ % для всех спектров с Δ ≠ 0, по |Δ|;
(2) состав (число компонентов и список) — где изменился; (3) share_pct — где сдвиг
больше порога п.п.; (4) спектры, у которых ВСЕ четыре таблицы побитово (без ms/cpu_ms).
"""
import io, os, sys, glob, csv
from collections import defaultdict

TIME = {'ms', 'cpu_ms'}

def rows_of(d, table):
    out = {}
    for f in sorted(glob.glob(os.path.join(d, '*_spline_%s.csv' % table))):
        with io.open(f, encoding='utf-8-sig', newline='') as fh:
            r = list(csv.reader(fh))
        if not r: continue
        head = r[0]
        for row in r[1:]:
            rec = dict(zip(head, row))
            out.setdefault(rec['spectrum'], []).append(rec)
    return out

def main():
    a, b = sys.argv[1], sys.argv[2]
    part = 'known'; thr = 0.5
    for arg in sys.argv[3:]:
        if arg.startswith('--part='): part = arg[7:]
        if arg.startswith('--share-thr='): thr = float(arg[12:])
    tabs = {}
    for t in ('runs', 'components', 'limits', 'anchors'):
        tabs[t] = (rows_of(a, t), rows_of(b, t))
    runs_a, runs_b = tabs['runs']
    specs = sorted(s for s in runs_a if runs_a[s][0].get('part') == part)

    # (1) χ²/ndf
    chi = []
    for s in specs:
        ra = runs_a[s][0]; rb = runs_b.get(s, [{}])[0]
        try:
            x = float(ra['chi2ndf']); y = float(rb['chi2ndf'])
        except (KeyError, ValueError):
            continue
        chi.append((s, x, y, (y - x) / x * 100.0 if x else 0.0))
    changed = [c for c in chi if c[1] != c[2]]
    changed.sort(key=lambda c: -abs(c[3]))
    print('### χ²/ndf ВЫКЛ → ВКЛ (часть %s, %d спектров; изменилось у %d)' % (part, len(chi), len(changed)))
    print()
    print('| спектр | χ²/ndf ВЫКЛ | ВКЛ | Δ % | невязка ВЫКЛ → ВКЛ, % | усиление ВЫКЛ → ВКЛ |')
    print('|---|---|---|---|---|---|')
    for s, x, y, d in changed:
        ra = runs_a[s][0]; rb = runs_b[s][0]
        print('| %s | %.3f | %.3f | %+.2f | %s → %s | %s → %s |' % (s, x, y, d,
              ra.get('model_residual_pct', ''), rb.get('model_residual_pct', ''), ra.get('gain', ''), rb.get('gain', '')))
    sx = sum(c[1] for c in chi); sy = sum(c[2] for c in chi)
    import statistics
    print()
    print('Σχ²/ndf %.1f → %.1f (%+.2f %%); медиана %.2f → %.2f; улучшилось %d, ухудшилось %d, |Δ| ≤ 0.15 %% у %d из %d изменившихся' % (
        sx, sy, (sy - sx) / sx * 100, statistics.median(c[1] for c in chi), statistics.median(c[2] for c in chi),
        sum(1 for c in changed if c[3] < 0), sum(1 for c in changed if c[3] > 0),
        sum(1 for c in changed if abs(c[3]) <= 0.15), len(changed)))
    print()

    # (2) состав
    ca, cb = tabs['components']
    print('### Состав (число компонентов, список)')
    print()
    comp_changed = 0
    for s in specs:
        la = sorted(r['component'] for r in ca.get(s, [])); lb = sorted(r['component'] for r in cb.get(s, []))
        if la != lb:
            comp_changed += 1
            print('* %s: %d → %d; только ВЫКЛ %s; только ВКЛ %s' % (s, len(la), len(lb),
                  sorted(set(la) - set(lb)), sorted(set(lb) - set(la))))
    print('состав изменился у %d из %d' % (comp_changed, len(specs)))
    print()

    # (3) share_pct
    print('### share_pct: сдвиг больше %.1f п.п.' % thr)
    print()
    print('| спектр | компонент | share ВЫКЛ | ВКЛ | Δ п.п. | z ВЫКЛ → ВКЛ |')
    print('|---|---|---|---|---|---|')
    n_share = 0; n_any = 0
    for s in specs:
        da = {r['component']: r for r in ca.get(s, [])}; db = {r['component']: r for r in cb.get(s, [])}
        for c in da:
            if c not in db: continue
            try:
                x = float(da[c]['share_pct']); y = float(db[c]['share_pct'])
            except ValueError:
                continue
            if x != y: n_any += 1
            if abs(y - x) > thr:
                n_share += 1
                print('| %s | %s | %.3f | %.3f | %+.3f | %s → %s |' % (s, c, x, y, y - x, da[c].get('z'), db[c].get('z')))
    print()
    print('строк с share_pct ≠: %d; больше %.1f п.п.: %d' % (n_any, thr, n_share))
    print()

    # (4) побитово по четырём таблицам
    def sig(rows):
        out = []
        for r in rows:
            out.append(tuple((k, v) for k, v in sorted(r.items()) if k not in TIME))
        return sorted(out)
    same = []; diff = []
    for s in specs:
        ok = True
        for t in tabs:
            ta, tb = tabs[t]
            if sig(ta.get(s, [])) != sig(tb.get(s, [])):
                ok = False; break
        (same if ok else diff).append(s)
    print('### Побитово по четырём таблицам (без ms/cpu_ms): %d из %d' % (len(same), len(specs)))
    print()
    print(', '.join(same))
    print()
    print('### Изменились (%d): ' % len(diff))
    print()
    print(', '.join(diff))
    return 0

if __name__ == '__main__':
    sys.exit(main())
