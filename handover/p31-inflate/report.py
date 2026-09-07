#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Свод всех таблиц полосы П31 (`A281`) в один файл. Ничего не считает заново —
читает готовые прогоны и печатает то, что попало в журнал."""
import csv, glob, io, math, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from read_runs import read_runs, manifest_counts, f, loglog_slope

R = 'tools/pie/out_p31'
CORPUS = 'tools/CORPUS/scripts/wd_p31c'
MAIN = {'ASN16_Cs137': 'Cs-137', 'G1S24_Eu152_P5': 'Eu-152',
        'G1S16_Co60_P5': 'Co-60', 'AS80_Onyx': 'K-40'}
ORDER = ['ASN16_Cs137', 'G1S24_Eu152_P5', 'G1S16_Co60_P5', 'AS80_Onyx']


def comps_of(d):
    out = {}
    for p in glob.glob(os.path.join(d, '*_components.csv')):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig')):
            out.setdefault(r['spectrum'], {})[r['component']] = f(r['z'])
    return out


def main():
    cnt = manifest_counts(CORPUS)
    lad, ladc = read_runs(R + '/ladder'), comps_of(R + '/ladder')
    syn, sync_ = read_runs(R + '/synth'), comps_of(R + '/synth')
    noh, nohc = read_runs(R + '/lad_nohuber'), comps_of(R + '/lad_nohuber')

    print('== 1. ЛЕСТНИЦА ПРОРЕЖИВАНИЯ, зерно 11 ==')
    print('%-16s %6s %12s %11s %10s %8s %8s %9s'
          % ('сцена', '1/N', 'отсчётов', 'chi2_пуас', 'chi2_реш', 'inflate', 'eps,%', 'z'))
    for s in ORDER:
        for d in (1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024):
            k = '%s_x%d_s11' % (s, d)
            if k not in lad:
                continue
            r = lad[k]
            print('%-16s %6d %12d %11.3f %10.3f %8.3f %8.2f %9.2f'
                  % (s, d, cnt[k], f(r['chi2ndf_pois']), f(r['chi2ndf']),
                     math.sqrt(max(1.0, f(r['chi2ndf']))), f(r['model_residual_pct']),
                     ladc.get(k, {}).get(MAIN[s], 0.0)))
        print()

    print('== 2. НАКЛОНЫ log-log по числу отсчётов ==')
    print('%-16s %10s %10s %10s %10s %10s'
          % ('сцена', 'chi2_пуас', 'chi2_реш', 'inflate', 'z', 'z*inflate'))
    for s in ORDER:
        N, cp, ch, z = [], [], [], []
        for d in (1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024):
            k = '%s_x%d_s11' % (s, d)
            if k not in lad:
                continue
            if f(lad[k]['chi2ndf_pois']) <= 2.0:
                continue
            N.append(cnt[k]); cp.append(f(lad[k]['chi2ndf_pois']))
            ch.append(f(lad[k]['chi2ndf'])); z.append(ladc.get(k, {}).get(MAIN[s], 0.0))
        inf = [math.sqrt(max(1.0, x)) for x in ch]
        zi = [a * b for a, b in zip(z, inf)]
        row = [loglog_slope(N, cp)[0], loglog_slope(N, ch)[0], loglog_slope(N, inf)[0],
               loglog_slope(N, z)[0], loglog_slope(N, zi)[0]]
        print('%-16s %10.3f %10.3f %10.3f %10.3f %10.3f' % tuple([s] + row))
    print()

    print('== 3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: наклон log z ==')
    print('%-16s %12s %12s %12s' % ('сцена', 'плечо P', 'плечо S (5%)', 'настоящая'))
    for s in ORDER:
        out = []
        for src, cm, tag in ((syn, sync_, '_synP'), (syn, sync_, '_synS')):
            N, Z = [], []
            for d in (1, 4, 16, 64, 256, 1024):
                k = '%s%s_x%d_s11' % (s, tag, d)
                if k not in src:
                    continue
                zz = cm.get(k, {}).get(MAIN[s], 0.0)
                if zz > 0:
                    N.append(cnt[k]); Z.append(zz)
            out.append(loglog_slope(N, Z)[0])
        N, Z = [], []
        for d in (1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024):
            k = '%s_x%d_s11' % (s, d)
            zz = ladc.get(k, {}).get(MAIN[s], 0.0)
            if zz > 0:
                N.append(cnt[k]); Z.append(zz)
        out.append(loglog_slope(N, Z)[0])
        print('%-16s %12.3f %12.3f %12.3f' % (s, out[0], out[1], out[2]))
    print()

    print('== 4. ХУБЕР ВЫКЛЮЧЕН (--huber=0): z перестаёт зависеть от набора СОВСЕМ ==')
    print('%-16s %6s %12s %10s %9s %9s' % ('сцена', '1/N', 'отсчётов', 'chi2_реш', 'inflate', 'z'))
    for s in ORDER:
        for d in (1, 4, 16, 64, 256, 1024):
            k = '%s_x%d_s11' % (s, d)
            if k not in noh:
                continue
            r = noh[k]
            print('%-16s %6d %12d %10.3f %9.3f %9.2f'
                  % (s, d, cnt[k], f(r['chi2ndf']), math.sqrt(max(1.0, f(r['chi2ndf']))),
                     nohc.get(k, {}).get(MAIN[s], 0.0)))
        print()
    return 0


if __name__ == '__main__':
    sys.exit(main())
