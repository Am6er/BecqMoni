# -*- coding: utf-8 -*-
u"""bqp15_cut.py — П15 / A307(а): разрез −2⟨r,Δm₀⟩ и ‖Δm₀‖² по областям порога регистрации, на прибор.

Δm₀ строится ТЕМ ЖЕ способом, что bqp12_real.py (амплитуды плеча А × колонки плеча Б, веса w_rep плеча А,
окно first..last). Положительный контроль: Σ по всем каналам = 17 303 / 99 309 (П12 §4).

Области — по КАНАЛАМ (порог — свойство АЦП/тракта, у всех спектров прибора один канал первого отсчёта;
калибровки внизу шкалы расходятся между спектрами на десятки кэВ — `AdcFloorOf`, A302), границы берутся из
чистых спектров прибора (bqp15_edge_fit.py) и печатаются здесь же:
  R0 — ниже первого ненулевого канала (данных нет);
  R1 — переход: от первого ненулевого канала до выхода на плато (90 % плато);
  R2 — буфер: плато … плато + 1 ПШПВ;
  R3 — уверенная регистрация: выше плато + 1 ПШПВ; внутри — полосы П12 по кэВ калибровки спектра.
Наложение N — каналы с y = raw − α·bg < 0 (A302).
"""
import csv, io, os, sys, glob
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
OUT12 = r'C:\Users\moroz\bqp12_out'
OUT = r'C:\Users\moroz\bqp15_out'
os.makedirs(OUT, exist_ok=True)

# границы по каналам, на прибор: (первый ненулевой, конец перехода = 90 % плато, конец буфера = плато + 1 ПШПВ)
# — из bqp15_edge_fit.py, см. журнал §2
EDGE = {'G1S16': (5, 12, 16), 'G1S24': (5, 14, 18), 'AS80': (52, 66, 92), 'ASN16': (5, 37, 48)}
BANDS = [(-1e9, 30.0), (30.0, 45.0), (45.0, 100.0), (100.0, 300.0), (300.0, 1e9)]
BN = ['<30', '30-45', '45-100', '100-300', '>300']
# группы: A — чистые до 100 кэВ (только континуум), B — рентген/линии 14…45 кэВ, C — рентген 55…90 кэВ
GROUP = {}
for _n in ('Co60', 'Na22', 'Mn54', 'Y88', 'Zn65'): GROUP[_n] = 'A'
for _n in ('Cs137', 'Ba133', 'Ce139', 'Eu152', 'Am241', 'Cd109', 'Co57'): GROUP[_n] = 'B'
for _n in ('Lu176', 'Bi207', 'Th228', 'Th232WT20'): GROUP[_n] = 'C'
GN = {'A': 'A чистые <100', 'B': 'B рентген 14-45', 'C': 'C рентген 55-90'}


def load(arm, key):
    d = os.path.join(OUT12, arm + '_dump')
    amps = list(csv.DictReader(io.open(os.path.join(d, key + '_amps.csv'), encoding='utf-8-sig')))
    rows = list(csv.DictReader(io.open(os.path.join(d, key + '_cols.csv'), encoding='utf-8-sig')))
    chi = list(csv.DictReader(io.open(os.path.join(d, key + '_chi.csv'), encoding='utf-8-sig')))
    names = [a['name'] for a in amps]
    amp = np.array([float(a['amp']) for a in amps])
    C = np.array([[float(r[n]) for n in names] for r in rows])
    kev = np.array([float(r['keV']) for r in rows])
    y = np.array([float(r['y']) for r in rows])
    w = np.array([float(r['w_rep']) for r in rows])
    res = np.array([float(r['resid']) for r in rows])
    raw = np.array([int(r['raw']) for r in chi])
    bg = np.array([float(r['bg']) for r in chi])
    model = np.array([float(r['model']) for r in chi])
    lo, hi = int(amps[0]['first']), int(amps[0]['last'])
    return names, amp, C, kev, y, w, res, lo, hi, raw, bg, model


def known():
    ks = []
    for f in glob.glob(os.path.join(OUT12, 'a', '*_runs.csv')):
        for r in csv.DictReader(io.open(f, encoding='utf-8-sig')):
            if r['part'] == 'known' and r['chi2ndf'] not in ('', 'ERROR'):
                ks.append(r['spectrum'])
    return sorted(ks)


