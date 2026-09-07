#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Разбор лестниц прореживания полосы П28 (`A279`)."""
import csv, glob, io, math, os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

def load(outdir):
    comps, runs = [], {}
    for p in glob.glob(os.path.join(outdir, '*_components.csv')):
        with io.open(p, encoding='utf-8-sig') as f:
            comps.extend(csv.DictReader(f))
    for p in glob.glob(os.path.join(outdir, '*_runs.csv')):
        with io.open(p, encoding='utf-8-sig') as f:
            for r in csv.DictReader(f):
                runs[r['spectrum']] = r
    return comps, runs

def manifest(corpus):
    m = {}
    with io.open(os.path.join(corpus, 'manifest.csv'), encoding='utf-8-sig') as f:
        for r in csv.DictReader(f):
            m[r['key']] = r
    return m

KEY = re.compile(r'^(?P<base>.+)_x(?P<den>\d+)_s(?P<seed>\d+)(?P<fam>n?)$')

def rows(outdir, corpus):
    comps, runs = load(outdir)
    man = manifest(corpus)
    out = []
    for c in comps:
        k = c['spectrum']
        mm = KEY.match(k)
        if not mm:
            continue
        m = man.get(k)
        if not m:
            continue
        r = runs.get(k, {})
        out.append(dict(base=mm.group('base'), den=int(mm.group('den')),
                        seed=int(mm.group('seed')), fam=mm.group('fam'),
                        key=k, comp=c['component'], kind=c['kind'],
                        z=float(c['z']), share=float(c['share_pct']),
                        peak=float(c['peak_counts'] or 0),
                        counts=float(m['counts']),
                        chi2=float(r.get('chi2ndf') or 'nan')))
    return out

def fit_slope(xs, ys):
    n = len(xs)
    if n < 3: return float('nan'), float('nan')
    mx, my = sum(xs)/n, sum(ys)/n
    sxx = sum((a-mx)**2 for a in xs); sxy = sum((a-mx)*(b-my) for a,b in zip(xs,ys))
    syy = sum((b-my)**2 for b in ys)
    if sxx <= 0 or syy <= 0: return float('nan'), float('nan')
    return sxy/sxx, sxy/math.sqrt(sxx*syy)

def main():
    outdir = sys.argv[1]
    corpus = os.path.join(ROOT, 'tools', 'CORPUS', 'scripts', 'wd_p28c')
    data = rows(outdir, corpus)
    print(u'строк: %d' % len(data))

    for base in sorted(set(d['base'] for d in data)):
        for fam in sorted(set(d['fam'] for d in data if d['base'] == base)):
            sel = [d for d in data if d['base'] == base and d['fam'] == fam]
            if not sel: continue
            print(u'\n=== %s%s ===' % (base, u' (нулевая семья)' if fam == 'n' else ''))
            # компоненты, встреченные хоть раз
            names = sorted(set(d['comp'] for d in sel))
            dens = sorted(set(d['den'] for d in sel))
            byden = collections.defaultdict(dict)
            for d in sel:
                byden[(d['den'], d['seed'])][d['comp']] = d
            # χ²/ndf и отсчёты по ступеням
            print(u'%-6s %11s %9s' % (u'1/N', u'отсчёты', u'chi2/ndf'))
            for den in dens:
                ss = [d for d in sel if d['den'] == den]
                cn = ss[0]['counts']
                ch = [d['chi2'] for d in ss if not math.isnan(d['chi2'])]
                ch = sorted(set(round(x, 4) for x in ch))
                print(u'%-6d %11.0f %9s' % (den, cn, ','.join('%.3f' % x for x in ch[:3])))
            # z по компонентам
            head = [n for n in names if not n.startswith(('SE-','DE-','Ann','Esc','Xray','Back','pile'))]
            head = head[:12]
            print(u'\n%-6s %11s | %s' % (u'1/N', u'отсчёты', ' '.join('%-9s' % n[:9] for n in head)))
            for den in dens:
                for seed in sorted(set(d['seed'] for d in sel if d['den'] == den)):
                    cells = byden.get((den, seed), {})
                    cn = next((d['counts'] for d in sel if d['den'] == den), 0)
                    line = ' '.join('%-9s' % (('%.2f' % cells[n]['z']) if n in cells else '  -  ')
                                    for n in head)
                    print(u'%-6d %11.0f | %s' % (den, cn, line))
            # наклон log z по log N для каждого компонента
            print(u'\n%-14s %8s %8s %8s   %s' % (u'компонент', u'наклон', 'r', 'точек', u'(теория 0.5)'))
            for n in names:
                pts = [(d['counts'], d['z']) for d in sel if d['comp'] == n and d['z'] > 0]
                if len(pts) < 6: continue
                s, r = fit_slope([math.log10(a) for a,b in pts], [math.log10(b) for a,b in pts])
                print(u'%-14s %8.3f %8.3f %8d' % (n, s, r, len(pts)))

if __name__ == '__main__':
    main()
