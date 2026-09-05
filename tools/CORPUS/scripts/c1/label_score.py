# -*- coding: utf-8 -*-
"""Разбор выгрузки `LabelTruthProbe`: что стало с подписями (полоса C1, `S134`).

    python label_score.py <labels_before.csv> <labels_after.csv>

Считает по КАЖДОМУ пику, не по спектру, и делит подписи на четыре разряда
(`truth.py`): ИСТИНА — нуклид объявлен в сцене; ФОН — только природный ряд;
ЛОЖЬ — ни там, ни там; приборное — рентген, вылет, аннигиляция.
"""
import csv, sys, collections, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import truth


def load(p):
    with open(p, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def key(r):
    return (r['spectrum'], r['peak_kev'])


def main(pb, pa):
    scene = truth.load()
    before, after = load(pb), load(pa)
    bi = {key(r): r for r in before}
    ai = {key(r): r for r in after}
    only_b, only_a = set(bi) - set(ai), set(ai) - set(bi)
    if only_b or only_a:
        # Пик может ИСЧЕЗНУТЬ или ПОЯВИТЬСЯ не от подписи, а от `isNewPeak`:
        # два неразрешимых пика с ОДНОЙ линией схлопываются в один, и какой
        # именно останется, зависит от того, подписались ли они вообще.
        # Такие пики из попарного сравнения выносятся ВСЛУХ, а не молча.
        print('⚠ пики без пары: только ДО %d, только ПОСЛЕ %d' % (len(only_b), len(only_a)))
        for k in sorted(only_b):
            print('     только ДО:    %s %s -> %s' % (k[0], k[1], bi[k]['nuclide'] or '(нет)'))
        for k in sorted(only_a):
            print('     только ПОСЛЕ: %s %s -> %s' % (k[0], k[1], ai[k]['nuclide'] or '(нет)'))
        print()
    paired = set(bi) & set(ai)

    def v(r):
        return truth.verdict(r['nuclide'], scene.get(r['spectrum'], set()))

    print('пиков: %d ; с подписью ДО %d, ПОСЛЕ %d'
          % (len(bi), sum(1 for r in before if r['nuclide']),
             sum(1 for r in after if r['nuclide'])))
    print()
    print('%-12s %8s %8s %8s' % ('разряд', 'ДО', 'ПОСЛЕ', 'Δ'))
    cb = collections.Counter(v(r) for r in before)
    ca = collections.Counter(v(r) for r in after)
    for k in ('ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборное', 'нет подписи'):
        print('%-12s %8d %8d %+8d' % (k, cb[k], ca[k], ca[k] - cb[k]))

    print()
    print('ПЕРЕХОДЫ (только там, где подпись изменилась):')
    moves = collections.Counter()
    for k in paired:
        b, a = bi[k], ai[k]
        if b['nuclide'] != a['nuclide']:
            moves[(v(b), v(a))] += 1
    for (f, t), n in moves.most_common():
        print('   %-12s -> %-12s %4d' % (f, t, n))

    print()
    print('СНЯТЫЕ ПОДПИСИ по линии (ДО -> нет подписи или другая):')
    lost = collections.Counter()
    for k in paired:
        b, a = bi[k], ai[k]
        if b['nuclide'] and b['nuclide'] != a['nuclide']:
            lost[(v(b), b['nuclide'], b['line_kev'], b['intensity_pct'])] += 1
    for (cls, nm, kev, I), n in sorted(lost.items(), key=lambda x: -x[1]):
        print('   %-10s %-14s %9s кэВ  I=%-8s  %3d' % (cls, nm, kev, I, n))

    print()
    print('НОВЫЕ ПОДПИСИ (не было -> стала):')
    new = collections.Counter()
    for k in paired:
        b, a = bi[k], ai[k]
        if a['nuclide'] and a['nuclide'] != b['nuclide']:
            new[(v(a), a['nuclide'], a['line_kev'])] += 1
    if not new:
        print('   нет')
    for (cls, nm, kev), n in sorted(new.items(), key=lambda x: -x[1]):
        print('   %-10s %-14s %9s кэВ %3d' % (cls, nm, kev, n))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
