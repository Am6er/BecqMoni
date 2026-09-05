# -*- coding: utf-8 -*-
"""Развёртка ИСТОЧНИКА ИЗБЫТОЧНОСТИ для `A227` — ДО правки приложения (полоса F24).

    python f24_sweep.py <labels_after.csv>

`A227`: наследники снятых плутониевых подписей. После поправки посылки (O21)
ложных наследников на корпусе ШЕСТЬ, и все шесть — `I-131` 364.0 (выход 81.5 %,
самая яркая своя линия). Ни порог по выходу (`A197`, 5 %), ни признак «не самая
яркая» (`A226`), ни список кандидатов (`S64`, O21) их не берут — измерено.

Здесь мерится ИНОЙ признак: **промах победителя от пика**. Линия, попавшая в
пик краем окна подписи, — слабая улика сама по себе, и яркость её не
подтверждает. Правило: подпись судится (требует ВТОРОЙ своей линии, видимой в
спектре), если выход ниже порога ЛИБО промах больше T·ПШПВ пика.

⚠ ПРИКИДКА, а не приёмка: приёмка — прогон `LabelTruthProbe` на сборке с
правкой. Здесь мерятся варианты, чтобы не платить пересборкой за каждый.
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


def apply_miss_rule(per, lib, k_conf, theta_yield, theta_miss, judge_no_yield=False):
    """Новый разряд каждой строки.

    theta_yield — порог по выходу (нынешние 5 %); theta_miss — порог по промаху
    в ПШПВ пика (None = признак выключен, то есть нынешнее правило `A197`);
    judge_no_yield — судить ли записи БЕЗ проставленного выхода (приборные
    образы); по умолчанию нет, как и в приложении.
    """
    out = {}
    for spec_name, peaks in per.items():
        for r in peaks:
            key = (spec_name, r['peak_kev'])
            if not r['nuclide']:
                out[key] = ('', r)
                continue
            inten = sw.num(r, 'intensity_pct')
            missf = sw.num(r, 'miss_fwhm')
            has_yield = inten is not None and inten > 0.0
            if not has_yield and not judge_no_yield:
                out[key] = (r['nuclide'], r)
                continue
            by_yield = has_yield and inten < theta_yield
            by_miss = (theta_miss is not None and missf is not None
                       and missf > theta_miss)
            if by_yield or by_miss:
                if sw.confirmed(r, peaks, lib, k_conf, 'peak') is False:
                    out[key] = ('', r)
                    continue
            out[key] = (r['nuclide'], r)
    return out


def heirs(base, out, scene, name, line):
    """Сколько наследников этого имени/линии осталось и сколько снято."""
    kept = removed = 0
    for kk in base:
        nm, r = base[kk]
        if nm != name:
            continue
        if abs(float(r['line_kev']) - line) > 0.5:
            continue
        if out[kk][0]:
            kept += 1
        else:
            removed += 1
    return kept, removed


def losses(base, out, scene, verdicts=('ИСТИНА',)):
    rows = []
    for kk in sorted(base):
        nm, r = base[kk]
        if nm and not out[kk][0]:
            v = truth.verdict(nm, scene.get(kk[0], set()))
            if v in verdicts:
                rows.append((v, kk[0], kk[1], nm, r['line_kev'], r['intensity_pct'],
                             r['miss_fwhm']))
    return rows


def main(path):
    lib = sw.load_library()
    per = sw.load_peaks(path)
    scene = truth.load()
    base = {}
    for spec_name, peaks in per.items():
        for r in peaks:
            base[(spec_name, r['peak_kev'])] = (r['nuclide'], r)
    cb = sw.score(base, scene)
    print('БАЗА (дерево на 05.09.2026, после `A196`/`A197`/`S64`):')
    print('  ИСТИНА %d  ФОН %d  ЛОЖЬ %d  приборное %d  нет подписи %d  всего %d'
          % (cb['ИСТИНА'], cb['ФОН'], cb['ЛОЖЬ'], cb['приборное'], cb['нет подписи'],
             sum(cb.values())))
    print()

    print('ПРОВЕРКА МОДЕЛИ: T_miss выключен, порог 5 %% — обязано совпасть с базой')
    out0 = apply_miss_rule(per, lib, 0.5, 5.0, None)
    c0 = sw.score(out0, scene)
    same = all(out0[k][0] == base[k][0] for k in base)
    print('  ИСТИНА %d  ФОН %d  ЛОЖЬ %d  приборн %d   подписи совпали: %s'
          % (c0['ИСТИНА'], c0['ФОН'], c0['ЛОЖЬ'], c0['приборное'],
             'ДА' if same else '⛔ НЕТ'))
    print()

    print('РАЗВЁРТКА ПО ПРОМАХУ (окно подтверждения 0.5 ПШПВ, порог по выходу 5 %):')
    print('%-10s %8s %8s %8s %8s %8s   %s'
          % ('T промах', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято', 'I-131 364 снято/оставлено'))
    for theta in (None, 1.00, 0.75, 0.60, 0.50, 0.40, 0.30, 0.25, 0.20, 0.15, 0.10, 0.05):
        out = apply_miss_rule(per, lib, 0.5, 5.0, theta)
        ca = sw.score(out, scene)
        lost = sum(1 for kk in base if base[kk][0] and not out[kk][0])
        kept, removed = heirs(base, out, scene, 'I-131', 364.0)
        print('%-10s %8d %8d %8d %8d %8d   %d / %d'
              % ('выкл' if theta is None else '%.2f' % theta,
                 ca['ИСТИНА'], ca['ФОН'], ca['ЛОЖЬ'], ca['приборное'], lost,
                 removed, kept))
    print()

    print('ТО ЖЕ, но окно подтверждения 0.75 ПШПВ:')
    print('%-10s %8s %8s %8s %8s %8s   %s'
          % ('T промах', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято', 'I-131 364 снято/оставлено'))
    for theta in (0.50, 0.40, 0.30, 0.25, 0.20, 0.15):
        out = apply_miss_rule(per, lib, 0.75, 5.0, theta)
        ca = sw.score(out, scene)
        lost = sum(1 for kk in base if base[kk][0] and not out[kk][0])
        kept, removed = heirs(base, out, scene, 'I-131', 364.0)
        print('%-10s %8d %8d %8d %8d %8d   %d / %d'
              % ('%.2f' % theta, ca['ИСТИНА'], ca['ФОН'], ca['ЛОЖЬ'], ca['приборное'],
                 lost, removed, kept))
    print()

    for theta in (0.30, 0.25, 0.20, 0.15):
        out = apply_miss_rule(per, lib, 0.5, 5.0, theta)
        rows = losses(base, out, scene)
        print('ЦЕНА T=%.2f: потеряно ИСТИННЫХ подписей %d' % (theta, len(rows)))
        for v, sp, pk, nm, kev, it, mf in rows:
            print('   %-26s пик %9s -> %-12s %9s кэВ I=%-8s промах %s ПШПВ'
                  % (sp, pk, nm, kev, it, mf))
        prib = sum(1 for kk in base if base[kk][0] and not out[kk][0]
                   and truth.verdict(base[kk][0], scene.get(kk[0], set())) == 'приборное')
        print('   приборных снято: %d' % prib)
        print()

    print('ШЕСТЬ НАСЛЕДНИКОВ `I-131` 364.0 — что с ними делает T=0.20:')
    out = apply_miss_rule(per, lib, 0.5, 5.0, 0.20)
    for kk in sorted(base):
        nm, r = base[kk]
        if nm == 'I-131':
            print('   %-24s пик %9s промах %6s ПШПВ  ->  %s'
                  % (kk[0], kk[1], r['miss_fwhm'], out[kk][0] or '(нет подписи)'))


if __name__ == '__main__':
    main(sys.argv[1])
