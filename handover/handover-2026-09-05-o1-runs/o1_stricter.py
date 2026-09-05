# -*- coding: utf-8 -*-
"""Цена СТРОЖЕ судящего варианта (полоса O1, §4 журнала).

    python o1_stricter.py <labels_before.csv>

Вариант: линия, НЕ САМАЯ ЯРКАЯ у своего нуклида в полосе прибора, обязана
подтверждаться независимо от выхода. Печатает цену поимённо.
"""
import importlib.util, os, sys
_HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location('sw', os.path.join(_HERE, 'o1_rule_sweep.py'))
sw = importlib.util.module_from_spec(spec); spec.loader.exec_module(sw)

lib = sw.load_library(); per = sw.load_peaks(sys.argv[1]); scene = sw.truth.load()
base = {}
for s, ps in per.items():
    for r in ps:
        base[(s, r['peak_kev'])] = (r['nuclide'], r)
print('%-40s %8s %8s %8s %8s' % ('вариант', 'ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборн'))
for title, top, th in (('принятый: порог 5 %', False, 5.0),
                       ('не самая яркая судится, порог 15 %', True, 15.0),
                       ('не самая яркая судится, без порога', True, 1e9)):
    out = sw.apply_rule(per, lib, 0.5, 'peak', th, exempt_top=top)
    c = sw.score(out, scene)
    print('%-40s %8d %8d %8d %8d' % (title, c['ИСТИНА'], c['ФОН'], c['ЛОЖЬ'], c['приборное']))
print()
print('ЦЕНА варианта «без порога» поимённо:')
out = sw.apply_rule(per, lib, 0.5, 'peak', 1e9, exempt_top=True)
for kk in sorted(base):
    nm, r = base[kk]
    if nm and not out[kk][0]:
        v = sw.truth.verdict(nm, scene.get(kk[0], set()))
        if v in ('ИСТИНА',):
            print('   %-10s %-24s пик %9s -> %-10s %9s кэВ I=%s'
                  % (v, kk[0], kk[1], nm, r['line_kev'], r['intensity_pct']))
n = sum(1 for kk in base if base[kk][0] and not out[kk][0]
        and sw.truth.verdict(base[kk][0], scene.get(kk[0], set())) == 'приборное')
print('   приборных снято: %d' % n)