def per_channel(key):
    nA, aA, CA, kev, yA, wA, rA, lo, hi, raw, bg, mA_app = load('a', key)
    nB, aB, CB, kev2, yB, wB, rB, lo2, hi2, raw2, bg2, mB_app = load('b', key)
    assert (lo, hi) == (lo2, hi2) and np.abs(yA - yB).max() == 0.0 and (raw == raw2).all()
    win = np.zeros(len(kev), bool); win[lo:hi + 1] = True
    w = np.where(win, wA, 0.0)
    assert np.abs((yA - CA @ aA) - rA)[win].max() < 1e-6 * max(1.0, np.abs(yA).max()), key
    mB0 = np.zeros(len(kev))
    for j, n in enumerate(nA):
        if aA[j] <= 0: continue
        if n in nB:
            mB0 += aA[j] * CB[:, nB.index(n)]
    mA = CA @ aA
    dm0 = mB0 - mA
    return dict(kev=kev, y=yA, w=w, r=rA, dm0=dm0, raw=raw, bg=bg, mA=mA, mB0=mB0, mB=mB_app, win=win)


def regions(key, d):
    det = key.split('_')[0]
    c1, c2, c3 = EDGE[det]
    ch = np.arange(len(d['kev']))
    R = {}
    R['R0 ниже порога'] = ch < c1
    R['R1 переход'] = (ch >= c1) & (ch < c2)
    R['R2 буфер +1ПШПВ'] = (ch >= c2) & (ch < c3)
    R['R3 уверенная'] = ch >= c3
    return R


def sums(d, m):
    w = d['w'] * m
    t1 = float((d['dm0'] ** 2 * w).sum())
    t2 = float(-2.0 * (d['r'] * d['dm0'] * w).sum())
    chi = float((d['r'] ** 2 * w).sum())
    nr = np.sqrt(chi); nd = np.sqrt(t1)
    cos = -0.5 * t2 / (nr * nd) if nr > 0 and nd > 0 else float('nan')
    # знаковый разрез: доля отрицательного ⟨r,Δm₀⟩ (по модулю) среди всех |r·Δm₀·w|
    p = d['r'] * d['dm0'] * w
    neg = float(-p[p < 0].sum()); pos = float(p[p > 0].sum())
    return dict(t1=t1, t2=t2, chi=chi, cos=cos, neg=neg, pos=pos, n=int((m & d['win']).sum()))


RN = ['R0 ниже порога', 'R1 переход', 'R2 буфер +1ПШПВ', 'R3 уверенная']


