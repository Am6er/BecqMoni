# -*- coding: utf-8 -*-
"""`A227`: СИЛА УЛИКИ — развёртка до правки приложения (полоса F24).

    python f24_cand.py <labels_after.csv>

Полоса O1 сделала правило «слабая линия подтверждается ДРУГОЙ своей линией»
(`A197`), и улика там — ПИК ВОЗЛЕ ЛИНИИ. На разрешении сцинтиллятора такая
улика слаба: окно 0.5 ПШПВ — это ±12…18 кэВ, и подтверждающим оказывается
чужой пик. Измерено здесь же: `I-131` 722.0 «подтверждается» пиком Bi-212
727.3 в спектрах тория.

Мерятся три силы улики и три условия запуска правила:

  улика   peak  — пик возле линии (нынешняя, `A197`);
          cand  — пик, спор за который наш нуклид НЕ ПРОИГРЫВАЕТ по положению
                  (имя стоит в списке кандидатов того пика, `S64`/O21);
          label — пик, ПОДПИСАННЫЙ этим именем (самая строгая, O1 её отверг).

  запуск  выход < 5 %             — нынешний (`A197`);
          + промах > T·ПШПВ       — «далёкая линия одна за себя не отвечает»;
          все                     — судить любую подпись с проставленным выходом.
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


def confirmed(row, peaks, lib, k_conf, mode):
    """True принять, False снять, None правило неприменимо (других линий нет)."""
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
            if p is row:
                continue
            f = sw.num(p, 'fwhm_kev')
            if not f or f <= 0:
                continue
            if abs(sw.num(p, 'peak_kev') - e) > k_conf * f:
                continue
            if mode == 'peak':
                return True
            if mode == 'label' and p['nuclide'] == name:
                return True
            if mode == 'cand' and name in cand_names(p):
                return True
    return False


def apply_rule(per, lib, k_conf, mode, theta_yield, theta_miss):
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
                out[key] = (r['nuclide'], r)       # выход не проставлен — не судится
                continue
            by_yield = inten < theta_yield
            by_miss = theta_miss is not None and missf is not None and missf > theta_miss
            if by_yield or by_miss:
                if confirmed(r, peaks, lib, k_conf, mode) is False:
                    out[key] = ('', r)
                    continue
            out[key] = (r['nuclide'], r)
    return out


def heirs_removed(base, out):
    kept = removed = 0
    for kk in base:
        nm, r = base[kk]
        if nm != 'I-131' or abs(float(r['line_kev']) - 364.0) > 0.5:
            continue
        if out[kk][0]:
            kept += 1
        else:
            removed += 1
    return removed, kept


def report(per, lib, scene, base, title, k, mode, ty, tm):
    out = apply_rule(per, lib, k, mode, ty, tm)
    ca = sw.score(out, scene)
    lost = sum(1 for kk in base if base[kk][0] and not out[kk][0])
    rem, kept = heirs_removed(base, out)
    ist = sum(1 for kk in base if base[kk][0] and not out[kk][0]
              and truth.verdict(base[kk][0], scene.get(kk[0], set())) == 'ИСТИНА')
    pr = sum(1 for kk in base if base[kk][0] and not out[kk][0]
             and truth.verdict(base[kk][0], scene.get(kk[0], set())) == 'приборное')
    print('%-46s %7d %6d %6d %7d %6d %6d %6d   %d/6'
          % (title, ca['ИСТИНА'], ca['ФОН'], ca['ЛОЖЬ'], ca['приборное'], lost, ist, pr, rem))
    return out


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
    hdr = ('%-46s %7s %6s %6s %7s %6s %6s %6s   %s'
           % ('вариант', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято', 'ист.-', 'приб.-', 'I-131'))
    print(hdr)
    print('-' * len(hdr))
    # контроль модели: нынешнее правило обязано дать базу
    report(per, lib, scene, base, 'КОНТРОЛЬ: нынешнее (peak, 0.5, выход<5 %)',
           0.5, 'peak', 5.0, None)
    print()
    for mode in ('peak', 'cand', 'label'):
        for k in (0.25, 0.5, 0.75):
            report(per, lib, scene, base,
                   'улика %-5s окно %.2f, запуск выход<5 %%' % (mode, k), k, mode, 5.0, None)
        print()
    for mode in ('cand', 'label'):
        for k in (0.5,):
            for tm in (0.50, 0.40, 0.30, 0.25, 0.20, 0.15, 0.10):
                report(per, lib, scene, base,
                       'улика %-5s окно %.2f, + промах > %.2f' % (mode, k, tm),
                       k, mode, 5.0, tm)
            print()
    for mode in ('peak', 'cand', 'label'):
        for k in (0.25, 0.5):
            report(per, lib, scene, base,
                   'улика %-5s окно %.2f, СУДИТЬ ВСЕХ' % (mode, k), k, mode, 1e9, None)
        print()


if __name__ == '__main__':
    main(sys.argv[1])
