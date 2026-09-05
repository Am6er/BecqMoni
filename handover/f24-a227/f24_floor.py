# -*- coding: utf-8 -*-
"""`A227`: правило промаха с АБСОЛЮТНЫМ ПОЛОМ и внутрипиковой уликой (F24).

    python f24_floor.py <labels_after.csv>

Две поправки к простому «промах > T·ПШПВ», обе выведены числом (журнал F24 §2):

  * **пол по абсолютному промаху.** 74 % записей поставочной библиотеки несут
    ЦЕЛУЮ энергию (353.0 вместо 351.93, 911.0, 345.0): энергии огрублены до
    кэВ. У германия ПШПВ 0.76…1.5 кэВ, поэтому «промах в ПШПВ» там мерит
    ОКРУГЛЕНИЕ БИБЛИОТЕКИ, а не положение пика, и правило по промаху рубит
    законные германиевые подписи. Пол в кэВ снимает ровно это;

  * **улика внутри того же пика** (`A226` п.4): у сцинтиллятора Kalpha и Kbeta
    рентгена сливаются в ОДИН пик, и «другого пика» у более яркой линии не
    бывает по построению — 70…90 приборных подписей.
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


def apply_rule(per, lib, k_conf, mode, theta_yield, theta_miss, floor_kev, self_pair):
    out = {}
    for spec_name, peaks in per.items():
        for r in peaks:
            key = (spec_name, r['peak_kev'])
            if not r['nuclide']:
                out[key] = ('', r)
                continue
            inten = sw.num(r, 'intensity_pct')
            missf = sw.num(r, 'miss_fwhm')
            missk = sw.num(r, 'miss_kev')
            if not (inten is not None and inten > 0.0):
                out[key] = (r['nuclide'], r)
                continue
            by_yield = inten < theta_yield
            by_miss = (theta_miss is not None and missf is not None and missk is not None
                       and missf > theta_miss and missk > floor_kev)
            if by_yield or by_miss:
                if confirmed(r, peaks, lib, k_conf, mode, self_pair) is False:
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


def line(title, ca, lost, ist, pr, rem):
    print('%-54s %7d %6d %6d %7d %6d %6d %6d   %d/6'
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
    hdr = ('%-54s %7s %6s %6s %7s %6s %6s %6s   %s'
           % ('вариант', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято', 'ист.-', 'приб.-', 'I-131'))
    print(hdr)
    print('-' * len(hdr))
    out = apply_rule(per, lib, 0.5, 'peak', 5.0, None, 0.0, False)
    line('КОНТРОЛЬ: нынешнее правило (обязано дать базу)', *counts(base, out, scene))
    print('   подписи совпали с базой: %s'
          % ('ДА' if all(out[k][0] == base[k][0] for k in base) else '⛔ НЕТ'))
    print()
    for mode in ('peak', 'cand'):
        for k in (0.50, 0.75):
            for floor in (0.0, 1.5, 3.0):
                for tm in (0.30, 0.20, 0.15, 0.10):
                    out = apply_rule(per, lib, k, mode, 5.0, tm, floor, True)
                    line('улика %-4s окно %.2f  пол %3.1f кэВ  промах > %.2f'
                         % (mode, k, floor, tm), *counts(base, out, scene))
            print()
    print()
    for mode, k, floor, tm in (('cand', 0.75, 1.5, 0.15), ('cand', 0.75, 1.5, 0.10),
                               ('peak', 0.75, 1.5, 0.10), ('cand', 0.50, 1.5, 0.15)):
        out = apply_rule(per, lib, k, mode, 5.0, tm, floor, True)
        ca, lost, ist, pr, rem = counts(base, out, scene)
        print('ЦЕНА: улика %s, окно %.2f, пол %.1f кэВ, промах > %.2f — '
              'ИСТИНА %d, приборн %d, снято %d, I-131 %d/6'
              % (mode, k, floor, tm, ca['ИСТИНА'], ca['приборное'], lost, rem))
        for kk in sorted(base):
            nm, r = base[kk]
            if nm and not out[kk][0]:
                v = truth.verdict(nm, scene.get(kk[0], set()))
                if v in ('ИСТИНА', 'приборное'):
                    print('   %-10s %-26s пик %9s -> %-12s %9s кэВ I=%-8s промах %s ПШПВ / %s кэВ'
                          % (v, kk[0], kk[1], nm, r['line_kev'], r['intensity_pct'],
                             r['miss_fwhm'], r['miss_kev']))
        print()


if __name__ == '__main__':
    main(sys.argv[1])
