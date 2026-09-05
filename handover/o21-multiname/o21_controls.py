# -*- coding: utf-8 -*-
"""(`S64`, `A227`) ПРИЁМКА ПОЛОСЫ O21 по выгрузкам `LabelTruthProbe`.

    python o21_controls.py <labels_before.csv> <labels_after.csv> <labels_after_ru.csv>

Четыре разбора, каждый — числом:
  1. победитель не сдвинулся ни у одного пика (значит состав не поехал);
  2. подписи со списком длиннее одного: сколько, каков самый длинный;
  3. вид подписи на ОБЕИХ культурах;
  4. плечи положительного контроля (а): америций `S64` и наследники `A227`.
"""
import csv, sys, collections, os
sys.path.insert(0, os.path.abspath('tools/CORPUS/scripts/c1'))
import truth
sys.stdout.reconfigure(encoding='utf-8')


def load(p):
    with open(p, encoding='utf-8-sig', newline='') as fh:
        return {(r['spectrum'], r['peak_kev']): r for r in csv.DictReader(fh)}


def main(pb, pa, pru):
    scene = truth.load()
    b, a, ru = load(pb), load(pa), load(pru)

    print('=== 1. ПОБЕДИТЕЛЬ НЕ СДВИНУЛСЯ (доказательство, что состав не поехал)')
    only_b, only_a = set(b) - set(a), set(a) - set(b)
    diff_name = sum(1 for k in set(b) & set(a) if b[k]['nuclide'] != a[k]['nuclide'])
    diff_line = sum(1 for k in set(b) & set(a) if b[k]['line_kev'] != a[k]['line_kev'])
    print('  пиков ДО %d, ПОСЛЕ %d; пар без соответствия %d' % (len(b), len(a), len(only_b) + len(only_a)))
    print('  имя победителя разошлось у %d пиков, линия победителя — у %d' % (diff_name, diff_line))
    bad_token = sum(1 for k in a if a[k]['nuclide'] and a[k]['label_token'] != a[k]['nuclide'].split(' ')[0])
    print('  лексема надписи (`NuclideNameOf`) разошлась с лексемой победителя у %d подписей' % bad_token)
    comp = collections.Counter(r['label_token'] for r in a.values() if r['nuclide'])
    compb = collections.Counter(r['nuclide'].split(' ')[0] for r in b.values() if r['nuclide'])
    print('  состав по лексемам: имён ДО %d, ПОСЛЕ %d; расхождений по счёту %d'
          % (len(compb), len(comp), sum(1 for n in set(compb) | set(comp) if compb[n] != comp[n])))
    print()

    print('=== 2. СПИСКИ ДЛИННЕЕ ОДНОГО')
    lens = collections.Counter(len(r['candidates'].split('|')) for r in a.values() if r['nuclide'])
    tot = sum(lens.values())
    for n in sorted(lens):
        print('  имён %d: %5d подписей (%.1f %%)' % (n, lens[n], 100.0 * lens[n] / tot))
    print('  ИТОГО подписей %d, со списком длиннее одного %d (%.1f %%)'
          % (tot, tot - lens[1], 100.0 * (tot - lens[1]) / tot))
    # что за имена приписались истинным подписям
    dirty = sum(1 for k, r in a.items()
                if r['nuclide'] and truth.verdict(r['nuclide'], scene.get(k[0], set())) == 'ИСТИНА'
                and any(truth.verdict(n, scene.get(k[0], set())) == 'ЛОЖЬ'
                        for n in r['candidates'].split('|')[1:]))
    saved = sum(1 for k, r in a.items()
                if r['nuclide'] and truth.verdict(r['nuclide'], scene.get(k[0], set())) == 'ЛОЖЬ'
                and any(truth.verdict(n, scene.get(k[0], set())) == 'ИСТИНА'
                        for n in r['candidates'].split('|')[1:]))
    print('  ложных подписей, получивших рядом ВЕРНОЕ имя: %d' % saved)
    print('  истинных подписей, получивших рядом ЛОЖНОЕ имя: %d' % dirty)
    print()

    print('=== 3. ВИД ПОДПИСИ НА ОБЕИХ КУЛЬТУРАХ')
    diff = [k for k in a if a[k]['label_text'] != ru[k]['label_text']]
    print('  подписей, различающихся текстом между «» (английский) и «ru-RU»: %d' % len(diff))
    for k in sorted(diff)[:6]:
        print('     %-22s %9s  en «%s»   ru «%s»' % (k[0], k[1], a[k]['label_text'], ru[k]['label_text']))
    print('  примеры списков (первые шесть, длина > 1):')
    shown = 0
    for k in sorted(a):
        r = a[k]
        if not r['nuclide'] or len(r['candidates'].split('|')) < 2:
            continue
        print('     %-22s пик %9s  en «%s»   ru «%s»' % (k[0], k[1], r['label_text'], ru[k]['label_text']))
        shown += 1
        if shown >= 6:
            break
    print()

    print('=== 4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (а): ПИК, ГДЕ СПОР НЕ РЕШАЕТСЯ ПОЛОЖЕНИЕМ')
    print('  (`S64`, случай «а») америций против K-рентгена вольфрама:')
    n = m = 0
    for k in sorted(a):
        r = a[k]
        if 'Am241' not in k[0] or not r['nuclide'] or float(r['peak_kev']) > 80:
            continue
        n += 1
        if len(r['candidates'].split('|')) > 1:
            m += 1
        print('     %-22s пик %9s -> «%s»' % (k[0], k[1], r['label_text']))
    print('     ИТОГО %d подписей, со списком длиннее одного %d' % (n, m))
    print()
    for nm, kev in (('U-235', '145.000'), ('I-131', '364.000')):
        print('  (`A227`) наследники снятых плутониевых — %s %s:' % (nm, kev))
        n = m = t = 0
        for k in sorted(a):
            r = a[k]
            if r['nuclide'] != nm or r['line_kev'] != kev:
                continue
            n += 1
            names = r['candidates'].split('|')
            if len(names) > 1:
                m += 1
            v = truth.verdict(r['nuclide'], scene.get(k[0], set()))
            if v == 'ЛОЖЬ':
                t += 1
            print('     %-22s пик %9s [%s] -> «%s»' % (k[0], k[1], v, r['label_text']))
        print('     ИТОГО %d подписей (из них ЛОЖЬ %d), со списком длиннее одного %d' % (n, t, m))
        print()


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], sys.argv[3])
