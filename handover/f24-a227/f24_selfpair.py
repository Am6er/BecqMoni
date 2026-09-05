# -*- coding: utf-8 -*-
"""`A227` + `A226` п.4: улика МОЖЕТ ЛЕЖАТЬ ВНУТРИ ТОГО ЖЕ ПИКА (полоса F24).

    python f24_selfpair.py <labels_after.csv>

`A226` назвала цену строгого правила: 70 приборных подписей рентгена. Причина
там же и написана — у сцинтиллятора Kalpha и Kbeta сливаются в ОДИН пик, и
«другого пика» у более яркой линии не бывает по построению. Вариант 4 решения
Amber: «сперва научить правило видеть другую свою линию ВНУТРИ того же пика и
мерить заново». Здесь это и меряется.

Улика при `self_pair=True` засчитывается, если ДРУГАЯ линия того же имени
попадает в окно подтверждения ОТ ЭТОГО ЖЕ пика — то есть пик широк настолько,
что несёт обе линии сразу.
"""
import importlib.util, os, sys, collections

try:
    sys.stdout.reconfigure(encoding='utf-8')
except Exception:
    pass

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.normpath(os.path.join(_HERE, '..', '..'))
_SW = os.path.join(_REPO, 'handover', 'handover-2026-09-05-o1-runs', 'o1_rule_sweep.py')
spec = importlib.util.spec_from_file_location('sw', _SW)
sw = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sw)
truth = sw.truth


def cand_names(row):
    c = (row.get('candidates') or '').strip()
    return [x for x in c.split('|') if x] if c else ([row['nuclide']] if row['nuclide'] else [])


def confirmed(row, peaks, lib, k_conf, mode, self_pair):
    name = row['nuclide']
    own = lib.get(name, [])
    line = sw.num(row, 'line_kev')
    rmin, rmax = sw.num(row, 'range_min_kev'), sw.num(row, 'range_max_kev')
    others = [e for (e, i) in own
              if abs(e - line) > 1e-9 and rmin is not None and rmin <= e <= rmax]
    if not others:
        return None
    for e in others:
        for p in peaks:
            f = sw.num(p, 'fwhm_kev')
            if not f or f <= 0:
                continue
            if abs(sw.num(p, 'peak_kev') - e) > k_conf * f:
                continue
            if p is row:
                # своя же линия ВНУТРИ этого пика: улика есть, но только если
                # так разрешено (иначе — как раньше, чужой пик обязателен)
                if self_pair:
                    return True
                continue
            if mode == 'peak':
                return True
            if mode == 'label' and p['nuclide'] == name:
                return True
            if mode == 'cand' and name in cand_names(p):
                return True
    return False


def apply_rule(per, lib, k_conf, mode, theta_yield, theta_miss, self_pair):
    out = {}
    for spec_name, peaks in per.items():
        for r in peaks:
            key = (spec_name, r['peak_kev'])
            if not r['nuclide']:
                out[key] = ('', r)
                continue
            inten = sw.num(r, 'intensity_pct')
            missf = sw.num(r, 'miss_fwhm')
            if not (inten is not None and inten > 0.0):
                out[key] = (r['nuclide'], r)
                continue
            by_yield = inten < theta_yield
            by_miss = theta_miss is not None and missf is not None and missf > theta_miss
            if by_yield or by_miss:
                if confirmed(r, peaks, lib, k_conf, mode, self_pair) is False:
                    out[key] = ('', r)
                    continue
            out[key] = (r['nuclide'], r)
    return out


def counts(base, out, scene):
    ca = sw.score(out, scene)
    lost = ist = pr = 0
    rem = 0
    for kk in base:
        nm, r = base[kk]
        if nm and not out[kk][0]:
            lost += 1
            v = truth.verdict(nm, scene.get(kk[0], set()))
            if v == 'ИСТИНА':
                ist += 1
            elif v == 'приборное':
                pr += 1
            if nm == 'I-131' and abs(float(r['line_kev']) - 364.0) < 0.5:
                rem += 1
    return ca, lost, ist, pr, rem


