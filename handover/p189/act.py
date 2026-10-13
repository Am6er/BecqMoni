# П186: активность разбора (decay_s из *_components.csv) против паспорта (corpus_def.py why), точечные G1S
# python act.py <каталог_выхода_прогона>
import re, os, sys, csv, datetime, glob
R = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS'
src = open(os.path.join(R, 'scripts', 'corpus_def.py'), encoding='utf-8').read()
T = {'Ba-133': 10.551, 'Am-241': 432.6, 'Cd-109': 461.9 / 365.25, 'Eu-152': 13.522, 'Cs-137': 30.08,
     'Co-60': 5.2711, 'Na-22': 2.6029, 'Y-88': 106.63 / 365.25, 'Bi-207': 31.55, 'Zn-65': 243.93 / 365.25,
     'Mn-54': 312.19 / 365.25, 'Ce-139': 137.64 / 365.25, 'Co-57': 271.74 / 365.25, 'Th-228': 1.9116, 'K-40': 1.248e9, 'Ra-226': 1600.0, 'Th-232': 1.405e10, 'Rn-222': 3.8235 / 365.25, 'Ti-44': 59.1}
pas = {}
for m in re.finditer(r"key='(G1S\d\d_[^']+)'.*?why='[^']*паспорт: ([A-Za-z]+-\d+) A=([0-9.E+]+) Бк[^']*?(\d\d)-(\d\d)-(\d\d\d\d)", src, re.S):
    k, nuc, a, d, mo, y = m.groups()
    pas[k] = (nuc, float(a), datetime.date(int(y), int(mo), int(d)))
out = sys.argv[1]
rows = {}
for f in glob.glob(os.path.join(out, 'G1S*_spline_components.csv')):
    for r in csv.DictReader(open(f, encoding='utf-8-sig')):
        rows[(r['spectrum'], r['component'])] = float(r['decay_s']) if r['decay_s'] else float('nan')
for k in sorted(pas):
    nuc, a0, d0 = pas[k]
    if (k, nuc) not in rows:
        continue
    t = open(os.path.join(R, 'corpus', 'spectra', k + '.xml'), encoding='utf-8-sig').read()
    st = re.search(r'<StartTime>(\d\d\d\d)-(\d\d)-(\d\d)', t).groups()
    d1 = datetime.date(*map(int, st))
    yrs = (d1 - d0).days / 365.25
    exp = a0 * 2 ** (-yrs / T[nuc])
    print('%-18s %-7s паспорт %9.0f на %s -> %9.0f на %s; разбор %9.0f; разбор/паспорт %.3f' % (k, nuc, a0, d0, exp, d1, rows[(k, nuc)], rows[(k, nuc)] / exp))
