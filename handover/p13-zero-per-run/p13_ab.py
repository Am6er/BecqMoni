# -*- coding: utf-8 -*-
# П13 12.09.2026: A/B нуля шкалы образа (S169) — нуль ПО СЪЁМКЕ (adc) против calib и adc-fixed (форма П8); копия p12_ab.py П12
#   python p13_ab.py out_p13_calib out_p13_adc out_p13_fixed [--loo] [--score-from-log]
#   для прогонов -List (score в логе) — --score-from-log; читает <here>\<плечо>.log
# Читает <root>\<плечо>\*_spline_runs.csv, *_spline_anchors.csv, *_spline_components.csv и <here>\<плечо>_score.txt
# (recall/фантомы/подавлен из score.py --part=known --members). По образцу bqp19_ab.py (П19), без полос дампов.
import csv, glob, io, os, re, sys, statistics
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\pie'
HERE = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p13-zero-per-run'
MINI = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\mini.csv'
args = [a for a in sys.argv[1:] if not a.startswith('--')]
flags = [a for a in sys.argv[1:] if a.startswith('--')]
ARMS = args
BASE = ARMS[0]
AM241 = ['G1S16_Am241_P5', 'G1S16_Am241_P25', 'G1S24_Am241_P5', 'AS80_Am241']
MIX = ['G1S16_Mix_Denta100', 'G1S16_Mix_Petri', 'G1S16_Mix_Mar']
WATCH = ['G1S24_Y88_P5', 'ASN16_Lu176', 'ASN16_Lu176_P0', 'AS80_Lu176', 'AS80_Lu176_v2', 'G1S24_Cs137_P5', 'G1S16_Co60_P5',
         'G1S24_Co60_P5', 'G1S16_Th228_P5', 'G1S16_Cs137_P5', 'G1S24_Eu152_P5', 'G1S24_Th228_P5', 'RC103_Cs137', 'RC103_Lu176']


