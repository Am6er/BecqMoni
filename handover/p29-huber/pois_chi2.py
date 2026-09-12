# -*- coding: utf-8 -*-
"""П29 12.09.2026, `AMBER22` — ПУАССОНОВСКИЙ χ²/n решения каждого плеча ПО ДАМПУ FsaStackShot.

    python handover/p29-huber/pois_chi2.py [плечо ...]

`FsaStackShot` печатает `result.Chi2Ndf` — χ² В ВЕСАХ РЕШАТЕЛЯ (после хуберовских проходов), и между
плечами с разным M это число НЕСРАВНИМО (у huber=0 оно же пуассоновское). Сравнимая метрика —
`Chi2NdfPoisson`, которую проба не печатает; здесь она приближена по дампу: Σ (net − model)² / max(net, 1)
на канал в полосе 15–2800 (и отдельно 200–2800, 15–200). Веса 1/net вместо 1/raw (фон в дампе не
выгружен) — одна и та же мера для всех плеч.
"""
import csv
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def chi(kev, net, model, lo, hi):
    ids = [i for i, e in enumerate(kev) if lo <= e < hi]
    return sum((net[i] - model[i]) ** 2 / max(net[i], 1.0) for i in ids) / len(ids), len(ids)


def main(arms):
    print('%-9s %12s %12s %12s' % ('плечо', 'χ²/n 15-2800', 'χ²/n 200-2800', 'χ²/n 15-200'))
    for a in arms:
        p = os.path.join(HERE, 'amber', 'dump_%s.csv' % a)
        if not os.path.exists(p):
            print('%-9s — нет дампа' % a)
            continue
        rows = list(csv.DictReader(open(p, encoding='utf-8-sig')))
        kev = [float(r['keV']) for r in rows]
        net = [float(r['net']) for r in rows]
        model = [float(r['model']) for r in rows]
        print('%-9s %12.3f %12.3f %12.3f' % (a, chi(kev, net, model, 15, 2800)[0],
                                             chi(kev, net, model, 200, 2800)[0], chi(kev, net, model, 15, 200)[0]))


if __name__ == '__main__':
    main(sys.argv[1:] or ['infer', 'huber05', 'huber1', 'huber2', 'huber4', 'huber5', 'huber10', 'huber20', 'huber0', 'huber100'])
