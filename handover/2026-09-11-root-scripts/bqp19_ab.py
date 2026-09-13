# -*- coding: utf-8 -*-
# П19 12.09.2026: A/B форм световой координаты — несколько плеч против первого (lin).
#   python C:\Users\moroz\bqp19_ab.py lin bin line peak [anchor] [--root=C:\Users\moroz\bqp19_out] [--bands] [--loo]
# Читает <плечо>\*_spline_runs.csv, *_spline_anchors.csv, *_spline_components.csv, <плечо>.log (recall/фантомы/подавлен)
# и дампы <плечо>_dump\*_curves.csv (полосы — ПРОКСИ-невязка Σ(net−model)²/max(model,|net|,1), формула П17/П18).
import csv, glob, io, os, re, sys, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp19_out'
args = [a for a in sys.argv[1:] if not a.startswith('--')]
flags = [a for a in sys.argv[1:] if a.startswith('--')]
for a in flags:
    if a.startswith('--root='):
        root = a[7:]
ARMS = args
BASE = ARMS[0]
MINI = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\mini.csv'
EDGES = [45.0, 100.0, 300.0, 1000.0]
BANDS = ['<45', '45-100', '100-300', '300-1000', '>1000']
WORST18 = ['G1S24_Y88_P5', 'ASN16_Lu176', 'G1S24_Cs137_P5', 'AS80_Lu176_v2', 'G1S16_Co60_P5']
WATCH = ['G1S16_Th228_P5', 'G1S16_Cs137_P5', 'G1S24_Eu152_P5', 'G1S24_Th228_P5', 'G1S16_Mix_Mar']


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


def score_line(d):
    """Строка «итого … часть: known» из лога плеча: спектров, recall, фантомов, подавлен."""
    for p in (os.path.join(root, d + '_score.txt'), os.path.join(root, d + '.log')):
        if not os.path.exists(p):
            continue
        with io.open(p, encoding='utf-8-sig', errors='replace') as f:
            for line in f:
                if line.lstrip().startswith(u'итого') and u'часть: known' in line:
                    m = re.match(r'итого\s+(\d+)\s+(\d+)%\s+(\d+)\s+(\d+)', line.strip())
                    if m:
                        return tuple(int(x) for x in m.groups())
    return None


mini = set()
with io.open(MINI, encoding='utf-8-sig') as f:
    for line in f:
        line = line.strip()
        if not line or line.startswith('#') or line.startswith('spectrum'):
            continue
        mini.add(line.split(',')[0].strip())

R = {a: runs(a) for a in ARMS}
def chi(a, k): return float(R[a][k]['chi2ndf'])
keys = sorted(k for k in set.intersection(*(set(R[a]) for a in ARMS)) if R[BASE][k]['part'] == 'known')
print(u'=== плечи %s; понятная часть, общих спектров %d (в плечах: %s) ===' % (
    ', '.join(ARMS), len(keys), ', '.join('%s %d' % (a, sum(1 for k in R[a] if R[a][k]['part'] == 'known')) for a in ARMS)))

print(u'\n%-10s %8s %7s %8s %7s %6s %8s %6s %6s %6s %8s' % (u'плечо', u'Σχ²/ndf', u'Δ', u'Δ%', u'мед.', u'n', u'recall', u'фант', u'подав', u'β=1', u'л/х/р'))
for a in ARMS:
    v = [chi(a, k) for k in keys]
    b = [chi(BASE, k) for k in keys]
    d = [x - y for x, y in zip(v, b)]
    sc = score_line(a)
    beta = sum(1 for k in keys if float(R[a][k].get('anchor_beta', '0') or 0) != 0.0)
    lhr = '%d/%d/%d' % (sum(1 for x in d if x < -0.005), sum(1 for x in d if x > 0.005), sum(1 for x in d if abs(x) <= 0.005))
    print(u'%-10s %8.1f %+7.1f %+7.1f%% %7.2f %6d %7s%% %6s %6s %6d %8s' % (
        a, sum(v), sum(v) - sum(b), 100.0 * (sum(v) - sum(b)) / sum(b), statistics.median(v), len(v),
        sc[1] if sc else '?', sc[2] if sc else '?', sc[3] if sc else '?', beta, lhr if a != BASE else u'—'))
