# -*- coding: utf-8 -*-
# П17: A/B малой базы по плечам a/b — chi2/ndf по спектрам (runs.csv), худшие/лучшие,
# и невязка по полосам кэВ из дампов *_curves.csv (ПРОКСИ: Σ(net−model)²/max(model,|net|,1),
# не веса решателя — патча П9 с _chi.csv в дереве нет; обе плечи одной формулой).
import csv, glob, io, os, sys, statistics
sys.stdout.reconfigure(encoding='utf-8')
root = r'C:\Users\moroz\bqp17_out'
arms = sys.argv[1:] or ['a', 'b']
EDGES = [45.0, 100.0, 300.0, 1000.0]
BANDS = ['<45', '45-100', '100-300', '300-1000', '>1000']

def runs(d):
    r = {}
    for p in glob.glob(os.path.join(root, d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                if row.get('part') == 'known':
                    r[row['spectrum']] = row
    return r

R = {a: runs(a) for a in arms}
keys = sorted(set.intersection(*[set(R[a]) for a in arms]))
print('понятных спектров в обоих плечах: %d' % len(keys))
for a in arms:
    vals = [float(R[a][k]['chi2ndf']) for k in keys]
    print('%s: Σ chi2/ndf %.1f, медиана %.2f, opor принято Σ %d' % (a, sum(vals), statistics.median(vals),
          sum(int(R[a][k]['anchors_used'] or 0) for k in keys)))
if len(arms) == 2:
    a, b = arms
    d = sorted(((float(R[b][k]['chi2ndf']) - float(R[a][k]['chi2ndf']), k) for k in keys))
    print('\nхуже %s (лучшие пять по Δ = %s − %s):' % (a, b, a))
    for dv, k in d[:5]:
        print('  %-20s %.2f → %.2f (%+.2f)' % (k, float(R[a][k]['chi2ndf']), float(R[b][k]['chi2ndf']), dv))
    print('хуже всего (Δ > 0):')
    for dv, k in d[-5:][::-1]:
        print('  %-20s %.2f → %.2f (%+.2f)' % (k, float(R[a][k]['chi2ndf']), float(R[b][k]['chi2ndf']), dv))
    print('лучше: %d, хуже: %d, ровно: %d' % (sum(1 for dv, _ in d if dv < -0.005), sum(1 for dv, _ in d if dv > 0.005), sum(1 for dv, _ in d if abs(dv) <= 0.005)))

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

print('\nпрокси-невязка по полосам, Σ по спектрам (в тысячах):')
tot = {a: [0.0] * 5 for a in arms}
per = {}
for k in keys:
    for a in arms:
        b = bands(a, k)
        if b is None:
            continue
        per[(a, k)] = b
        for i in range(5):
            tot[a][i] += b[i]
print('%6s | ' % 'плечо' + ' | '.join('%9s' % x for x in BANDS) + ' | всего')
for a in arms:
    print('%6s | ' % a + ' | '.join('%9.1f' % (x / 1000.0) for x in tot[a]) + ' | %9.1f' % (sum(tot[a]) / 1000.0))
if len(arms) == 2:
    a, b = arms
    print('%6s | ' % 'b−a' + ' | '.join('%+9.1f' % ((tot[b][i] - tot[a][i]) / 1000.0) for i in range(5)) + ' | %+9.1f' % ((sum(tot[b]) - sum(tot[a])) / 1000.0))
    print('\nполоса <45 и 45–100 по спектрам с рентгеном (b−a, тыс.):')
    for k in ['G1S16_Cs137_P5', 'G1S24_Cs137_P5', 'G1S16_Ba133_P5', 'G1S24_Ba133_P5', 'G1S16_Ba133_P25', 'G1S16_Eu152_P5', 'G1S24_Eu152_P5', 'G1S16_Eu152_P25', 'G1S16_Cd109_P5', 'G1S24_Cd109_P5', 'G1S16_Am241_P5', 'G1S24_Am241_P5', 'G1S16_Co57_P5', 'AS80_Cs137_0cm', 'ASN16_Cs137', 'ASN16_Lu176']:
        if (a, k) in per and (b, k) in per:
            print('  %-18s <45: %+7.2f (%.2f→%.2f)   45–100: %+7.2f (%.2f→%.2f)' % (
                k, (per[(b, k)][0] - per[(a, k)][0]) / 1000.0, per[(a, k)][0] / 1000.0, per[(b, k)][0] / 1000.0,
                (per[(b, k)][1] - per[(a, k)][1]) / 1000.0, per[(a, k)][1] / 1000.0, per[(b, k)][1] / 1000.0))
