# -*- coding: utf-8 -*-
# П24 12.09.2026: A/B лестницы умолчаний (A266 / S167 / S166, кривые --force, shield=Pb у диска) — понятная часть 85.
#   python p24_ab.py <база> <плечо> [<плечо> …] [--mini]
# Читает <ROOT>\<плечо>\*_spline_runs.csv / *_spline_components.csv и <HERE>\<плечо>_score.txt (или .log при --mini —
# счёт run_mini.ps1 пишется в лог). По образцу p13_ab.py (П13), без нуля/опор.
import csv, glob, io, os, re, sys, statistics
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\bqp24\tools\pie'
HERE = r'C:\Users\moroz\bqp24\handover\p24-out-rev19'
MINI = r'C:\Users\moroz\bqp24\tools\CORPUS\corpus\mini.csv'
args = [a for a in sys.argv[1:] if not a.startswith('--')]
flags = [a for a in sys.argv[1:] if a.startswith('--')]
ARMS = args
BASE = ARMS[0]
AM241 = ['G1S16_Am241_P5', 'G1S16_Am241_P25', 'G1S24_Am241_P5', 'AS80_Am241']
MIX = ['G1S16_Mix_Denta100', 'G1S16_Mix_Petri', 'G1S16_Mix_Mar']
LU = ['ASN16_Lu176', 'ASN16_Lu176_P0', 'AS80_Lu176', 'AS80_Lu176_v2', 'RC103_Lu176']
WATCH = ['AS80_Th232Medal', 'G1S24_Y88_P5', 'G1S16_Cd109_P25', 'G1S16_Mn54_P25', 'G1S16_Y88_P25', 'G1S16_Co60_P5', 'G1S24_Co60_P5', 'G1S24_Bi207_P5', 'G1S16_Cs137_P5', 'G1S24_Eu152_P5', 'ASN16_Cs137']


def fix(line):
    try:
        return line.encode('cp866').decode('utf-8')
    except Exception:
        return line


