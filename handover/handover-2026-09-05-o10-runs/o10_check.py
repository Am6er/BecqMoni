# -*- coding: utf-8 -*-
"""Приёмка полосы O10 (`A229`): правка тронула ТОЛЬКО НАДПИСЬ.

    python o10_check.py <labels_before.csv> <labels_after.csv> [<labels_after_ru.csv>]

Три вопроса, и все три считаются, а не осматриваются.

1. ЧТО ВООБЩЕ ИЗМЕНИЛОСЬ В ВЫГРУЗКЕ. Сравнение построчное и поклеточное: у
   правки, которая меняет ИМЯ и ничего больше, разойтись обязана ровно одна
   колонка (`nuclide`) и ровно у тех строк, где стоит подпись суммы. Разойдись
   хоть одна клетка `snr`, `fwhm_ch` или `line_kev` — задето правило, а не
   надпись, и никакие сводные числа этого не покажут.

2. ИСТИНА / ЛОЖЬ ПО КОРПУСНОЙ МЕРКЕ (`c1/truth.py`) — как есть, без поблажек.

3. ИСТИНА / ЛОЖЬ ПО ТОЙ ЖЕ МЕРКЕ, НАУЧЕННОЙ НОВОМУ ИМЕНИ. ⛔ Мерка разбирает
   подпись ПО ИМЕНИ и держит список приборных образов литералами
   (`truth.INSTRUMENTAL = {'Annihilation', ...}`); имени с хвостом в нём нет, и
   четыре подписи суммы уезжают из разряда «приборное» в «ЛОЖЬ» — не потому, что
   изменился отбор, а потому, что мерка не знает нового слова. Здесь тот же
   разбор считается ещё раз со списком, дополненным хвостом.

   ⚠ У научённой мерки ЕСТЬ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: на плече «ДО», где хвоста
   нет ни у одной подписи, она обязана дать РОВНО ТЕ ЖЕ числа, что и корпусная.
   Разойдись они — «поправка» правит не то, что объявлено.
"""
import csv
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_C1 = os.path.normpath(os.path.join(_HERE, '..', '..', 'tools', 'CORPUS', 'scripts', 'c1'))
sys.path.insert(0, _C1)
import truth  # noqa: E402

# Хвост живёт в ресурсах приложения; здесь он выписан ДОСЛОВНО — как и в пробе,
# и по той же причине: спроси мы его у приложения, поправка приняла бы любое имя.
TAILS = (' (sum 511+511)', ' (сумма 511+511)')


def is_instrumental_taught(name):
    """Тот же вопрос, что у `truth.is_instrumental`, но с хвостом подписи суммы."""
    if truth.is_instrumental(name):
        return True
    for tail in TAILS:
        if name.endswith(tail) and truth.is_instrumental(name[:-len(tail)]):
            return True
    return False


def verdict_taught(label, scene):
    if label and is_instrumental_taught(label):
        return 'приборное'
    return truth.verdict(label, scene)


