# -*- coding: utf-8 -*-
u"""Сверка прогонов ДО и ПОСЛЕ правки `A234`/`A235` — числом, а не глазами.

Три плеча положительного контроля:
  1. слепки всех плеч совпали ПОБАЙТНО — правка не сдвинула ни одного числа;
  2. на ОДНОМ файле обе двери сказали ОДНО И ТО ЖЕ;
  3. русская строка отличается от английской (обе прочитаны из СОБРАННОГО
     сателлита самим приложением, а не из `.resx`).
"""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ARM = u'УМОЛЧАНИЕ НЕ СТРОИТСЯ, файл 2006 | документ приложения | дверь '


def read(name):
    with io.open(os.path.join(HERE, name), encoding='utf-8', errors='replace') as fh:
        return fh.read().replace('\r\n', '\n').split('\n')


def bodies(lines):
    return [l for l in lines if u' | ВВЕЗЁН | ' in l or u' | ОТКАЗ | ' in l]


def totals(lines):
    return [l for l in lines if l.startswith(u'  ИТОГ: ')]


def voice_of(lines, arm):
    out, on = [], False
    for l in lines:
        if l.startswith(u'=== ' + arm):
            on = True
            continue
        if on and l.startswith(u'==='):
            break
        if on and l.strip().startswith(u'BecqMoni:'):
            out.append(l.strip())
    return out


report = []
ok = True
for cul in ('ru', 'en'):
    b, a = read('before-noconfig-%s.txt' % cul), read('after-noconfig-%s.txt' % cul)
    same = bodies(b) == bodies(a)
    ok &= same
    report.append(u'%s: слепков ДО %d, ПОСЛЕ %d, совпали побайтно: %s'
                  % (cul, len(bodies(b)), len(bodies(a)), u'ДА' if same else u'НЕТ'))

for cul in ('ru', 'en'):
    a = read('after-noconfig-%s.txt' % cul)
    su, n4 = voice_of(a, ARM + 'SpecUtils'), voice_of(a, ARM + 'N42')
    # Путь в строке один и тот же файл, поэтому строки обязаны совпасть целиком.
    same = bool(su) and su == n4
    ok &= same
    report.append(u'%s: голос SpecUtils == голос N42 на ОДНОМ файле: %s (строк %d/%d)'
                  % (cul, u'ДА' if same else u'НЕТ', len(su), len(n4)))

r = voice_of(read('after-noconfig-ru.txt'), ARM + 'N42')
e = voice_of(read('after-noconfig-en.txt'), ARM + 'N42')
diff = bool(r) and bool(e) and r != e
ok &= diff
report.append(u'русская строка отличается от английской: %s' % (u'ДА' if diff else u'НЕТ'))
report.append(u'  ru: %s' % (r[0] if r else u'—'))
report.append(u'  en: %s' % (e[0] if e else u'—'))

report.append(u'')
report.append(u'=== ИТОГИ ПЛЕЧ, ДО -> ПОСЛЕ (культура ru) ===')
for x, y in zip(totals(read('before-noconfig-ru.txt')), totals(read('after-noconfig-ru.txt'))):
    report.append(u'  ДО    ' + x.strip())
    report.append(u'  ПОСЛЕ ' + y.strip())
report.append(u'')
report.append(u'СОШЛОСЬ' if ok else u'РАЗОШЛОСЬ')

with io.open(os.path.join(HERE, 'compare.txt'), 'w', encoding='utf-8') as fh:
    fh.write(u'\n'.join(report) + u'\n')
for line in report:
    print(line)
