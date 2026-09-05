# -*- coding: utf-8 -*-
"""Развёртка правила «подтверждение составом» ДО правки приложения (полоса O1, `A197`).

    python o1_rule_sweep.py <labels_before.csv>

Считает по выгрузке `LabelTruthProbe`, что сделало бы с подписями правило
«победитель принимается, только если в спектре есть ещё хотя бы одна его
СОБСТВЕННАЯ линия». Разбор ровно тот же, что у `label_score.py` (`truth.py`),
чтобы числа были сравнимы; библиотека читается из поставочной
`BecquerelMonitor\\config\\NuclideDefinition.xml`.

⚠ Это ПРИКИДКА, а не приёмка: приёмка — прогон пробы на сборке с правкой.
Здесь мерятся варианты правила, чтобы не платить пересборкой за каждый.
"""
import csv, sys, os, collections
import xml.etree.ElementTree as ET

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.normpath(os.path.join(_HERE, '..', '..'))
sys.path.insert(0, os.path.join(_REPO, 'tools', 'CORPUS', 'scripts', 'c1'))
import truth

LIB = os.path.join(_REPO, 'BecquerelMonitor', 'config', 'NuclideDefinition.xml')
YIELD_GATE = 0.1        # PeakDetector.MinimumLabelYieldPercent
MISS_FWHM = 1.5         # PeakDetector.MaximumLabelMissInFwhm


def load_library():
    """имя -> список (энергия, выход). Только видимые и допустимые по выходу."""
    out = collections.defaultdict(list)
    for nd in ET.parse(LIB).getroot().iter('Nuclide'):
        name = (nd.findtext('Name') or '').strip()
        vis = (nd.findtext('Visible') or 'false').strip().lower() == 'true'
        e = float(nd.findtext('Energy') or 0)
        it = float(nd.findtext('Intencity') or 0)
        if not name or not vis or e == 0.0:
            continue
        if it > 0.0 and it < YIELD_GATE:
            continue
        out[name].append((e, it))
    return out


