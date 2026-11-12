# -*- coding: utf-8 -*-
# store54b: лестница плеч корпуса по частям (known / unknown / excluded — раздельно, не складывать).
# Σχ²/ndf и Σχ²/ndf Пуассона по *_spline_runs.csv (только разобранные, χ² числом); паспорт known (passport.py --csv):
# вне / всего, Σ выходов за порог; попарные сдвиги соседних ступеней.
# python -I ladder.py <repo> <full|mini> <плечо1> <плечо2> ...   (плечо — имя каталога в tools/pie)
import csv, glob, os, subprocess, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
REPO, KIND, ARMS = sys.argv[1], sys.argv[2], sys.argv[3:]
PIE = os.path.join(REPO, 'tools', 'pie')
LOGS = r'D:\BqMoni_Claude\store54b\logs'
MINI = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'mini.csv')
only = MINI if KIND == 'mini' else None


def runs(d):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for r in csv.DictReader(open(p, encoding='utf-8-sig')):
            out[r['spectrum']] = r
    return out


def keepset():
    if not only:
        return None
    return set(r['spectrum'] for r in csv.DictReader(
        (l for l in open(only, encoding='utf-8') if not l.startswith('#'))))


def num(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def passport(d):
    tmp = os.path.join(LOGS, 'pp_' + os.path.basename(d) + '.csv')
    cmd = [sys.executable, os.path.join(PIE, 'passport.py'), '--out-dir', d, '--mode', 'spline', '--part', 'known', '--csv', tmp]
    if only:
        cmd += ['--only', only]
    r = subprocess.run(cmd, cwd=REPO, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                       env=dict(os.environ, PYTHONIOENCODING='utf-8'))
    if r.returncode != 0:
        print('⛔ passport.py код', r.returncode, r.stderr.decode('utf-8', 'replace')[-400:])
    return {(x['spectrum'], x['nuclide']): x for x in csv.DictReader(open(tmp, encoding='utf-8-sig'))}


def excess(r):
    return max(0.0, abs(float(r['ratio']) - 1.0) - float(r['tol']))


keep = keepset()
data = {}
for a in ARMS:
    d = os.path.join(PIE, a)
    data[a] = (runs(d), passport(d))

print('== %s: %s' % (KIND, ' -> '.join(ARMS)))
for part in ('known', 'unknown', 'excluded'):
    cells = []
    for a in ARMS:
        ra = data[a][0]
        n = tot = 0
        c = p = 0.0
        for s, r in ra.items():
            if keep is not None and s not in keep:
                continue
            if r['part'] != part:
                continue
            tot += 1
            x = num(r['chi2ndf'])
            if x is None:
                continue
            n += 1
            c += x
            p += num(r['chi2ndf_pois']) or 0.0
        cells.append('%.2f / %.2f (разобр. %d из %d)' % (c, p, n, tot))
    print('%-8s Σχ²/ndf / Пуассон: %s' % (part, ' | '.join(cells)))
cells = []
for a in ARMS:
    q = data[a][1]
    cells.append('вне %d из %d, Σ выходов %.1f п.п.' % (sum(int(r['out']) for r in q.values()), len(q),
                                                       100 * sum(excess(r) for r in q.values())))
print('паспорт known: %s' % ' | '.join(cells))

for i in range(1, len(ARMS)):
    A, B = ARMS[i - 1], ARMS[i]
    ra, rb = data[A][0], data[B][0]
    qa, qb = data[A][1], data[B][1]
    print('\n-- %s -> %s' % (A, B))
    ch = [s for s in sorted(ra) if s in rb and (keep is None or s in keep) and ra[s]['chi2ndf'] != rb[s]['chi2ndf']]
    big = []
    for s in ch:
        x, y = num(ra[s]['chi2ndf']), num(rb[s]['chi2ndf'])
        rel = abs(y - x) / abs(x) if (x not in (None, 0.0) and y is not None) else float('inf')
        big.append((rel, s))
    big.sort(reverse=True)
    print('спектров со сдвигом χ²/ndf: %d из %d; наибольший отн. сдвиг: %s' % (
        len(ch), len([s for s in ra if keep is None or s in keep]),
        ', '.join('%s %.2e' % (s, r) for r, s in big[:5]) or '—'))
    for rel, s in big:
        if rel > 1e-4:
            print('  %-26s %-8s %s -> %s | П %s -> %s | опор %s -> %s' % (
                s, ra[s]['part'], ra[s]['chi2ndf'], rb[s]['chi2ndf'], ra[s]['chi2ndf_pois'], rb[s]['chi2ndf_pois'],
                ra[s].get('anchors_used'), rb[s].get('anchors_used')))
    dif = []
    for k in sorted(qa):
        if k in qb:
            d = float(qb[k]['ratio']) - float(qa[k]['ratio'])
            if d != 0.0:
                dif.append((abs(d), k, qa[k]['ratio'], qb[k]['ratio'], qa[k]['out'], qb[k]['out']))
    dif.sort(reverse=True)
    print('паспорт: отношений сдвинуто %d из %d; наибольшие |Δ|: %s' % (
        len(dif), len(qa), ', '.join('%s/%s %.2e' % (k[0], k[1], d) for d, k, *_ in dif[:5]) or '—'))
    for d, k, x, y, o1, o2 in dif:
        if d > 5e-4 or o1 != o2:
            print('  %-26s %-8s %s -> %s  вне %s -> %s' % (k[0], k[1], x, y, o1, o2))