def main():
    keys = known()
    per = {}
    tot = np.zeros(2)
    rows_csv = []
    for key in keys:
        d = per_channel(key)
        per[key] = d
        s = sums(d, d['win'])
        tot += [s['t1'], s['t2']]
    print('=== контроль (i): Σ по всем каналам, 42 спектра: ‖Δm₀‖² = %.1f (П12: 17302.9), −2⟨r,Δm₀⟩ = %.1f (П12: 99309.1)' % tuple(tot))
    assert abs(tot[0] - 17302.9) < 1.0 and abs(tot[1] - 99309.1) < 1.0
    b12 = [(-1e9, 45.0), (45.0, 100.0), (100.0, 300.0), (300.0, 1e9)]
    acc = np.zeros((4, 2))
    for key, d in per.items():
        for i, (e0, e1) in enumerate(b12):
            s = sums(d, d['win'] & (d['kev'] >= e0) & (d['kev'] < e1))
            acc[i] += [s['t1'], s['t2']]
    print('    по полосам П12 (<45 / 45-100 / 100-300 / >300): ‖Δm₀‖² %s ; −2⟨r,Δm₀⟩ %s' % (' / '.join('%.1f' % v for v in acc[:, 0]), ' / '.join('%.1f' % v for v in acc[:, 1])))

    print('\n=== границы областей по каналам (на прибор) и в кэВ по калибровке КАЖДОГО спектра ===')
    print('%-20s %6s %6s %6s   %8s %8s %8s' % ('спектр', 'ch1', 'ch2', 'ch3', 'кэВ ch1', 'кэВ ch2', 'кэВ ch3'))
    for key, d in per.items():
        c1, c2, c3 = EDGE[key.split('_')[0]]
        print('%-20s %6d %6d %6d   %8.1f %8.1f %8.1f' % (key, c1, c2, c3, d['kev'][c1], d['kev'][c2], d['kev'][c3]))

    print('\n=== разрез по областям, на спектр: −2⟨r,Δm₀⟩ | ‖Δm₀‖² | cos(r,Δm₀) ===')
    print('%-20s ' % 'спектр' + ' '.join('%26s' % n for n in RN) + '   %10s' % 'всего t2')
    inst = {}
    for key, d in per.items():
        R = regions(key, d)
        det = key.split('_')[0]
        line = '%-20s ' % key
        rec = {}
        for n in RN:
            s = sums(d, d['win'] & R[n])
            rec[n] = s
            line += ' %10.1f %8.1f %6.3f' % (s['t2'], s['t1'], s['cos'])
        sa = sums(d, d['win'])
        line += '   %10.1f' % sa['t2']
        print(line)
        inst.setdefault(det, []).append((key, rec, d, R))
        rows_csv.append([key] + [rec[n][k] for n in RN for k in ('t2', 't1', 'chi', 'cos', 'neg', 'pos')])
    with io.open(os.path.join(OUT, 'cut_regions_per_spectrum.csv'), 'w', encoding='utf-8', newline='') as f:
        f.write('spectrum,' + ','.join('%s_%s' % (n.split()[0], k) for n in RN for k in ('t2', 't1', 'chi2A', 'cos', 'neg', 'pos')) + '\n')
        for r in rows_csv:
            f.write(','.join([r[0]] + [repr(float(v)) for v in r[1:]]) + '\n')

    print('\n=== разрез по областям, на ПРИБОР (Σ по спектрам): −2⟨r,Δm₀⟩ | ‖Δm₀‖² | χ²_A | cos | доля отрицательного ⟨r,Δm₀⟩ ===')
    grand = {n: np.zeros(5) for n in RN}
    for det, lst in inst.items():
        print('--- %s (%d спектров), границы ch %s' % (det, len(lst), EDGE[det]))
        for n in RN:
            t2 = sum(rec[n]['t2'] for _, rec, _, _ in lst); t1 = sum(rec[n]['t1'] for _, rec, _, _ in lst)
            chi = sum(rec[n]['chi'] for _, rec, _, _ in lst)
            neg = sum(rec[n]['neg'] for _, rec, _, _ in lst); pos = sum(rec[n]['pos'] for _, rec, _, _ in lst)
            cos = -0.5 * t2 / np.sqrt(chi * t1) if chi > 0 and t1 > 0 else float('nan')
            grand[n] += [t2, t1, chi, neg, pos]
            print('  %-18s t2 = %10.1f  t1 = %8.1f  χ²_A = %12.1f  cos = %6.3f  отриц. доля ⟨r,Δm₀⟩ = %5.1f %%' % (n, t2, t1, chi, cos, 100.0 * neg / (neg + pos) if neg + pos > 0 else float('nan')))
    print('--- все 4 прибора')
    for n in RN:
        t2, t1, chi, neg, pos = grand[n]
        cos = -0.5 * t2 / np.sqrt(chi * t1) if chi > 0 and t1 > 0 else float('nan')
        print('  %-18s t2 = %10.1f (%5.1f %% от 99309)  t1 = %8.1f (%5.1f %%)  χ²_A = %12.1f  cos = %6.3f  отриц. доля = %5.1f %%' % (n, t2, 100 * t2 / 99309.1, t1, 100 * t1 / 17302.9, chi, cos, 100.0 * neg / (neg + pos)))

    print('\n=== внутри R3 «уверенная регистрация» — полосы по кэВ калибровки спектра (t2 | t1 | cos), на прибор ===')
    for det, lst in inst.items():
        print('--- %s' % det)
        for grp in ('все', 'A', 'B', 'C'):
            sub = [(k, rec, d, R) for k, rec, d, R in lst if grp == 'все' or GROUP[k.split('_')[1]] == grp]
            grp = GN.get(grp, grp)
            if not sub: continue
            line = '  %-26s n=%2d ' % (grp, len(sub))
            for i, (e0, e1) in enumerate(BANDS):
                t2 = t1 = chi = 0.0
                for k, rec, d, R in sub:
                    s = sums(d, d['win'] & R['R3 уверенная'] & (d['kev'] >= e0) & (d['kev'] < e1))
                    t2 += s['t2']; t1 += s['t1']; chi += s['chi']
                cos = -0.5 * t2 / np.sqrt(chi * t1) if chi > 0 and t1 > 0 else float('nan')
                line += ' | %s: %9.1f %7.1f %6.3f' % (BN[i], t2, t1, cos)
            print(line)
    print('--- ВСЕ 4 прибора, внутри R3, по группам')
    allv = [x for lst in inst.values() for x in lst]
    for grp in ('все', 'A', 'B', 'C'):
        sub = [(k, rec, d, R) for k, rec, d, R in allv if grp == 'все' or GROUP[k.split('_')[1]] == grp]
        line = '  %-26s n=%2d ' % (GN.get(grp, grp), len(sub))
        for i, (e0, e1) in enumerate(BANDS):
            t2 = t1 = chi = 0.0
            for k, rec, d, R in sub:
                s = sums(d, d['win'] & R['R3 уверенная'] & (d['kev'] >= e0) & (d['kev'] < e1))
                t2 += s['t2']; t1 += s['t1']; chi += s['chi']
            cos = -0.5 * t2 / np.sqrt(chi * t1) if chi > 0 and t1 > 0 else float('nan')
            line += ' | %s: %9.1f %7.1f %6.3f' % (BN[i], t2, t1, cos)
        print(line)
    print('--- ВСЕ 4 прибора, по областям, по группам (t2 | t1 | cos)')
    for grp in ('все', 'A', 'B', 'C'):
        sub = [(k, rec, d, R) for k, rec, d, R in allv if grp == 'все' or GROUP[k.split('_')[1]] == grp]
        line = '  %-26s n=%2d ' % (GN.get(grp, grp), len(sub))
        for n in RN:
            t2 = sum(rec[n]['t2'] for _, rec, _, _ in sub); t1 = sum(rec[n]['t1'] for _, rec, _, _ in sub); chi = sum(rec[n]['chi'] for _, rec, _, _ in sub)
            cos = -0.5 * t2 / np.sqrt(chi * t1) if chi > 0 and t1 > 0 else float('nan')
            line += ' | %s: %9.1f %7.1f %6.3f' % (n.split()[0], t2, t1, cos)
        print(line)
    print('\n=== R1 переход + R2 буфер — те же полосы по кэВ калибровки спектра (t2 | t1 | cos), на прибор ===')
    for det, lst in inst.items():
        line = '  %-8s' % det
        for i, (e0, e1) in enumerate(BANDS):
            t2 = t1 = chi = 0.0
            for k, rec, d, R in lst:
                m = d['win'] & (R['R1 переход'] | R['R2 буфер +1ПШПВ']) & (d['kev'] >= e0) & (d['kev'] < e1)
                s = sums(d, m); t2 += s['t2']; t1 += s['t1']; chi += s['chi']
            cos = -0.5 * t2 / np.sqrt(chi * t1) if chi > 0 and t1 > 0 else float('nan')
            line += ' | %s: %9.1f %7.1f %6.3f' % (BN[i], t2, t1, cos)
        print(line)

    print('\n=== наложение N: каналы с y = raw − α·bg < 0 (A302) — где лежат и что несут ===')
    for det, lst in inst.items():
        for k, rec, d, R in lst:
            m = d['win'] & (d['y'] < 0)
            if m.sum() == 0: continue
            s = sums(d, m)
            byR = ', '.join('%s %d' % (n.split()[0], int((m & R[n]).sum())) for n in RN if (m & R[n]).sum())
            print('  %-20s каналов %4d (%s)  t2 = %8.1f  t1 = %6.1f  χ²_A = %10.1f' % (k, int(m.sum()), byR, s['t2'], s['t1'], s['chi']))

    print('\n=== группа A (чистые до 100 кэВ): t2 и cos по областям, на спектр ===')
    print('%-20s ' % 'спектр' + ' '.join('%18s' % n for n in RN))
    for det, lst in inst.items():
        for k, rec, d, R in lst:
            if GROUP[k.split('_')[1]] != 'A': continue
            print('%-20s ' % k + ' '.join('%10.1f %7.3f' % (rec[n]['t2'], rec[n]['cos']) for n in RN))


if __name__ == '__main__':
    main()
