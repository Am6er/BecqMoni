# -*- coding: utf-8 -*-
r"""П85 (AMBER42): чтение отчётов TCCFCALC2 (tccfcalc.out) для пар angular=0/1.
Повторы (_r0, _r1, …) складываются по Areas/AreasCoi (один поток, разные зёрна).

    python read_tccf.py <корень D:\BqMoni_Claude\p85> <tag> [--nuc co60,y88,...] [--all]

Печатает по нуклиду: линии (I ≥ порога) — CF ВЫКЛ/ВКЛ и отношение с σ (σ_rel(CF) = dCF(%) из отчёта,
σ отношения = hypot); сумм-пики (таблица «Coincidence sum peaks» и AreasCoi линий с Areas ≈ 0 —
это сумма, легшая на слабую линию: Co-60 2505.692, Y-88 2734.0) — отношение площадей с пуассоновой σ.
"""
import glob, io, os, re, sys, math

def parse(path):
    lines, sums = {}, {}
    in_sum = False
    for raw in io.open(path, encoding='latin-1'):
        s = raw.rstrip('\r\n')
        if s.startswith('Coincidence sum peaks'):
            in_sum = True
            continue
        parts = [x.strip() for x in s.split('\t')]
        if in_sum:
            if len(parts) == 2 and re.match(r'^[\d.]+$', parts[0]):
                sums[round(float(parts[0]) * 1000.0, 2)] = float(parts[1])
            continue
        if len(parts) >= 10 and re.match(r'^\d+$', parts[0]):
            e = float(parts[1]) * 1000.0
            lines[round(e, 3)] = dict(I=float(parts[2]), cf=float(parts[4]), dcf=float(parts[5]),
                                     eff=float(parts[6]), deff=float(parts[7]),
                                     areas=float(parts[8]), coi=float(parts[9]))
    return lines, sums

def load(root, tag, nuc, ang):
    files = sorted(glob.glob(os.path.join(root, 'tccf_%s_ang%d*' % (nuc, ang), 'out_%s_%s_ang%d*.txt' % (tag, nuc, ang))))
    if not files:
        return None, None, 0
    L, S = {}, {}
    for f in files:
        l, s = parse(f)
        for e, v in l.items():
            d = L.setdefault(e, dict(I=v['I'], areas=0.0, coi=0.0))
            d['areas'] += v['areas']; d['coi'] += v['coi']
        for e, v in s.items():
            S[e] = S.get(e, 0.0) + v
    return L, S, len(files)

def rel_err(n):
    return 1.0 / math.sqrt(n) if n > 0 else float('nan')

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    root, tag = sys.argv[1], sys.argv[2]
    nucs = 'co60,y88,cs134,eu152'
    show_all = '--all' in sys.argv
    for a in sys.argv[3:]:
        if a.startswith('--nuc='):
            nucs = a[6:]
    for nuc in nucs.split(','):
        L0, S0, n0 = load(root, tag, nuc, 0)
        L1, S1, n1 = load(root, tag, nuc, 1)
        if not L0 or not L1:
            print('== %s: нет отчётов (ang0 файлов %d, ang1 файлов %d)' % (nuc, n0, n1)); continue
        print('== %s: файлов ВЫКЛ %d, ВКЛ %d' % (nuc, n0, n1))
        print('   %-10s %10s %10s %8s %8s %8s   %s' % ('кэВ', 'CF ВЫКЛ', 'CF ВКЛ', 'ВКЛ/ВЫКЛ', 'σ', 'σ-ед.', 'I'))
        for e in sorted(set(L0) & set(L1)):
            a, b = L0[e], L1[e]
            if (a['I'] < 0.005 or e < 100.0) and not show_all:
                continue
            if a['coi'] <= 0 or b['coi'] <= 0 or a['areas'] <= 0 or b['areas'] <= 0:
                continue
            cf0 = a['areas'] / a['coi']; cf1 = b['areas'] / b['coi']
            # σ CF: потеря L = 1 − coi/areas; при независимых потоках берём биномиальную σ на обеих
            s0 = math.sqrt(abs(a['areas'] - a['coi']) + 1.0) / a['areas'] if a['areas'] else 1.0
            s1 = math.sqrt(abs(b['areas'] - b['coi']) + 1.0) / b['areas'] if b['areas'] else 1.0
            # плюс шум самих площадей (потоки разные): 1/sqrt(areas)
            s0 = math.hypot(s0, rel_err(a['areas'])); s1 = math.hypot(s1, rel_err(b['areas']))
            r = cf1 / cf0; sr = math.hypot(s0, s1)
            print('   %-10.2f %10.4f %10.4f %8.4f %8.4f %8.1f   %.3g' % (e, cf0, cf1, r, r * sr, (r - 1) / sr if sr else 0, a['I']))
        # сумм-пики: таблица + слабые линии с Areas≈0. Записи таблицы, отличающиеся
        # энергией меньше чем на 0.05 кэВ (у Eu-152 1529.79/1529.80 — округление DLL
        # между прогонами), складываются.
        merged = []
        for e in sorted(set(S0) | set(S1)):
            if merged and abs(e - merged[-1][0]) < 0.05:
                merged[-1][1] += S0.get(e, 0.0); merged[-1][2] += S1.get(e, 0.0)
            else:
                merged.append([e, S0.get(e, 0.0), S1.get(e, 0.0)])
        for e, c0, c1 in merged:
            if c0 < 30 and c1 < 30:
                continue
            r = c1 / c0 if c0 else float('nan'); sr = math.hypot(rel_err(c0), rel_err(c1))
            print('   Σ %-8.2f %10.0f %10.0f %8.4f %8.4f %8.1f   сумм-пик (таблица)' % (e, c0, c1, r, r * sr, (r - 1) / sr))
        for e in sorted(set(L0) & set(L1)):
            a, b = L0[e], L1[e]
            if a['coi'] >= 30 and a['I'] < 0.02 and a['coi'] > 3 * a['areas'] + 5:
                # площадь суммы ≈ AreasCoi − Areas (собственная линия слаба и без каскада — вынос ≈ 0)
                c0, c1 = a['coi'] - a['areas'], b['coi'] - b['areas']
                r = c1 / c0 if c0 else float('nan')
                sr = math.hypot(math.sqrt(a['coi'] + a['areas']) / c0, math.sqrt(b['coi'] + b['areas']) / c1)
                print('   Σ %-8.2f %10.0f %10.0f %8.4f %8.4f %8.1f   сумм-пик на линии %.3f (AreasCoi−Areas; Areas %.0f/%.0f)'
                      % (e, c0, c1, r, r * sr, (r - 1) / sr, e, a['areas'], b['areas']))

if __name__ == '__main__':
    main()
