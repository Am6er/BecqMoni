# П30 (S105): сколько отсчётов ниже Min_Range спектра описано моделью (образы + сплайн) и сколько — сплайном,
# по дампам --dump-curves= (столбцы net, fit, model, continuum_raw). Min_Range — из XML спектра.
import csv, io, os, re, sys, glob
sys.stdout.reconfigure(encoding='utf-8')
corpus = r'C:\Users\moroz\bqp30\tools\CORPUS\corpus\spectra'
arms = sys.argv[1:]
def minrange(key):
    s = io.open(os.path.join(corpus, key + '.xml'), encoding='utf-8', errors='replace').read()
    m = re.search(r'<Min_Range>([\d.]+)</Min_Range>', s)
    return float(m.group(1)) if m else None
def known(d):
    r = set()
    for f in glob.glob(d + '/*_spline_runs.csv'):
        for x in csv.DictReader(io.open(f, encoding='utf-8-sig', newline='')):
            if x['part'] == 'known' and x['chi2ndf'] != 'ERROR': r.add(x['spectrum'])
    return r
keys = sorted(known(r'C:\Users\moroz\bqp30\tools\pie\out_p30_mini_s105_base'))
print('плечо           | спектров | данные ниже Min_Range | модель (образы+сплайн) | из них сплайн | описано % | сплайн % | неописано % | худшие (неописано %)')
for arm in arms:
    d = r'C:\Users\moroz\bqp30\handover\p30-cleaner-s\curves_' + arm
    D = M = C = 0.0; per = []
    for key in keys:
        p = os.path.join(d, key + '_curves.csv')
        if not os.path.exists(p): continue
        mr = minrange(key)
        dd = mm = cc = 0.0
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            if float(r['keV']) >= mr: continue
            net = float(r['net']); fit = float(r['fit']); con = float(r['continuum_raw'])
            if net <= 0: continue
            dd += net; mm += fit; cc += con
        D += dd; M += mm; C += cc
        if dd > 0: per.append((key, dd, mm, cc, mr))
    per.sort(key=lambda t: -(t[1] - t[2]))
    worst = ', '.join('%s %.0f%% (%.0f из %.0f, порог %.0f кэВ)' % (k, 100 * (1 - m / dd), dd - m, dd, mr) for k, dd, m, c, mr in per[:4])
    print('%-15s | %8d | %21.0f | %22.0f | %13.0f | %8.1f | %7.1f | %10.1f | %s' % (arm, len(per), D, M, C, 100 * M / D, 100 * C / D, 100 * (1 - M / D), worst))
