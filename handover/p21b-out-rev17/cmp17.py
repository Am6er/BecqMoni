# -*- coding: utf-8 -*-
# П21б: сравнение out_rev17_* с out_rev16_* и разложение шага склад/свет.
import csv, glob, io, os, sys, statistics
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
P21 = r'C:\Users\moroz\p21b_out'
def runs(d):
    r = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for row in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            if not row.get('error'):
                r[row['spectrum']] = row
    return r
def allrows(d):
    r = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for row in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            r[row['spectrum']] = row
    return r
def chi(R, k): return float(R[k]['chi2ndf'])
def summ(name, A, B, keys, part='known'):
    ks = [k for k in keys if A[k]['part'] == part]
    da = [chi(A, k) for k in ks]; db = [chi(B, k) for k in ks]
    d = [x - y for x, y in zip(da, db)]
    print(u'%-34s n %3d: %7.1f / %5.2f -> %7.1f / %5.2f  Δ %+6.1f (%+.1f %%)  лучше %d / хуже %d / ровно %d' % (
        name, len(ks), sum(db), statistics.median(db), sum(da), statistics.median(da), sum(d), 100.0 * sum(d) / sum(db),
        sum(1 for x in d if x < -0.005), sum(1 for x in d if x > 0.005), sum(1 for x in d if abs(x) <= 0.005)))
    return ks, d
for kind, r16, r17, lin, line in (
    ('ПОЛНАЯ', 'out_rev16_full', 'out_rev17_full', 'lin', 'line'),
    ('МАЛАЯ', 'out_rev16_mini', 'out_rev17_mini', 'mini_lin', 'mini_line')):
    R16 = runs(os.path.join(ROOT, 'tools', 'pie', r16)); R17 = runs(os.path.join(ROOT, 'tools', 'pie', r17))
    L = runs(os.path.join(P21, lin)); LN = runs(os.path.join(P21, line))
    A16 = allrows(os.path.join(ROOT, 'tools', 'pie', r16)); A17 = allrows(os.path.join(ROOT, 'tools', 'pie', r17))
    print(u'\n=== %s: %s -> %s (строк %d / %d, разобрано %d / %d) ===' % (kind, r16, r17, len(A16), len(A17), len(R16), len(R17)))
    keys = sorted(set(R16) & set(R17) & set(L) & set(LN))
    # контроль: rev17 = плечо line построчно
    same = sum(1 for k in set(R17) & set(LN) if R17[k]['chi2ndf'] == LN[k]['chi2ndf'] and R17[k].get('gain') == LN[k].get('gain') and R17[k].get('anchors_used') == LN[k].get('anchors_used'))
    print(u'контроль «умолчание = плечо line»: совпали chi2ndf/gain/anchors у %d из %d разобранных' % (same, len(set(R17) & set(LN))))
    ks, d = summ(u'rev16 -> rev17 (понятная)', R17, R16, keys)
    summ(u'  склад: rev16 -> новый склад, свет ВЫКЛ', L, R16, keys)
    summ(u'  свет:  новый склад ВЫКЛ -> ВКЛ (line)', LN, L, keys)
    summ(u'rev16 -> rev17 (непонятная)', R17, R16, keys, 'unknown')
    # группы
    def grp(name, sel): 
        kk = [k for k in ks if sel(k)]
        if kk: summ(u'  ' + name, R17, R16, kk)
    grp(u'CsI: ASN16 + RC103', lambda k: k.startswith('ASN16') or k.startswith('RC103'))
    grp(u'ASN16', lambda k: k.startswith('ASN16'))
    grp(u'AS80x80', lambda k: k.startswith('AS80'))
    grp(u'G1S16', lambda k: k.startswith('G1S16'))
    grp(u'G1S24', lambda k: k.startswith('G1S24'))
    grp(u'RC103', lambda k: k.startswith('RC103'))
    dd = sorted(zip(d, ks))
    print(u'  лучшие пять rev17−rev16: ' + '; '.join(u'%s %.2f→%.2f (%+.2f)' % (k, chi(R16, k), chi(R17, k), x) for x, k in dd[:5]))
    print(u'  худшие пять rev17−rev16: ' + '; '.join(u'%s %.2f→%.2f (%+.2f)' % (k, chi(R16, k), chi(R17, k), x) for x, k in dd[-5:][::-1]))
    # непонятная часть — те же спектры?
    u16 = sorted(k for k in R16 if R16[k]['part'] == 'unknown'); u17 = sorted(k for k in R17 if R17[k]['part'] == 'unknown')
    print(u'  непонятная разобрана: rev16 %s | rev17 %s' % (', '.join('%s %.2f' % (k, chi(R16, k)) for k in u16), ', '.join('%s %.2f' % (k, chi(R17, k)) for k in u17)))
    # матрица
    mx = {}
    for k in R17:
        mx[R17[k].get('matrix', '?')] = mx.get(R17[k].get('matrix', '?'), 0) + 1
    print(u'  графа matrix у rev17: %s' % mx)