def line(title, ca, lost, ist, pr, rem):
    print('%-52s %7d %6d %6d %7d %6d %6d %6d   %d/6'
          % (title, ca['ИСТИНА'], ca['ФОН'], ca['ЛОЖЬ'], ca['приборное'],
             lost, ist, pr, rem))


def main(path):
    lib = sw.load_library()
    per = sw.load_peaks(path)
    scene = truth.load()
    base = {}
    for s, ps in per.items():
        for r in ps:
            base[(s, r['peak_kev'])] = (r['nuclide'], r)
    cb = sw.score(base, scene)
    print('БАЗА: ИСТИНА %d  ФОН %d  ЛОЖЬ %d  приборное %d  нет подписи %d'
          % (cb['ИСТИНА'], cb['ФОН'], cb['ЛОЖЬ'], cb['приборное'], cb['нет подписи']))
    print()
    hdr = ('%-52s %7s %6s %6s %7s %6s %6s %6s   %s'
           % ('вариант', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято', 'ист.-', 'приб.-', 'I-131'))
    print(hdr)
    print('-' * len(hdr))

    # контроль модели
    out = apply_rule(per, lib, 0.5, 'peak', 5.0, None, False)
    line('КОНТРОЛЬ: нынешнее правило (обязано дать базу)', *counts(base, out, scene))
    same = all(out[k][0] == base[k][0] for k in base)
    print('   подписи совпали с базой: %s' % ('ДА' if same else '⛔ НЕТ'))
    print()

    print('СУДИТЬ ВСЕХ, улика внутри того же пика ЗАСЧИТЫВАЕТСЯ:')
    for mode in ('peak', 'cand', 'label'):
        for k in (0.25, 0.5, 0.75):
            out = apply_rule(per, lib, k, mode, 1e9, None, True)
            line('  улика %-5s окно %.2f' % (mode, k), *counts(base, out, scene))
    print()
    print('ТО ЖЕ БЕЗ внутрипикового зачёта (для сравнения цены):')
    for mode in ('peak', 'cand', 'label'):
        for k in (0.25, 0.5, 0.75):
            out = apply_rule(per, lib, k, mode, 1e9, None, False)
            line('  улика %-5s окно %.2f' % (mode, k), *counts(base, out, scene))
    print()
    print('ЗАПУСК «выход<5 % ИЛИ промах > T», внутрипиковый зачёт ВКЛ:')
    for mode in ('cand', 'peak'):
        for tm in (0.40, 0.30, 0.20, 0.15):
            out = apply_rule(per, lib, 0.5, mode, 5.0, tm, True)
            line('  улика %-5s окно 0.50, промах > %.2f' % (mode, tm),
                 *counts(base, out, scene))
    print()

    for mode, k in (('cand', 0.5), ('peak', 0.5), ('cand', 0.75)):
        out = apply_rule(per, lib, k, mode, 1e9, None, True)
        ca, lost, ist, pr, rem = counts(base, out, scene)
        print('ЦЕНА «судить всех», улика %s, окно %.2f, внутрипиковый зачёт ВКЛ:' % (mode, k))
        print('   потеряно ИСТИННЫХ %d, приборных %d, снято всего %d, I-131 %d/6'
              % (ist, pr, lost, rem))
        for kk in sorted(base):
            nm, r = base[kk]
            if nm and not out[kk][0]:
                v = truth.verdict(nm, scene.get(kk[0], set()))
                if v in ('ИСТИНА', 'приборное'):
                    print('   %-10s %-26s пик %9s -> %-12s %9s кэВ I=%-8s промах %s'
                          % (v, kk[0], kk[1], nm, r['line_kev'], r['intensity_pct'],
                             r['miss_fwhm']))
        print()


if __name__ == '__main__':
    main(sys.argv[1])
