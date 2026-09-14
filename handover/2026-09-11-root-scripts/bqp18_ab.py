# -*- coding: utf-8 -*-
# П18 11.09.2026: A/B полного корпуса — положение по свету (--anchor-light=1) против lin.
#   python C:\Users\moroz\bqp18_ab.py <плечо A> <плечо B> [--root=C:\Users\moroz\bqp18_out]
# Читает <плечо>\*_spline_runs.csv, *_spline_anchors.csv, *_spline_components.csv и дампы <плечо>_dump\*_curves.csv.
# Полосы — ПРОКСИ невязки Σ(net−model)²/max(model,|net|,1) по дампам (как у П17 bqp17_bands.py; оба плеча одной формулой).
import csv, glob, io, os, sys, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp18_out'
args = [a for a in sys.argv[1:] if not a.startswith('--')]
for a in sys.argv[1:]:
    if a.startswith('--root='):
        root = a[7:]
A, B = args[0], args[1]
MINI = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\mini.csv'
EDGES = [45.0, 100.0, 300.0, 1000.0]
BANDS = ['<45', '45-100', '100-300', '300-1000', '>1000']

def runs(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                if row.get('error'):
                    continue
                r[row['spectrum']] = row
    return r

def anchors(d):
    out = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_anchors.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out.setdefault(row['spectrum'], []).append(row)
    return out

def comps(d):
    out = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_components.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out.setdefault(row['spectrum'], {})[row['component']] = row
    return out

mini = set()
with io.open(MINI, encoding='utf-8-sig') as f:
    for line in f:
        line = line.strip()
        if not line or line.startswith('#') or line.startswith('spectrum'):
            continue
        mini.add(line.split(',')[0].strip())

RA, RB = runs(A), runs(B)
def chi(r, k): return float(r[k]['chi2ndf'])

print('=== %s (A) против %s (B) ===' % (A, B))
for part in ('known', 'unknown'):
    ka = sorted(k for k in RA if RA[k]['part'] == part)
    kb = sorted(k for k in RB if RB[k]['part'] == part)
    keys = sorted(set(ka) & set(kb))
    if not keys:
        continue
    va = [chi(RA, k) for k in keys]; vb = [chi(RB, k) for k in keys]
    print('часть %-8s спектров %d (A %d, B %d): Σχ²/ndf A %.1f B %.1f (Δ %+.1f, %+.1f %%); медиана A %.2f B %.2f' % (
        part, len(keys), len(ka), len(kb), sum(va), sum(vb), sum(vb) - sum(va), 100.0 * (sum(vb) - sum(va)) / sum(va),
        statistics.median(va), statistics.median(vb)))

keys = sorted(k for k in set(RA) & set(RB) if RA[k]['part'] == 'known')
d = sorted(((chi(RB, k) - chi(RA, k), k) for k in keys))
better = [x for x in d if x[0] < -0.005]; worse = [x for x in d if x[0] > 0.005]; same = [x for x in d if abs(x[0]) <= 0.005]
print('\nпонятная часть, %d: лучше %d, хуже %d, ровно %d' % (len(keys), len(better), len(worse), len(same)))
print('лучшие пять (Δ = B − A):')
for dv, k in d[:5]:
    print('  %-22s %6.2f → %6.2f (%+.2f)  опор %s→%s  свет %s' % (k, chi(RA, k), chi(RB, k), dv, RA[k]['anchors_used'], RB[k]['anchors_used'], RB[k].get('anchor_light', '')))
print('худшие пять:')
for dv, k in d[-5:][::-1]:
    print('  %-22s %6.2f → %6.2f (%+.2f)  опор %s→%s  свет %s' % (k, chi(RA, k), chi(RB, k), dv, RA[k]['anchors_used'], RB[k]['anchors_used'], RB[k].get('anchor_light', '')))

def group_report(name, ks):
    if not ks:
        print('%-34s —' % name); return
    va = [chi(RA, k) for k in ks]; vb = [chi(RB, k) for k in ks]
    dd = [chi(RB, k) - chi(RA, k) for k in ks]
    print('%-34s n %3d  Σ A %7.1f  B %7.1f  Δ %+6.1f  мед. A %5.2f B %5.2f  лучше %d хуже %d ровно %d' % (
        name, len(ks), sum(va), sum(vb), sum(vb) - sum(va), statistics.median(va), statistics.median(vb),
        sum(1 for x in dd if x < -0.005), sum(1 for x in dd if x > 0.005), sum(1 for x in dd if abs(x) <= 0.005)))

print('\nгруппы (понятная часть):')
group_report('малая база (в mini.csv)', [k for k in keys if k in mini])
group_report('вне малой базы', [k for k in keys if k not in mini])
for det in sorted(set(RA[k]['det'] for k in keys)):
    group_report('  прибор ' + det, [k for k in keys if RA[k]['det'] == det])
print('по числу опор в A:')
def nanch(k): return int(RA[k]['anchors_used'] or 0)
group_report('  опор 0', [k for k in keys if nanch(k) == 0])
group_report('  опор 1', [k for k in keys if nanch(k) == 1])
group_report('  опор 2', [k for k in keys if nanch(k) == 2])
group_report('  опор ≥ 3', [k for k in keys if nanch(k) >= 3])
for c in sorted(set(RB[k].get('anchor_light', '') for k in keys)):
    group_report('  кривая ' + (c or '(нет)'), [k for k in keys if RB[k].get('anchor_light', '') == c])
group_report('  ASN16 (CsI, своя кривая)', [k for k in keys if k.startswith('ASN16')])
group_report('  RC103 (CsI)', [k for k in keys if k.startswith('RC103')])
print('β=1 в B: %d спектров; кривые: %s' % (
    sum(1 for k in keys if float(RB[k].get('anchor_beta', '0') or 0) != 0.0),
    ', '.join('%s %d' % (c, n) for c, n in sorted({c: sum(1 for k in keys if RB[k].get('anchor_light') == c and float(RB[k].get('anchor_beta', '0') or 0) != 0.0) for c in set(RB[k].get('anchor_light', '') for k in keys) if c}.items()))))

# полосы
def bands(d, k):
    p = os.path.join(root, d + '_dump', k + '_curves.csv')
    if not os.path.exists(p):
        return None
    out = [0.0] * 5
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            e = float(row['keV']); net = float(row['net']); m = float(row['model'])
            if m <= 0.0 and abs(net) <= 0.0:
                continue
            i = 4
            for j, x in enumerate(EDGES):
                if e < x:
                    i = j; break
            out[i] += (net - m) ** 2 / max(m, abs(net), 1.0)
    return out

tot = {A: [0.0] * 5, B: [0.0] * 5}
per = {}
nb = 0
for k in keys:
    ba, bb = bands(A, k), bands(B, k)
    if ba is None or bb is None:
        continue
    nb += 1
    per[k] = (ba, bb)
    for i in range(5):
        tot[A][i] += ba[i]; tot[B][i] += bb[i]
print('\nпрокси-невязка по полосам, Σ по %d понятным (тыс.):' % nb)
print('%8s | ' % 'плечо' + ' | '.join('%9s' % x for x in BANDS) + ' | всего')
for a in (A, B):
    print('%8s | ' % a[:8] + ' | '.join('%9.1f' % (x / 1000.0) for x in tot[a]) + ' | %9.1f' % (sum(tot[a]) / 1000.0))
print('%8s | ' % 'B−A' + ' | '.join('%+9.1f' % ((tot[B][i] - tot[A][i]) / 1000.0) for i in range(5)) + ' | %+9.1f (%+.1f %%)' % ((sum(tot[B]) - sum(tot[A])) / 1000.0, 100.0 * (sum(tot[B]) - sum(tot[A])) / sum(tot[A])))
bd = sorted(((sum(per[k][1]) - sum(per[k][0])) / 1000.0, k) for k in per)
print('крупнейшие по прокси (тыс.): ' + '; '.join('%s %+.1f' % (k, v) for v, k in bd[:3]) + ' | ' + '; '.join('%s %+.1f' % (k, v) for v, k in bd[-3:][::-1]))

# опоры: остатки принятых, промахи выкинутых (skip)
AA, AB = anchors(A), anchors(B)
def acc(an, k):
    return [abs(float(r['shift_kev'])) for r in an.get(k, []) if r['used'] == '1' and r['shift_kev'] not in ('', 'NaN')]
ra = [x for k in keys for x in acc(AA, k)]; rb = [x for k in keys for x in acc(AB, k)]
print('\nостатки ПРИНЯТЫХ опор (|изм − мод|, кэВ): A n %d медиана %.2f среднее %.2f; B n %d медиана %.2f среднее %.2f' % (
    len(ra), statistics.median(ra) if ra else float('nan'), sum(ra) / max(1, len(ra)), len(rb), statistics.median(rb) if rb else float('nan'), sum(rb) / max(1, len(rb))))
byline = {}
for arm, an in ((A, AA), (B, AB)):
    for k in keys:
        for r in an.get(k, []):
            if r['refusal'] == 'skip' and r['shift_kev'] not in ('', 'NaN'):
                byline.setdefault(round(float(r['line_kev']), 1), {}).setdefault(arm, []).append((k, float(r['shift_kev'])))
if byline:
    print('промахи ВЫКИНУТЫХ линий (skip), изм − мод, кэВ: линия: A |ср.| / B |ср.| (n), поспектрово')
    for e in sorted(byline):
        la = byline[e].get(A, []); lb = byline[e].get(B, [])
        print('  %7.1f: A %.2f / B %.2f (n %d/%d)   A: %s   B: %s' % (
            e, sum(abs(x) for _, x in la) / max(1, len(la)), sum(abs(x) for _, x in lb) / max(1, len(lb)), len(la), len(lb),
            ' '.join('%+.2f' % x for _, x in la), ' '.join('%+.2f' % x for _, x in lb)))

# S95 Cd-109
CA, CB = comps(A), comps(B)
for k in ('G1S16_Cd109_P5', 'G1S24_Cd109_P5', 'G1S16_Cd109_P25'):
    if k in CA and k in CB:
        ca = CA[k].get('Cd-109'); cb = CB[k].get('Cd-109')
        print('S95 %s: Cd-109 share A %s B %s; χ² %.2f → %.2f; опор %s→%s' % (
            k, ca['share_pct'] if ca else '—', cb['share_pct'] if cb else '—', chi(RA, k), chi(RB, k), RA[k]['anchors_used'], RB[k]['anchors_used']))
