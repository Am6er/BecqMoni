# П30 (S105): по _spectra.csv ключа --band-audit — доля отсчётов ниже Min_Range, описанная моделью (образы+сплайн) и сплайном отдельно, по плечам; понятная часть малой базы.
import csv, io, sys
sys.stdout.reconfigure(encoding='utf-8')
here = r'C:\Users\moroz\bqp30\handover\p30-cleaner-s'
print('плечо   | спектров(данные>0) | данные ниже Min_Range | модель | сплайн | образы | описано % | сплайн % | образы % | неописано % | худшие по неописанным отсчётам')
for arm in ['base', 'huber0', 'kf1', 'kf2', 'whole', 'whole_huber0', 'whole_kf1', 'whole_k512', 'k512', 'whole_k512kf1']:
    rows = [r for r in csv.DictReader(io.open(f'{here}/bandaudit_{arm}_spectra.csv', encoding='utf-8-sig', newline='')) if r['part'] == 'known']
    D = M = C = I = 0.0; per = []
    for r in rows:
        d = float(r['data_below']); m = float(r['model_below']); c = float(r['continuum_below']); i = float(r['images_below'])
        if d <= 0: continue
        D += d; M += m; C += c; I += i; per.append((r['key'], d, m, c, i, float(r['min_range_keV']), float(r['fit_lo_keV'])))
    per.sort(key=lambda t: -(t[1] - t[2]))
    worst = '; '.join('%s %.0f%% из %.0f (сплайн %.0f%%, образы %.0f%%; фит от %.1f кэВ)' % (k, 100 * (1 - m / d), d, 100 * c / d, 100 * i / d, lo) for k, d, m, c, i, mr, lo in per[:5])
    print('%-7s | %3d | %13.0f | %10.0f | %9.0f | %9.0f | %6.1f | %6.1f | %6.1f | %6.1f | %s' % (arm, len(per), D, M, C, I, 100 * M / D, 100 * C / D, 100 * I / D, 100 * (1 - M / D), worst))
print()
print('поимённо, плечо base: все понятные с данными ниже Min_Range (неописано % / сплайн % / образы %):')
rows = [r for r in csv.DictReader(io.open(f'{here}/bandaudit_base_spectra.csv', encoding='utf-8-sig', newline='')) if r['part'] == 'known']
for r in sorted(rows, key=lambda r: -float(r['data_below'])):
    d = float(r['data_below']); 
    if d <= 0: continue
    m = float(r['model_below']); c = float(r['continuum_below']); i = float(r['images_below'])
    print('  %-24s данные %11.0f (%.2f %% спектра)  неописано %5.1f %%  сплайн %5.1f %%  образы %5.1f %%  Min_Range %.0f  фит от %.1f кэВ  линий ниже %s' % (r['key'], d, 100 * d / float(r['data_total']), 100 * (1 - m / d), 100 * c / d, 100 * i / d, float(r['min_range_keV']), float(r['fit_lo_keV']), r['lines_below']))
