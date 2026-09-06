# -*- coding: utf-8 -*-
u"""ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ №2 к `A261`: под `en-US` поведение полей со
стрелками не изменилось НИ НА ЗНАК.

Сличаются таблицы двух прогонов `NumericCultureProbeF68` — «до» и «после»
правки, — строка к строке, по ключу «сцена + порядковый номер поля внутри
сцены». Судятся ВСЕ тринадцать столбцов; ожидаемых расхождений ровно два рода,
и оба названы здесь заранее, чтобы «ожидаемое» не подгонялось под вышедшее:

  1. столбец «тип» — это и есть правка (`NumericUpDown` → `InvariantNumericUpDown`);
  2. одно поле `EfficiencyMakerForm` теряет группировку разрядов (`A244`).

Любое расхождение сверх этих двух — регресс.

  python handover/f68-a261/compare_en.py <до.txt> <после.txt>
"""
import io, sys
try: sys.stdout.reconfigure(encoding='utf-8')
except Exception: pass

COLS = [u'культура', u'сцена', u'имя', u'тип', u'dp', u'группы', u'печать',
        u'подано', u'разобрано', u'запятая', u'печать≠en', u'разбор≠en', u'набор']


def table(path):
    rows, started = {}, False
    for line in io.open(path, encoding='utf-8', newline='').read().splitlines():
        if line.startswith(u'культура\t'):
            started = True
            continue
        if not started or not line.strip():
            continue
        f = line.split('\t')
        rows.setdefault((f[0], f[1]), []).append(f)
    return rows


def main(argv):
    before, after = table(argv[1]), table(argv[2])
    expected, unexpected, n = [], [], 0
    for key in sorted(set(before) | set(after)):
        if key[0] != u'en-US':
            continue
        ra, rb = before.get(key, []), after.get(key, [])
        if len(ra) != len(rb):
            unexpected.append((key[1], u'разное число полей: %d → %d' % (len(ra), len(rb))))
            continue
        for i, (x, y) in enumerate(zip(ra, rb)):
            n += 1
            for c in range(len(COLS)):
                if x[c] == y[c]:
                    continue
                note = u'поле #%d, столбец «%s»: «%s» → «%s»' % (i, COLS[c], x[c], y[c])
                known = (COLS[c] == u'тип'
                         or (key[1] == u'EfficiencyMakerForm'
                             and COLS[c] in (u'группы', u'печать', u'запятая')))
                (expected if known else unexpected).append((key[1], note))

    print(u'полей en-US сверено: %d' % n)
    print(u'ожидаемых расхождений (тип + снятая группировка): %d' % len(expected))
    for k, d in expected:
        if u'столбец «тип»' not in d:
            print(u'    %-34s %s' % (k, d))
    print(u'    (ещё %d — смена типа поля, по одному на каждое)'
          % sum(1 for _, d in expected if u'столбец «тип»' in d))
    print(u'НЕОЖИДАННЫХ расхождений: %d' % len(unexpected))
    for k, d in unexpected:
        print(u'    ⛔ %-34s %s' % (k, d))
    return 1 if unexpected else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