unk = {a: sorted(k for k in R[a] if R[a][k]['part'] == 'unknown') for a in ARMS}
for a in ARMS:
    if unk[a]:
        print(u'  непонятная часть %s: %d спектров, Σχ²/ndf %.1f' % (a, len(unk[a]), sum(chi(a, k) for k in unk[a])))


def group_table(name, ks):
    if not ks:
        return
    row = u'%-30s n %3d' % (name, len(ks))
    for a in ARMS:
        v = [chi(a, k) for k in ks]
        if a == BASE:
            row += u' | %s %7.1f мед %5.2f' % (a, sum(v), statistics.median(v))
        else:
            d = [chi(a, k) - chi(BASE, k) for k in ks]
            row += u' | %s %7.1f (%+6.1f) %d/%d/%d' % (a, sum(v), sum(d), sum(1 for x in d if x < -0.005), sum(1 for x in d if x > 0.005), sum(1 for x in d if abs(x) <= 0.005))
    print(row)


def nanch(k): return int(R[BASE][k]['anchors_used'] or 0)
print(u'\nгруппы (понятная часть; Σχ²/ndf, в скобках Δ к %s, лучше/хуже/ровно):' % BASE)
group_table(u'малая база (mini.csv)', [k for k in keys if k in mini])
group_table(u'вне малой базы', [k for k in keys if k not in mini])
group_table(u'опор 0 (в %s)' % BASE, [k for k in keys if nanch(k) == 0])
group_table(u'опор 1', [k for k in keys if nanch(k) == 1])
group_table(u'опор 2', [k for k in keys if nanch(k) == 2])
group_table(u'опор ≤ 2', [k for k in keys if 1 <= nanch(k) <= 2])
group_table(u'опор ≥ 3', [k for k in keys if nanch(k) >= 3])
for det in sorted(set(R[BASE][k]['det'] for k in keys)):
    group_table(u'  прибор ' + det, [k for k in keys if R[BASE][k]['det'] == det])
group_table(u'CsI (ASN16 + RC103)', [k for k in keys if k.startswith('ASN16') or k.startswith('RC103')])
for a in ARMS[1:]:
    for c in sorted(set(R[a][k].get('anchor_light', '') for k in keys)):
        if c:
            group_table(u'  кривая %s (по %s)' % (c, a), [k for k in keys if R[a][k].get('anchor_light', '') == c])
    break

print(u'\nпятёрка худших П18 и спектры под надзором (χ²/ndf по плечам; опор по %s):' % BASE)
for k in WORST18 + WATCH:
    if k in R[BASE]:
        print(u'  %-20s ' % k + '  '.join('%s %6.2f' % (a, chi(a, k)) for a in ARMS if k in R[a]) + u'  опор %s' % R[BASE][k]['anchors_used'])

for a in ARMS[1:]:
    d = sorted(((chi(a, k) - chi(BASE, k), k) for k in keys))
    print(u'\n%s − %s: лучшие: ' % (a, BASE) + '; '.join(u'%s %.2f→%.2f (%+.2f, оп. %s)' % (k, chi(BASE, k), chi(a, k), dv, R[BASE][k]['anchors_used']) for dv, k in d[:5]))
    print(u'      худшие: ' + '; '.join(u'%s %.2f→%.2f (%+.2f, оп. %s)' % (k, chi(BASE, k), chi(a, k), dv, R[BASE][k]['anchors_used']) for dv, k in d[-5:][::-1]))