def runs(d):
    r = {}
    for p in glob.glob(os.path.join(ROOT, d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                if row.get('error'):
                    continue
                r[row['spectrum']] = row
    return r


def comps(d):
    out = {}
    for p in glob.glob(os.path.join(ROOT, d, '*_spline_components.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out.setdefault(row['spectrum'], {})[row['component']] = row
    return out


def score_line(d, part='known'):
    p = os.path.join(HERE, d + ('.log' if '--mini' in flags else '_score.txt'))
    if not os.path.exists(p):
        return None
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        for line in f:
            line = fix(line)
            if line.lstrip().startswith(u'итого') and (u'часть: ' + part) in line:
                m = re.match(r'итого\s+(\d+)\s+(\d+)%\s+(\d+)\s+(\d+)', line.strip())
                if m:
                    return tuple(int(x) for x in m.groups())
    return None


def misses(d):
    p = os.path.join(HERE, d + ('.log' if '--mini' in flags else '_score.txt'))
    out = []
    if not os.path.exists(p):
        return out
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        for line in f:
            line = fix(line)
            if 'MISS:' in line or u'ФАНТОМ' in line or u'PHANTOM' in line:
                out.append(line.rstrip())
    return out


def refit_line(d):
    p = os.path.join(HERE, d + '.log')
    if not os.path.exists(p):
        return u'—'
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        for line in f:
            line = fix(line)
            if u'отсев по значимости' in line:
                return line.strip()
    return u'—'


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

print(u'\n%-22s %8s %7s %8s %7s %5s %7s %5s %5s %9s' % (u'плечо', u'Σχ²/ndf', u'Δ', u'Δ%', u'мед.', u'n', u'recall', u'фант', u'подав', u'л/х/р'))
for a in ARMS:
    v = [chi(a, k) for k in keys]
    b = [chi(BASE, k) for k in keys]
    d = [x - y for x, y in zip(v, b)]
    sc = score_line(a)
    lhr = '%d/%d/%d' % (sum(1 for x in d if x < -0.005), sum(1 for x in d if x > 0.005), sum(1 for x in d if abs(x) <= 0.005))
    print(u'%-22s %8.1f %+7.1f %+7.1f%% %7.2f %5d %6s%% %5s %5s %9s' % (
        a, sum(v), sum(v) - sum(b), 100.0 * (sum(v) - sum(b)) / sum(b), statistics.median(v), len(v),
        sc[1] if sc else '?', sc[2] if sc else '?', sc[3] if sc else '?', lhr if a != BASE else u'—'))
for a in ARMS:
    unk = sorted(k for k in R[a] if R[a][k]['part'] == 'unknown')
    sc = score_line(a, 'unknown')
    if unk:
        print(u'  непонятная %s: разобрано %d, Σχ²/ndf %.1f, медиана %.2f; recall %s%% / фантомов %s / подавлен %s (спектров %s)' % (
            a, len(unk), sum(chi(a, k) for k in unk), statistics.median([chi(a, k) for k in unk]), sc[1] if sc else '?', sc[2] if sc else '?', sc[3] if sc else '?', sc[0] if sc else '?'))
for a in ARMS:
    m = misses(a)
    if m:
        print(u'  промахи/фантомы %s:' % a)
        for line in m:
            print(u'    ' + line)
for a in ARMS:
    print(u'  %s: %s' % (a, refit_line(a)))
for a in ARMS:
    v = [float(R[a][k]['model_residual_pct']) for k in keys if R[a][k].get('model_residual_pct')]
    print(u'  невязка модели %s: медиана %.1f %%' % (a, statistics.median(v)))


def group_table(name, ks):
    if not ks:
        return
    row = u'%-30s n %3d' % (name, len(ks))
    for a in ARMS:
        v = [chi(a, k) for k in ks]
        if a == BASE:
            row += u' | %7.2f' % sum(v)
        else:
            d = [chi(a, k) - chi(BASE, k) for k in ks]
            row += u' | %7.2f (%+6.2f, %+5.1f%%) %d/%d/%d' % (sum(v), sum(d), 100.0 * sum(d) / sum(chi(BASE, k) for k in ks),
                                                        sum(1 for x in d if x < -0.005), sum(1 for x in d if x > 0.005), sum(1 for x in d if abs(x) <= 0.005))
    print(row)


print(u'\nгруппы (понятная; Σχ²/ndf, в скобках Δ к %s, лучше/хуже/ровно):' % BASE)
group_table(u'малая база (mini.csv)', [k for k in keys if k in mini])
group_table(u'вне малой базы', [k for k in keys if k not in mini])
for det in sorted(set(R[BASE][k]['det'] for k in keys)):
    group_table(u'  прибор ' + det, [k for k in keys if R[BASE][k]['det'] == det])
group_table(u'CsI (ASN16 + RC103)', [k for k in keys if k.startswith('ASN16') or k.startswith('RC103')])
group_table(u'NaI (G1S16 + G1S24 + AS80)', [k for k in keys if not (k.startswith('ASN16') or k.startswith('RC103'))])
group_table(u'Am-241 ×4', [k for k in keys if k in AM241])
group_table(u'смеси G1S16_Mix ×3', [k for k in keys if k in MIX])
group_table(u'Lu-176 ×5', [k for k in keys if k in LU])
group_table(u'диск AS80_Th232Medal', [k for k in keys if k == 'AS80_Th232Medal'])
group_table(u'без диска', [k for k in keys if k != 'AS80_Th232Medal'])

print(u'\nпод надзором (χ²/ndf по плечам):')
for k in WATCH + AM241 + MIX + LU:
    if all(k in R[a] for a in ARMS):
        print(u'  %-20s ' % k + '  '.join('%s %7.3f' % (a[8:], chi(a, k)) for a in ARMS) + u'  Δ(посл.) %+.3f' % (chi(ARMS[-1], k) - chi(BASE, k)))

for a in ARMS[1:]:
    d = sorted(((chi(a, k) - chi(BASE, k), k) for k in keys))
    print(u'\n%s − %s: лучшие пять: ' % (a, BASE) + '; '.join(u'%s %.2f→%.2f (%+.2f)' % (k, chi(BASE, k), chi(a, k), dv) for dv, k in d[:5]))
    print(u'      худшие пять: ' + '; '.join(u'%s %.2f→%.2f (%+.2f)' % (k, chi(BASE, k), chi(a, k), dv) for dv, k in d[-5:][::-1]))
    print(u'      |Δ| ≥ 0.5: ' + ('; '.join(u'%s %+.2f' % (k, dv) for dv, k in d if abs(dv) >= 0.5) or u'—'))
    print(u'      |Δ| ≥ 0.1: ' + ('; '.join(u'%s %+.2f' % (k, dv) for dv, k in d if abs(dv) >= 0.1) or u'—'))
    print(u'      хуже на ≥ +0.05: %d, лучше на ≤ −0.05: %d' % (sum(1 for dv, k in d if dv >= 0.05), sum(1 for dv, k in d if dv <= -0.05)))

C = {a: comps(a) for a in ARMS}
for k in ('G1S16_Cd109_P5', 'G1S24_Cd109_P5', 'G1S16_Cd109_P25'):
    if all(k in C[a] for a in ARMS):
        print(u'S95 %s: Cd-109 доля ' % k + ', '.join('%s %s' % (a[8:], C[a][k]['Cd-109']['share_pct'] if 'Cd-109' in C[a][k] else u'—') for a in ARMS)
              + u'; подавлен: ' + ', '.join('%s %s' % (a[8:], sum(1 for c in C[a][k].values() if c.get('kind') == 'suppressed')) for a in ARMS))
print(u'\nсостав смесей G1S16_Mix и диска (доля % / z по плечам):')
for k, nucs in [(m, ('Am-241', 'Cs-137', 'Eu-152', 'Ti-44')) for m in MIX] + [('AS80_Th232Medal', ('Ac-228', 'Tl-208', 'Pb-212', 'Bi-212', 'Xray-Pb', 'Xray-Th', 'Th-228'))]:
    for nuc in nucs:
        print(u'  %-20s %-7s ' % (k, nuc) + '  '.join('%s %s' % (a[8:], ('%s / %s' % (C[a][k][nuc]['share_pct'], C[a][k][nuc]['z'])) if nuc in C[a].get(k, {}) else u'НЕТ') for a in ARMS))
