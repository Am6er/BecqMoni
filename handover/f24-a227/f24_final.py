# -*- coding: utf-8 -*-
"""`A227`: выбор чисел правила по ПОЛКАМ, каждый параметр отдельно (F24).

    python f24_final.py <labels_after.csv>

Правило-кандидат (три части, каждая со своим смыслом):

  ЗАПУСК   подпись судится, если выход ниже 5 % (нынешнее, `A197`) ЛИБО промах
           победителя больше T·ПШПВ И больше FLOOR кэВ. Второй порог — не
           украшение: 74 % записей поставочной библиотеки несут ЦЕЛУЮ энергию,
           и у германия (ПШПВ 0.8…1.5 кэВ) «промах в ПШПВ» мерит округление
           библиотеки, а не положение пика.

  УЛИКА    пик, спор за который наш нуклид НЕ ПРОИГРЫВАЕТ по положению — то
           есть имя стоит в списке кандидатов того пика (`S64`/O21). Улика
           «просто пик возле линии» на разрешении сцинтиллятора слаба:
           измерено, что `I-131` 722.0 «подтверждается» пиком Bi-212 727.3.

  ЗАЧЁТ    линия того же имени, попавшая в ЭТОТ ЖЕ пик, — улика (`A226` п.4):
           у сцинтиллятора Kalpha и Kbeta рентгена сливаются в один пик.
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

BASE = dict(k_other=0.5, k_self=1.0, mode='cand', theta_yield=5.0,
            theta_miss=0.20, floor=1.5)


def cand_names(row):
    c = (row.get('candidates') or '').strip()
    return [x for x in c.split('|') if x] if c else ([row['nuclide']] if row['nuclide'] else [])


def confirmed(row, peaks, lib, p):
    name = row['nuclide']
    own = lib.get(name, [])
    linek = sw.num(row, 'line_kev')
    rmin, rmax = sw.num(row, 'range_min_kev'), sw.num(row, 'range_max_kev')
    others = [e for (e, i) in own
              if abs(e - linek) > 1e-9 and rmin is not None and rmin <= e <= rmax]
    if not others:
        return None
    for e in others:
        for q in peaks:
            f = sw.num(q, 'fwhm_kev')
            if not f or f <= 0:
                continue
            d = abs(sw.num(q, 'peak_kev') - e)
            if q is row:
                if p['k_self'] is not None and d <= p['k_self'] * f:
                    return True
                continue
            if d > p['k_other'] * f:
                continue
            if p['mode'] == 'peak':
                return True
            if p['mode'] == 'label' and q['nuclide'] == name:
                return True
            if p['mode'] == 'cand' and name in cand_names(q):
                return True
    return False


def apply_rule(per, lib, p):
    out = {}
    for spec_name, peaks in per.items():
        for r in peaks:
            key = (spec_name, r['peak_kev'])
            if not r['nuclide']:
                out[key] = ('', r)
                continue
            inten = sw.num(r, 'intensity_pct')
            if not (inten is not None and inten > 0.0):
                out[key] = (r['nuclide'], r)
                continue
            missf, missk = sw.num(r, 'miss_fwhm'), sw.num(r, 'miss_kev')
            by_yield = inten < p['theta_yield']
            by_miss = (p['theta_miss'] is not None and missf is not None
                       and missk is not None
                       and missf > p['theta_miss'] and missk > p['floor'])
            if (by_yield or by_miss) and confirmed(r, peaks, lib, p) is False:
                out[key] = ('', r)
                continue
            out[key] = (r['nuclide'], r)
    return out


def counts(base, out, scene):
    ca = sw.score(out, scene)
    lost = ist = pr = rem = 0
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


def row(title, ca, lost, ist, pr, rem):
    print('%-44s %7d %6d %6d %7d %6d %6d %6d   %d/6'
          % (title, ca['ИСТИНА'], ca['ФОН'], ca['ЛОЖЬ'], ca['приборное'],
             lost, ist, pr, rem))


def hdr():
    h = ('%-44s %7s %6s %6s %7s %6s %6s %6s   %s'
         % ('вариант', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято', 'ист.-', 'приб.-', 'I-131'))
    print(h)
    print('-' * len(h))


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

    hdr()
    p0 = dict(BASE, theta_miss=None, k_self=None, mode='peak')
    out = apply_rule(per, lib, p0)
    row('КОНТРОЛЬ: нынешнее правило = база', *counts(base, out, scene))
    print('   подписи совпали с базой: %s'
          % ('ДА' if all(out[k][0] == base[k][0] for k in base) else '⛔ НЕТ'))
    print()

    print('ПОЛКА ПО ПРОМАХУ T (пол 1.5 кэВ, улика cand 0.5, зачёт в пике 1.0):')
    hdr()
    for tm in (0.50, 0.40, 0.30, 0.25, 0.20, 0.18, 0.15, 0.12, 0.10):
        out = apply_rule(per, lib, dict(BASE, theta_miss=tm))
        row('T = %.2f' % tm, *counts(base, out, scene))
    print()

    print('ПОЛКА ПО ПОЛУ (T = 0.20):')
    hdr()
    for fl in (0.0, 0.5, 1.0, 1.2, 1.5, 2.0, 2.5, 3.0, 5.0, 8.0):
        out = apply_rule(per, lib, dict(BASE, floor=fl))
        row('пол = %.1f кэВ' % fl, *counts(base, out, scene))
    print()

    print('ПОЛКА ПО ЗАЧЁТУ ВНУТРИ ПИКА (T = 0.20, пол 1.5):')
    hdr()
    for ks in (None, 0.5, 0.75, 1.0, 1.25, 1.5):
        out = apply_rule(per, lib, dict(BASE, k_self=ks))
        row('зачёт в пике = %s' % ('выкл' if ks is None else '%.2f ПШПВ' % ks),
            *counts(base, out, scene))
    print()

    print('СИЛА УЛИКИ (T = 0.20, пол 1.5, зачёт 1.0):')
    hdr()
    for mode in ('peak', 'cand', 'label'):
        for ko in (0.25, 0.5, 0.75):
            out = apply_rule(per, lib, dict(BASE, mode=mode, k_other=ko))
            row('улика %-5s окно %.2f' % (mode, ko), *counts(base, out, scene))
    print()

    print('ВЫБРАННЫЙ ВАРИАНТ: %s' % BASE)
    hdr()
    out = apply_rule(per, lib, BASE)
    row('выбранный', *counts(base, out, scene))
    print()
    print('   снятое поимённо (первые имена по частоте):')
    cnt = collections.Counter()
    for kk in base:
        nm, r = base[kk]
        if nm and not out[kk][0]:
            v = truth.verdict(nm, scene.get(kk[0], set()))
            cnt[(v, nm, r['line_kev'])] += 1
    for (v, nm, kev), n in sorted(cnt.items(), key=lambda x: -x[1])[:25]:
        print('      %-10s %-14s %9s кэВ  %3d' % (v, nm, kev, n))
    print()
    print('   шесть наследников `I-131` 364.0:')
    for kk in sorted(base):
        nm, r = base[kk]
        if nm == 'I-131':
            print('      %-24s пик %9s промах %6s ПШПВ / %6s кэВ -> %s'
                  % (kk[0], kk[1], r['miss_fwhm'], r['miss_kev'],
                     out[kk][0] or '(нет подписи)'))
    print()
    print('   ПОТЕРИ (ИСТИНА и приборное) — обязано быть пусто:')
    empty = True
    for kk in sorted(base):
        nm, r = base[kk]
        if nm and not out[kk][0]:
            v = truth.verdict(nm, scene.get(kk[0], set()))
            if v in ('ИСТИНА', 'приборное'):
                empty = False
                print('      %-10s %-24s пик %9s -> %-12s %s кэВ'
                      % (v, kk[0], kk[1], nm, r['line_kev']))
    if empty:
        print('      (пусто)')


if __name__ == '__main__':
    main(sys.argv[1])
