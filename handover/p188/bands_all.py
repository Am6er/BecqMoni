# -*- coding: utf-8 -*-
r"""П188: Σ(n−m)²/m по полосам энергии по ВСЕМ спектрам понятной части, rev36 против rev37 (кривые --dump-curves).
   python bands_all.py <кривые A> <кривые B> <runs-каталог B>   Разделитель — точка."""
import csv, glob, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
BANDS = [(0, 30), (30, 40), (40, 100), (100, 300), (300, 1000), (1000, 2000), (2000, 1e9)]
def stats(path):
    out = [0.0] * len(BANDS)
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            e = float(r['keV']); a = float(r['fit']); b = float(r['model'])
            for i, (lo, hi) in enumerate(BANDS):
                if lo <= e < hi:
                    out[i] += (a - b) ** 2 / max(b, 1.0)
    return out
A, B, RUNS = sys.argv[1:4]
known = set()
for f in glob.glob(os.path.join(RUNS, '*_runs.csv')):
    for r in csv.DictReader(io.open(f, encoding='utf-8-sig', newline='')):
        if r.get('part') == 'known':
            known.add(r['spectrum'])
tot_a = [0.0] * len(BANDS); tot_b = [0.0] * len(BANDS); better = [0] * len(BANDS); worse = [0] * len(BANDS)
rows = []
for s in sorted(known):
    pa = os.path.join(A, s + '_curves.csv'); pb = os.path.join(B, s + '_curves.csv')
    if not (os.path.exists(pa) and os.path.exists(pb)):
        print(u'нет кривых: %s' % s); continue
    a = stats(pa); b = stats(pb)
    rows.append((s, a, b))
    for i in range(len(BANDS)):
        tot_a[i] += a[i]; tot_b[i] += b[i]
        if b[i] < a[i] - 1e-9: better[i] += 1
        elif b[i] > a[i] + 1e-9: worse[i] += 1
lab = [(u'%g–%g' % (lo, hi) if hi < 1e8 else u'>%g' % lo) for lo, hi in BANDS]
print(u'спектров: %d' % len(rows))
print(u'полоса, кэВ    Σ rev36        Σ rev37        Δ%%      лучше хуже')
for i in range(len(BANDS)):
    print(u'%-12s %14.1f %14.1f %+8.3f%%  %4d %4d' % (lab[i], tot_a[i], tot_b[i], 100 * (tot_b[i] / tot_a[i] - 1) if tot_a[i] else 0, better[i], worse[i]))
print(u'\nбез AS80_Lu176_v2:')
for i in range(len(BANDS)):
    ta = sum(r[1][i] for r in rows if r[0] != 'AS80_Lu176_v2'); tb = sum(r[2][i] for r in rows if r[0] != 'AS80_Lu176_v2')
    print(u'%-12s %14.1f %14.1f %+8.3f%%' % (lab[i], ta, tb, 100 * (tb / ta - 1) if ta else 0))
print(u'\nспектры с содержательной полосой выше 1000 кэВ (Σ rev36 > 500), Δ по полосам 1000–2000 и >2000:')
for s, a, b in sorted(rows, key=lambda r: -(r[1][5] + r[1][6])):
    if a[5] + a[6] > 500:
        print(u'  %-26s 1000–2000 %9.0f → %-9.0f (%+.2f%%)   >2000 %9.0f → %-9.0f (%+.2f%%)' % (
            s, a[5], b[5], 100 * (b[5] / a[5] - 1) if a[5] else 0, a[6], b[6], 100 * (b[6] / a[6] - 1) if a[6] else 0))