# S95 Cd-109
C = {a: comps(a) for a in ARMS}
for k in ('G1S16_Cd109_P5', 'G1S24_Cd109_P5', 'G1S16_Cd109_P25'):
    if all(k in C[a] for a in ARMS):
        print(u'S95 %s: Cd-109 доля ' % k + ', '.join('%s %s' % (a, C[a][k]['Cd-109']['share_pct'] if 'Cd-109' in C[a][k] else u'—') for a in ARMS)
              + u'; подавлен: ' + ', '.join('%s %s' % (a, sum(1 for c in C[a][k].values() if c.get('kind') == 'suppressed')) for a in ARMS))

# опоры: остатки принятых
AN = {a: anchors(a) for a in ARMS}
def acc(a, k):
    return [abs(float(r['shift_kev'])) for r in AN[a].get(k, []) if r['used'] == '1' and r['shift_kev'] not in ('', 'NaN')]
print(u'\nостатки ПРИНЯТЫХ опор |изм − мод|, кэВ:')
for a in ARMS:
    v = [x for k in keys for x in acc(a, k)]
    print(u'  %-8s n %3d медиана %.2f среднее %.2f' % (a, len(v), statistics.median(v) if v else float('nan'), sum(v) / max(1, len(v))))

if '--loo' in flags:
    print(u'\nвыброс узла: остаток ВЫЧЕРКНУТОЙ линии (refusal=skip) на итоговой шкале, |ср.| кэВ по спектрам с долей пика ≥ 0.5:')
    byline = {}
    for a in ARMS:
        for k in keys:
            for r in AN[a].get(k, []):
                if r['refusal'] == 'skip' and r['shift_kev'] not in ('', 'NaN') and float(r['peak_share'] or 0) >= 0.5:
                    byline.setdefault(round(float(r['line_kev']), 1), {}).setdefault(a, []).append((k, float(r['shift_kev'])))
    for e in sorted(byline):
        print(u'  %7.1f: ' % e + ' | '.join('%s %.2f (n %d)' % (a, sum(abs(x) for _, x in byline[e].get(a, [])) / max(1, len(byline[e].get(a, []))), len(byline[e].get(a, []))) for a in ARMS))
        for a in ARMS:
            print(u'          %-8s %s' % (a, ' '.join('%s %+.2f' % (k, x) for k, x in byline[e].get(a, []))))

if '--bands' in flags:
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
    tot = {a: [0.0] * 5 for a in ARMS}
    per = {a: {} for a in ARMS}
    nb = 0
    for k in keys:
        bb = {a: bands(a, k) for a in ARMS}
        if any(bb[a] is None for a in ARMS):
            continue
        nb += 1
        for a in ARMS:
            per[a][k] = bb[a]
            for i in range(5):
                tot[a][i] += bb[a][i]
    print(u'\nпрокси-невязка по полосам, Σ по %d понятным (тыс.):' % nb)
    print(u'%10s | ' % u'плечо' + ' | '.join('%9s' % x for x in BANDS) + u' | всего')
    for a in ARMS:
        print(u'%10s | ' % a + ' | '.join('%9.1f' % (x / 1000.0) for x in tot[a]) + ' | %9.1f' % (sum(tot[a]) / 1000.0))
        if a != BASE:
            print(u'%10s | ' % (u'−' + BASE) + ' | '.join('%+9.1f' % ((tot[a][i] - tot[BASE][i]) / 1000.0) for i in range(5)) + ' | %+9.1f (%+.1f %%)' % ((sum(tot[a]) - sum(tot[BASE])) / 1000.0, 100.0 * (sum(tot[a]) - sum(tot[BASE])) / sum(tot[BASE])))
    for a in ARMS[1:]:
        bd = sorted(((sum(per[a][k]) - sum(per[BASE][k])) / 1000.0, k) for k in per[a])
        print(u'  %s крупнейшие (тыс.): ' % a + '; '.join('%s %+.1f' % (k, v) for v, k in bd[:3]) + ' | ' + '; '.join('%s %+.1f' % (k, v) for v, k in bd[-3:][::-1]))