def load(path):
    with open(path, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def counts(rows, scene, fn):
    out = {}
    for r in rows:
        v = fn(r['nuclide'], scene.get(r['spectrum'], set()))
        out[v] = out.get(v, 0) + 1
    return out


def table(title, cb, ca):
    print(title)
    print('%-12s %8s %8s %8s' % ('разряд', 'ДО', 'ПОСЛЕ', 'Δ'))
    for k in ('ИСТИНА', 'ФОН', 'ЛОЖЬ', 'приборное', 'нет подписи'):
        print('%-12s %8d %8d %+8d' % (k, cb.get(k, 0), ca.get(k, 0), ca.get(k, 0) - cb.get(k, 0)))
    print()


def rowdiff(before, after, name_a, name_b):
    print('1. ПОСТРОЧНОЕ СРАВНЕНИЕ ВЫГРУЗОК: %s против %s' % (name_a, name_b))
    if len(before) != len(after):
        print('   ⛔ РАЗНОЕ ЧИСЛО СТРОК: %d против %d — сравнивать поклеточно нельзя'
              % (len(before), len(after)))
        return 1
    cols = list(before[0].keys())
    bad = 0
    diffs = {}
    rows_changed = []
    for i, (b, a) in enumerate(zip(before, after)):
        if b['spectrum'] != a['spectrum'] or b['peak_kev'] != a['peak_kev']:
            print('   ⛔ строки разошлись по самому пику: %s %s против %s %s'
                  % (b['spectrum'], b['peak_kev'], a['spectrum'], a['peak_kev']))
            bad += 1
            continue
        changed = [c for c in cols if b[c] != a[c]]
        if changed:
            rows_changed.append((b['spectrum'], b['peak_kev'], b['nuclide'], a['nuclide'], changed))
            for c in changed:
                diffs[c] = diffs.get(c, 0) + 1
    print('   строк: %d ; изменившихся: %d' % (len(before), len(rows_changed)))
    print('   колонки, в которых есть расхождение: %s'
          % (', '.join('%s=%d' % (c, n) for c, n in sorted(diffs.items())) or '(нет)'))
    for sp, kev, nb, na, changed in rows_changed:
        print('     %-22s пик %9s кэВ : «%s» -> «%s»   [%s]'
              % (sp, kev, nb or '(нет)', na or '(нет)', ','.join(changed)))
    other = [c for c in diffs if c != 'nuclide']
    if other:
        print('   ⛔ ОТКАЗ: разошлись НЕ ТОЛЬКО имена — %s' % ', '.join(sorted(other)))
        bad += 1
    else:
        print('   ✓ разошлась ровно одна колонка — `nuclide`')
    print()
    return bad


def main(pb, pa, pru=None):
    scene = truth.load()
    before, after = load(pb), load(pa)
    bad = rowdiff(before, after, os.path.basename(pb), os.path.basename(pa))

    print('2. КОРПУСНАЯ МЕРКА КАК ЕСТЬ (`c1/truth.py`)')
    cb = counts(before, scene, truth.verdict)
    ca = counts(after, scene, truth.verdict)
    table('', cb, ca)

    print('3. ТА ЖЕ МЕРКА, НАУЧЕННАЯ ХВОСТУ ПОДПИСИ СУММЫ')
    tb = counts(before, scene, verdict_taught)
    ta = counts(after, scene, verdict_taught)
    table('', tb, ta)

    print('   ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ научённой мерки: на плече «ДО» хвоста нет,')
    print('   значит она обязана совпасть с корпусной построчно.')
    if tb == cb:
        print('   ✓ совпала: %s' % ', '.join('%s=%d' % (k, cb[k]) for k in sorted(cb)))
    else:
        print('   ⛔ НЕ СОВПАЛА: %r против %r' % (tb, cb))
        bad += 1
    print()

    if pru:
        ru = load(pru)
        print('4. ВТОРАЯ КУЛЬТУРА: %s' % os.path.basename(pru))
        names_en = sorted({r['nuclide'] for r in after if r['line_kev'] == '1022.000'})
        names_ru = sorted({r['nuclide'] for r in ru if r['line_kev'] == '1022.000'})
        print('   имя подписи суммы, английская культура: %s' % (names_en or '(нет)'))
        print('   имя подписи суммы, русская культура:    %s' % (names_ru or '(нет)'))
        if names_en and names_ru and names_en != names_ru:
            print('   ✓ строки РАЗНЫЕ — имя живёт в ресурсе, а не в коде')
        else:
            print('   ⛔ строки НЕ различаются — имя в ресурсе не живёт')
            bad += 1
        # всё остальное в выгрузках обязано совпадать
        bad += rowdiff(after, ru, os.path.basename(pa), os.path.basename(pru))

    print('ИТОГ: %s' % ('всё сошлось' if bad == 0 else ('ОТКАЗОВ: %d' % bad)))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(*sys.argv[1:]))