def runs(d):
    r = {}
    for p in glob.glob(os.path.join(ROOT, d, '*_spline_runs.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                if row.get('error'):
                    continue
                r[row['spectrum']] = row
    return r


def anchors(d):
    out = {}
    for p in glob.glob(os.path.join(ROOT, d, '*_spline_anchors.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out.setdefault(row['spectrum'], []).append(row)
    return out


def comps(d):
    out = {}
    for p in glob.glob(os.path.join(ROOT, d, '*_spline_components.csv')):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                out.setdefault(row['spectrum'], {})[row['component']] = row
    return out


def score_line(d, part='known'):
    p = os.path.join(HERE, d + ('.log' if '--score-from-log' in flags else '_score.txt'))
    if not os.path.exists(p):
        return None
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        for line in f:
            if line.lstrip().startswith(u'итого') and (u'часть: ' + part) in line:
                m = re.match(r'итого\s+(\d+)\s+(\d+)%\s+(\d+)\s+(\d+)', line.strip())
                if m:
                    return tuple(int(x) for x in m.groups())
    return None


def misses(d):
    p = os.path.join(HERE, d + ('.log' if '--score-from-log' in flags else '_score.txt'))
    out = []
    if not os.path.exists(p):
        return out
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        for line in f:
            if 'MISS:' in line or u'ФАНТОМ' in line or u'PHANTOM' in line:
                out.append(line.rstrip())
    return out


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

print(u'\n%-16s %8s %7s %8s %7s %6s %8s %6s %6s %6s %8s %8s' % (u'плечо', u'Σχ²/ndf', u'Δ', u'Δ%', u'мед.', u'n', u'recall', u'фант', u'подав', u'β=1', u'л/х/р', u'adc'))
for a in ARMS:
    v = [chi(a, k) for k in keys]
    b = [chi(BASE, k) for k in keys]
    d = [x - y for x, y in zip(v, b)]
    sc = score_line(a)
    beta = sum(1 for k in keys if float(R[a][k].get('anchor_beta', '0') or 0) != 0.0)
    adc = sum(1 for k in keys if u'нуль adc' in (R[a][k].get('anchor_note') or ''))
    run = sum(1 for k in keys if u'(по съёмке:' in (R[a][k].get('anchor_note') or ''))
    lhr = '%d/%d/%d' % (sum(1 for x in d if x < -0.005), sum(1 for x in d if x > 0.005), sum(1 for x in d if abs(x) <= 0.005))
    print(u'%-16s %8.1f %+7.1f %+7.1f%% %7.2f %6d %7s%% %6s %6s %6d %8s %8s' % (
        a, sum(v), sum(v) - sum(b), 100.0 * (sum(v) - sum(b)) / sum(b), statistics.median(v), len(v),
        sc[1] if sc else '?', sc[2] if sc else '?', sc[3] if sc else '?', beta, lhr if a != BASE else u'—', '%d (съёмка %d)' % (adc, run) if adc else '0'))
for a in ARMS:
    unk = sorted(k for k in R[a] if R[a][k]['part'] == 'unknown')
    sc = score_line(a, 'unknown')
    if unk:
        print(u'  непонятная часть %s: разобрано %d, Σχ²/ndf %.1f, медиана %.2f; recall %s%% / фантомов %s / подавлен %s (спектров %s)' % (
            a, len(unk), sum(chi(a, k) for k in unk), statistics.median([chi(a, k) for k in unk]), sc[1] if sc else '?', sc[2] if sc else '?', sc[3] if sc else '?', sc[0] if sc else '?'))
for a in ARMS:
    m = misses(a)
    if m:
        print(u'  промахи/фантомы %s:' % a)
        for line in m:
            print(u'    ' + line)

# невязка модели
for a in ARMS:
    v = [float(R[a][k]['model_residual_pct']) for k in keys if R[a][k].get('model_residual_pct')]
    print(u'  невязка модели %s: медиана %.1f %%' % (a, statistics.median(v)))


def group_table(name, ks):
    if not ks:
        return
    row = u'%-34s n %3d' % (name, len(ks))
    for a in ARMS:
        v = [chi(a, k) for k in ks]
        if a == BASE:
            row += u' | %s %7.1f мед %5.2f' % (a[-5:], sum(v), statistics.median(v))
        else:
            d = [chi(a, k) - chi(BASE, k) for k in ks]
            row += u' | %s %7.1f (%+6.1f, %+5.1f%%) мед %5.2f  %d/%d/%d' % (a[-3:], sum(v), sum(d), 100.0 * sum(d) / sum(chi(BASE, k) for k in ks), statistics.median(v),
                                                                 sum(1 for x in d if x < -0.005), sum(1 for x in d if x > 0.005), sum(1 for x in d if abs(x) <= 0.005))
    print(row)


def nanch(k): return int(R[BASE][k]['anchors_used'] or 0)
def zsrc(a, k):
    n = R[a][k].get('anchor_note') or ''
    return u'съёмка' if u'(по съёмке:' in n else (u'прибор' if u'(по прибору:' in n else u'—')
print(u'\nгруппы (понятная часть; Σχ²/ndf, в скобках Δ к %s, лучше/хуже/ровно):' % BASE)
group_table(u'малая база (mini.csv)', [k for k in keys if k in mini])
group_table(u'вне малой базы', [k for k in keys if k not in mini])
for a in ARMS[1:]:
    group_table(u'нуль по съёмке (в %s)' % a, [k for k in keys if zsrc(a, k) == u'съёмка'])
    group_table(u'нуль по прибору (в %s)' % a, [k for k in keys if zsrc(a, k) == u'прибор'])
group_table(u'опор 0 (в %s)' % BASE, [k for k in keys if nanch(k) == 0])
group_table(u'опор 1', [k for k in keys if nanch(k) == 1])
group_table(u'опор 2', [k for k in keys if nanch(k) == 2])
group_table(u'опор ≥ 2', [k for k in keys if nanch(k) >= 2])
group_table(u'опор ≥ 3', [k for k in keys if nanch(k) >= 3])
for det in sorted(set(R[BASE][k]['det'] for k in keys)):
    group_table(u'  прибор ' + det, [k for k in keys if R[BASE][k]['det'] == det])
group_table(u'CsI (ASN16 + RC103)', [k for k in keys if k.startswith('ASN16') or k.startswith('RC103')])
group_table(u'NaI (G1S16 + G1S24 + AS80)', [k for k in keys if not (k.startswith('ASN16') or k.startswith('RC103'))])
group_table(u'Am-241 ×4', [k for k in keys if k in AM241])
group_table(u'смеси G1S16_Mix ×3', [k for k in keys if k in MIX])
group_table(u'всё, кроме Am-241 ×4 и Mix ×3', [k for k in keys if k not in AM241 and k not in MIX])
for a in ARMS[1:]:
    for c in sorted(set(R[a][k].get('anchor_light', '') for k in keys)):
        if c:
            group_table(u'  кривая %s (по %s)' % (c, a), [k for k in keys if R[a][k].get('anchor_light', '') == c])
    break

# опоры: число опор по плечам
print(u'\nчисло опор поспектрово: ' + '; '.join(u'%s — опор 1: %d, 2: %d, ≥3: %d, всего %d' % (
    a, sum(1 for k in keys if int(R[a][k]['anchors_used'] or 0) == 1), sum(1 for k in keys if int(R[a][k]['anchors_used'] or 0) == 2),
    sum(1 for k in keys if int(R[a][k]['anchors_used'] or 0) >= 3), sum(int(R[a][k]['anchors_used'] or 0) for k in keys)) for a in ARMS))
for a in ARMS[1:]:
    lost = [(k, R[BASE][k]['anchors_used'], R[a][k]['anchors_used']) for k in keys if int(R[a][k]['anchors_used'] or 0) < int(R[BASE][k]['anchors_used'] or 0)]
    gain = [(k, R[BASE][k]['anchors_used'], R[a][k]['anchors_used']) for k in keys if int(R[a][k]['anchors_used'] or 0) > int(R[BASE][k]['anchors_used'] or 0)]
    print(u'  %s: потеряли опоры %d: %s' % (a, len(lost), ', '.join('%s %s→%s' % x for x in lost) or u'—'))
    print(u'  %s: прибавили опоры %d: %s' % (a, len(gain), ', '.join('%s %s→%s' % x for x in gain) or u'—'))
    zb = [k for k in keys if float(R[BASE][k]['anchor_offset_kev'] or 0) != 0.0]
    za = [k for k in keys if float(R[a][k]['anchor_offset_kev'] or 0) != 0.0]
    print(u'  ноль подобран: %s у %d, %s у %d; |ноль| медиана %s %.2f кэВ, %s %.2f кэВ' % (
        BASE, len(zb), a, len(za), BASE, statistics.median([abs(float(R[BASE][k]['anchor_offset_kev'])) for k in zb]) if zb else 0,
        a, statistics.median([abs(float(R[a][k]['anchor_offset_kev'])) for k in za]) if za else 0))

print(u'\nAm-241 ×4, смеси ×3, CsI поимённо и спектры под надзором (χ²/ndf по плечам; опор / ноль кэВ по плечам):')
for k in AM241 + MIX + [k for k in keys if k.startswith('ASN16') or k.startswith('RC103')] + WATCH:
    if k in R[BASE] and k in R[ARMS[-1]]:
        print(u'  %-20s ' % k + '  '.join('%s %7.3f (оп. %s, ноль %s, %s)' % (a[-5:], chi(a, k), R[a][k]['anchors_used'], R[a][k]['anchor_offset_kev'], zsrc(a, k)) for a in ARMS if k in R[a])
              + u'  Δ %+.3f' % (chi(ARMS[1], k) - chi(BASE, k)))

for a in ARMS[1:]:
    d = sorted(((chi(a, k) - chi(BASE, k), k) for k in keys))
    print(u'\n%s − %s: лучшие пять: ' % (a, BASE) + '; '.join(u'%s %.2f→%.2f (%+.2f, оп. %s)' % (k, chi(BASE, k), chi(a, k), dv, R[BASE][k]['anchors_used']) for dv, k in d[:5]))
    print(u'      худшие пять: ' + '; '.join(u'%s %.2f→%.2f (%+.2f, оп. %s)' % (k, chi(BASE, k), chi(a, k), dv, R[BASE][k]['anchors_used']) for dv, k in d[-5:][::-1]))
    print(u'      хуже на ≥ +0.05: ' + '; '.join(u'%s %+.2f' % (k, dv) for dv, k in d if dv >= 0.05))
    print(u'      лучше на ≤ −0.10: ' + '; '.join(u'%s %+.2f' % (k, dv) for dv, k in d if dv <= -0.10))

# S95 Cd-109 и состав смесей
C = {a: comps(a) for a in ARMS}
for k in ('G1S16_Cd109_P5', 'G1S24_Cd109_P5', 'G1S16_Cd109_P25'):
    if all(k in C[a] for a in ARMS):
        print(u'S95 %s: Cd-109 доля ' % k + ', '.join('%s %s' % (a[-5:], C[a][k]['Cd-109']['share_pct'] if 'Cd-109' in C[a][k] else u'—') for a in ARMS)
              + u'; подавлен: ' + ', '.join('%s %s' % (a[-5:], sum(1 for c in C[a][k].values() if c.get('kind') == 'suppressed')) for a in ARMS))
print(u'\nсостав смесей G1S16_Mix (доля % / z по плечам):')
for k in MIX:
    for nuc in ('Am-241', 'Cs-137', 'Eu-152', 'Ti-44'):
        print(u'  %-20s %-7s ' % (k, nuc) + '  '.join('%s %s' % (a[-5:], ('%s / %s' % (C[a][k][nuc]['share_pct'], C[a][k][nuc]['z'])) if nuc in C[a].get(k, {}) else u'НЕТ') for a in ARMS))

# опоры: остатки принятых
AN = {a: anchors(a) for a in ARMS}
def acc(a, k):
    return [abs(float(r['shift_kev'])) for r in AN[a].get(k, []) if r['used'] == '1' and r['shift_kev'] not in ('', 'NaN')]
print(u'\nостатки ПРИНЯТЫХ опор |изм − мод|, кэВ:')
for a in ARMS:
    v = [x for k in keys for x in acc(a, k)]
    print(u'  %-16s n %3d медиана %.2f среднее %.2f' % (a, len(v), statistics.median(v) if v else float('nan'), sum(v) / max(1, len(v))))

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
            print(u'          %-18s %s' % (a, ' '.join('%s %+.2f' % (k, x) for k, x in byline[e].get(a, []))))


print(u'\nнуль света по съёмке (плечо %s): E(0) калибровки, z0 карты (свет в кан 0), источник, растяжение:' % ARMS[1])
pat = re.compile(u'кан 0 = ([-\\d.]+) кэВ калибровки, свет ([-\\d.]+) кэВ \\((по съёмке|по прибору): (.*?)\\), растяжение ([\\d.]+)')
for k in keys:
    n = R[ARMS[1]][k].get('anchor_note') or ''
    m = pat.search(n)
    if m:
        print(u'  %-22s E(0) %7.2f  z0 %7.2f  %s: %s  s %s' % (k, float(m.group(1)), float(m.group(2)), m.group(3), m.group(4), m.group(5)))
