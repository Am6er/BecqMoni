# П187: прямой счёт без разбора — отсчёты в окне (net = данные - фон по кривым разбора) против A*выход*eff*LT
# eff — кривая спектра (<Efficiency> файла, полный пик), A — паспорт с распадом (как act.py П186)
import re, os, sys, csv, datetime, math
R = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS'
CUR = sys.argv[1]
src = open(os.path.join(R, 'scripts', 'corpus_def.py'), encoding='utf-8').read()
T = {'Ba-133': 10.551, 'Am-241': 432.6, 'Cd-109': 461.9 / 365.25, 'Eu-152': 13.522, 'Cs-137': 30.08, 'Ce-139': 137.64 / 365.25, 'Co-57': 271.74 / 365.25}
pas = {}
for m in re.finditer(r"key='(G1S\d\d_[^']+)'.*?why='[^']*паспорт: ([A-Za-z]+-\d+) A=([0-9.E+]+) Бк[^']*?(\d\d)-(\d\d)-(\d\d\d\d)", src, re.S):
    k, nuc, a, d, mo, y = m.groups()
    pas[k] = (nuc, float(a), datetime.date(int(y), int(mo), int(d)))
def effcurve(t):
    i = t.find('<Efficiency><Guid>'); e = t[i:t.find('</Curve>', i)]
    pts = [(float(a), float(b)) for a, b in re.findall(r'<Energy>([^<]+)</Energy><Efficiency>([^<]+)</Efficiency>', e)]
    def f(E):
        for (e0, y0), (e1, y1) in zip(pts, pts[1:]):
            if e0 <= E <= e1:
                return math.exp(math.log(y0) + (math.log(y1) - math.log(y0)) * (math.log(E) - math.log(e0)) / (math.log(e1) - math.log(e0)))
    return f, pts
# окна: (спектр, [(lo, hi, [(энергия, выход %)...])...])
W = [(k, w) for k, w in [
    ('G1S16_Cd109_P5', [(12, 30, [(22.1, 84.4), (25.2, 17.93)]), (78, 102, [(88.03, 3.644)])]),
    ('G1S16_Cd109_P25', [(12, 30, [(22.1, 84.4), (25.2, 17.93)]), (78, 102, [(88.03, 3.644)])]),
    ('G1S24_Cd109_P5', [(12, 30, [(22.1, 84.4), (25.2, 17.93)]), (78, 102, [(88.03, 3.644)])]),
    ('G1S16_Am241_P5', [(8, 24, [(13.9, 13.1), (17.6, 18.9), (20.8, 4.6)]), (24, 30, [(26.34, 2.27)]), (50, 68, [(59.54, 35.9)])]),
    ('G1S16_Am241_P25', [(8, 24, [(13.9, 13.1), (17.6, 18.9), (20.8, 4.6)]), (24, 30, [(26.34, 2.27)]), (50, 68, [(59.54, 35.9)])]),
    ('G1S24_Am241_P5', [(8, 24, [(13.9, 13.1), (17.6, 18.9), (20.8, 4.6)]), (24, 30, [(26.34, 2.27)]), (50, 68, [(59.54, 35.9)])]),
    ('G1S16_Ba133_P5', [(26, 40, [(30.8, 92.8), (35.4, 21.9)]), (340, 372, [(356.01, 62.05)])]),
    ('G1S24_Ba133_P5', [(26, 40, [(30.8, 92.8), (35.4, 21.9)]), (340, 372, [(356.01, 62.05)])]),
    ('G1S16_Ce139_P5', [(28, 44, [(33.3, 63.8), (38.3, 15.4)]), (150, 185, [(165.86, 79.9)])]),
    ('G1S16_Eu152_P5', [(35, 50, [(40.0, 58.5), (46.0, 14.8)]), (110, 135, [(121.78, 28.41)])]),
    ('G1S24_Eu152_P5', [(35, 50, [(40.0, 58.5), (46.0, 14.8)]), (110, 135, [(121.78, 28.41)])]),
]]
for k, wins in W:
    nuc, a0, d0 = pas[k]
    t = open(os.path.join(R, 'corpus', 'spectra', k + '.xml'), encoding='utf-8-sig').read()
    d1 = datetime.date(*map(int, re.search(r'<StartTime>(\d\d\d\d)-(\d\d)-(\d\d)', t).groups()))
    lt = float(re.search(r'<LiveTime>([^<]+)', t[t.find('<EnergySpectrum>'):]).group(1))
    A = a0 * 2 ** (-(d1 - d0).days / 365.25 / T[nuc])
    f, pts = effcurve(t)
    rows = list(csv.DictReader(open(os.path.join(CUR, k + '_curves.csv'))))
    print('%s A=%.0f Бк LT=%.0f' % (k, A, lt))
    for lo, hi, lines in wins:
        n = sum(float(r['net']) for r in rows if lo <= float(r['keV']) <= hi)
        exp = sum(A * y / 100 * f(E) * lt for E, y in lines)
        print('   %5.0f-%-5.0f net %9.0f  ожидание по кривой %9.0f  net/ожид %.3f   eff(%s)=%s' % (lo, hi, n, exp, n / exp, lines[0][0], '%.4g' % f(lines[0][0])))
    print('   кривая 10..40:', ' '.join('%.1f:%.3g' % p for p in pts if 10 <= p[0] <= 100))
