# -*- coding: utf-8 -*-
u"""П18/`E29`: заметит ли оснастка НОВЫЙ КЛЮЧ РОЗЫГРЫША точки вылета.

Дерево НЕ ПРАВИТСЯ: исходник читается, порча подставляется в памяти (тем же
приёмом, что `selftest` самого сторожа), приговор берётся у `judge`.

Три плеча:
  0. целое дерево                       -> нарушений быть не должно;
  1. поле `ImportanceSampling` заведено только в объявлении симулятора
     -> сторож ОБЯЗАН отказать (правило A: реестр равен исходнику);
  2. то же поле, но и в реестре сторожа, и в клейме нет
     -> проверка, что дело именно в реестре, а не в имени.
"""
from __future__ import print_function
import io
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__)))
REPO = u'C:\\Users\\moroz\\source\\repos\\BQ Eng res .NET 4.8'
sys.path.insert(0, os.path.join(REPO, u'tools'))

import check_matrix_keys as G  # noqa: E402

src = {}
for rel in G.SOURCES:
    src[rel] = G.read(REPO, rel)
    if src[rel] is None:
        print(u'нет файла: %s' % rel)
        sys.exit(3)

clean = G.judge(src)
print(u'плечо 0 (целое дерево): нарушений %d' % len(clean))
for b in clean:
    print(u'   %s' % b)

ANCHOR = u'public bool AnalogConeSampling = false;'
NEW = (u'public bool ImportanceSampling = false;\n\n        '
       + ANCHOR)
spoiled = dict(src)
if ANCHOR not in spoiled[G.ES]:
    print(u'ЯКОРЬ НЕ НАЙДЕН — опыт не поставлен')
    sys.exit(2)

spoiled[G.ES] = spoiled[G.ES].replace(ANCHOR, NEW, 1)
bad = G.judge(spoiled)
print(u'плечо 1 (новый ключ розыгрыша мимо реестра): нарушений %d' % len(bad))
for b in bad:
    print(u'   %s' % b)

print(u'ВЕРДИКТ: %s' % (u'сторож ЗАМЕТИЛ новый ключ розыгрыша'
                        if len(bad) > len(clean)
                        else u'СТОРОЖ СЛЕП — ключ прошёл бы молча'))
sys.exit(0 if len(bad) > len(clean) else 1)
