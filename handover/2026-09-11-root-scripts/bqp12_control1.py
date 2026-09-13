# -*- coding: utf-8 -*-
# bqp12_control1.py <arm_dump: synb|syna> — контроль 1: разбор истины ПРИЛОЖЕНИЕМ (полная свобода,
# Хубер, свои веса) против истины: амплитуды образов и χ² решателя; для плеча А — просто числа шага 3'.
import csv, io, os, sys, glob
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
OUT = r'C:\Users\moroz\bqp12_out'
KEYS = ['G1S16_Cs137_P5', 'G1S16_Am241_P5', 'G1S16_Ba133_P5', 'G1S24_Ba133_P5', 'G1S24_Bi207_P5', 'G1S24_Am241_P5']

def runs(d):
    out = {}
    for f in glob.glob(os.path.join(d, '*_runs.csv')):
        for r in csv.DictReader(io.open(f, encoding='utf-8-sig')):
            out[r['spectrum']] = r
    return out

def main():
    arm = sys.argv[1]
    rr = runs(os.path.join(OUT, arm))
    for key in KEYS:
        tr = {r['name']: r for r in csv.DictReader(io.open(os.path.join(OUT, 'truth', key + '_truth_amps.csv'), encoding='utf-8-sig'))}
        fa = os.path.join(OUT, arm + '_dump', key + '_asimov_amps.csv')
        fit = {r['name']: r for r in csv.DictReader(io.open(fa, encoding='utf-8-sig'))}
        row = rr[key + '_asimov']
        print('%s (%s): chi2ndf %s, chi2ndf_pois %s, inflate %s, компонентов %s' % (key, arm, row['chi2ndf'], row['chi2ndf_pois'], row['inflate'], row['components']))
        worst = 0.0
        for name, t in tr.items():
            if t['kind'] != 'image':
                continue
            x0 = float(t['amp']); x1 = float(fit[name]['amp']) if name in fit else float('nan')
            d = (x1 / x0 - 1.0) * 100.0 if x0 > 0 else float('nan')
            if x0 > 0 and abs(d) > worst: worst = abs(d)
            print('   %-14s истина %14.6g  фит %14.6g  Δ %+8.4f %%  z %s' % (name, x0, x1, d, fit[name]['z'] if name in fit else '-'))
        extra = [n for n in fit if n not in tr]
        print('   худший образ: %.4f %%; колонок в фите, которых нет в истине: %s' % (worst, extra if extra else 'нет'))
        # континуум: хвосты и сплайн
        for kind in ('tail', 'spline'):
            s0 = sum(float(t['amp']) for t in tr.values() if t['kind'] == kind)
            s1 = sum(float(f['amp']) for f in fit.values() if f['kind'] == kind)
            print('   Σ amp %-6s истина %12.6g  фит %12.6g' % (kind, s0, s1))

if __name__ == '__main__':
    main()