def load_peaks(path):
    per = collections.OrderedDict()
    with open(path, encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            per.setdefault(r['spectrum'], []).append(r)
    return per


def num(r, k):
    v = r.get(k)
    return float(v) if v not in (None, '') else None


def confirmed(row, peaks, lib, k_conf, mode):
    """Есть ли у победителя ЕЩЁ ОДНА своя линия, видимая в этом спектре.

    Возврат: True — принять, False — снять, None — правило неприменимо
    (других своих линий в полосе прибора нет вовсе).
    """
    name = row['nuclide']
    own = lib.get(name, [])
    line = num(row, 'line_kev')
    rmin, rmax = num(row, 'range_min_kev'), num(row, 'range_max_kev')
    others = [e for (e, i) in own
              if abs(e - line) > 1e-9 and rmin is not None and rmin <= e <= rmax]
    if not others:
        return None
    for e in others:
        for p in peaks:
            if p is row:
                continue
            f = num(p, 'fwhm_kev')
            if not f or f <= 0:
                continue
            if abs(num(p, 'peak_kev') - e) <= k_conf * f:
                if mode == 'peak':
                    return True
                if mode == 'label' and p['nuclide'] == name:
                    return True
    return False


def is_top_line(row, lib):
    """Победитель — САМАЯ ЯРКАЯ своя линия в полосе прибора?"""
    own = lib.get(row['nuclide'], [])
    line, rmin, rmax = num(row, 'line_kev'), num(row, 'range_min_kev'), num(row, 'range_max_kev')
    inr = [(e, i) for (e, i) in own if rmin is not None and rmin <= e <= rmax]
    if not inr:
        return True
    top = max(i for (e, i) in inr)
    mine = max([i for (e, i) in inr if abs(e - line) < 1e-9] or [0.0])
    return mine >= top - 1e-12


def apply_rule(per, lib, k_conf, mode, theta, exempt_top=False):
    """Возвращает новый разряд каждой строки."""
    out = {}
    for spec, peaks in per.items():
        for r in peaks:
            key = (spec, r['peak_kev'])
            if not r['nuclide']:
                out[key] = ('', r)
                continue
            inten = num(r, 'intensity_pct')
            # выход НЕ ПРОСТАВЛЕН (0) — приборный образ, правилу не подлежит
            if exempt_top and is_top_line(r, lib):
                out[key] = (r['nuclide'], r)
                continue
            if inten is not None and inten > 0.0 and inten < theta:
                c = confirmed(r, peaks, lib, k_conf, mode)
                if c is False:
                    out[key] = ('', r)
                    continue
            out[key] = (r['nuclide'], r)
    return out


def score(out, scene):
    c = collections.Counter()
    for (spec, _), (name, r) in out.items():
        c[truth.verdict(name, scene.get(spec, set()))] += 1
    return c


def main(path):
    lib = load_library()
    per = load_peaks(path)
    scene = truth.load()
    base = {}
    for spec, peaks in per.items():
        for r in peaks:
            base[(spec, r['peak_kev'])] = (r['nuclide'], r)
    cb = score(base, scene)
    print('БАЗА: ИСТИНА %d  ФОН %d  ЛОЖЬ %d  приборное %d  нет подписи %d'
          % (cb['ИСТИНА'], cb['ФОН'], cb['ЛОЖЬ'], cb['приборное'], cb['нет подписи']))
    print()
    print('%-6s %-6s %-8s %8s %8s %8s %8s %8s'
          % ('окно', 'по', 'порог %', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято'))
    for mode in ('peak', 'label'):
        for k in (0.25, 0.5, 0.75, 1.0, 1.5):
            for theta in (1e9, 5.0, 2.0, 1.0):
                out = apply_rule(per, lib, k, mode, theta)
                ca = score(out, scene)
                lost = sum(1 for kk in base
                           if base[kk][0] and not out[kk][0])
                print('%-6.2f %-6s %-8s %8d %8d %8d %8d %8d'
                      % (k, mode, ('все' if theta > 1e8 else '%g' % theta),
                         ca['ИСТИНА'], ca['ФОН'], ca['ЛОЖЬ'], ca['приборное'], lost))
    print()
    print('ТОНКАЯ РАЗВЁРТКА ПО ПОРОГУ ВЫХОДА (окно по пикам):')
    print('%-6s %-8s %8s %8s %8s %8s %8s'
          % ('окно', 'порог %', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято'))
    for k in (0.5, 0.75):
        for theta in (1.0, 2.0, 3.0, 5.0, 8.0, 10.0, 15.0, 20.0, 30.0):
            out = apply_rule(per, lib, k, 'peak', theta)
            ca = score(out, scene)
            lost = sum(1 for kk in base if base[kk][0] and not out[kk][0])
            print('%-6.2f %-8g %8d %8d %8d %8d %8d'
                  % (k, theta, ca['ИСТИНА'], ca['ФОН'], ca['ЛОЖЬ'], ca['приборное'], lost))

    print()
    print('ВАРИАНТ «САМАЯ ЯРКАЯ СВОЯ ЛИНИЯ НЕ СУДИТСЯ» (без порога и с ним):')
    print('%-6s %-8s %8s %8s %8s %8s %8s'
          % ('окно', 'порог %', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн', 'снято'))
    for k in (0.5, 0.75):
        for theta in (1e9, 20.0, 10.0, 5.0):
            out = apply_rule(per, lib, k, 'peak', theta, exempt_top=True)
            ca = score(out, scene)
            lost = sum(1 for kk in base if base[kk][0] and not out[kk][0])
            print('%-6.2f %-8s %8d %8d %8d %8d %8d'
                  % (k, ('все' if theta > 1e8 else '%g' % theta),
                     ca['ИСТИНА'], ca['ФОН'], ca['ЛОЖЬ'], ca['приборное'], lost))

    for k, theta in ((0.5, 5.0), (0.5, 8.0)):
        print()
        print('СНЯТОЕ ПОИМЁННО, окно %.2f ПШПВ, порог %g %%:' % (k, theta))
        out = apply_rule(per, lib, k, 'peak', theta)
        cnt = collections.Counter()
        for kk in sorted(base):
            nm, r = base[kk]
            if nm and not out[kk][0]:
                v = truth.verdict(nm, scene.get(kk[0], set()))
                cnt[(v, nm, r['line_kev'], r['intensity_pct'])] += 1
        for (v, nm, kev, i), n in sorted(cnt.items(), key=lambda x: -x[1]):
            print('   %-10s %-14s %9s кэВ I=%-9s %3d' % (v, nm, kev, i, n))
        print('   --- ИСТИНА, потерянная поимённо ---')
        for kk in sorted(base):
            nm, r = base[kk]
            if nm and not out[kk][0] and truth.verdict(nm, scene.get(kk[0], set())) == 'ИСТИНА':
                print('   %-24s %9s кэВ -> %-14s %9s кэВ I=%s'
                      % (kk[0], kk[1], nm, r['line_kev'], r['intensity_pct']))

    print()
    print('`Pa-234m` 1001.0 — где стоит и что с ним делает правило (окно 0.5, порог 5 %):')
    out = apply_rule(per, lib, 0.5, 'peak', 5.0)
    for kk in sorted(base):
        nm, r = base[kk]
        if nm == 'Pa-234m' and abs(float(r['line_kev']) - 1001.0) < 0.5:
            uran = 'Pa-234m' in scene.get(kk[0], set())
            print('   %-26s пик %9s  уран объявлен: %-3s  после правила: %s'
                  % (kk[0], kk[1], 'да' if uran else 'НЕТ',
                     out[kk][0] or '(нет подписи)'))


if __name__ == '__main__':
    main(sys.argv[1])
