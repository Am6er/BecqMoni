# -*- coding: utf-8 -*-
r"""П188 (копия П181 apply_readme_rev36.py): заменить раздел «## ✅ ДЕЙСТВУЮЩАЯ БАЗА» в tools/CORPUS/README.md
(до «### Как переобъявлять базу») текстом D:\BqMoni_Claude\p188\readme_rev37.md и поправить §1.5 (склад — физика 26),
§1.10 (графа «матрица» сводки), §2.5 (кто исполнил единый счёт). BOM и CRLF сохраняются; каждая замена — ровно одно вхождение.
   python D:\BqMoni_Claude\p188\apply_readme_rev37.py <корень дерева>
"""
import io
import os
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = sys.argv[1]
README = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
SECTION = r'D:\BqMoni_Claude\p188\readme_rev37.md'
raw = open(README, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
text = raw.decode('utf-8-sig')
eol = '\r\n' if '\r\n' in text else '\n'
t = text.replace('\r\n', '\n')

if t.count(u'\n## ✅ ДЕЙСТВУЮЩАЯ БАЗА: ') != 1:
    print(u'⛔ заголовков раздела не один'); sys.exit(1)
start = t.index(u'\n## ✅ ДЕЙСТВУЮЩАЯ БАЗА: ') + 1
end = t.index(u'### Как переобъявлять базу', start)
new = io.open(SECTION, encoding='utf-8').read().replace('\r\n', '\n').rstrip('\n') + '\n\n'
t = t[:start] + new + t[end:]

REPL = [
    (u"Склад сегодня — **физика 25, формат файла 10** (физика 25 — ветка `p180-physics25`, полоса П180 28–29.09.2026,\n",
     u"Склад сегодня — **физика 26, формат файла 10** (физика 26 — ветка `p183-physics26`, полоса П183 29–30.09.2026,\n"
     u"ночной счёт П185 30.09, приёмка и объявление П188 30.09.2026: `AMBER139` спектр тормозного `ThickTargetBrem` —\n"
     u"розыгрыш энергии кванта из той же интерполяции узлов, что число квантов и энергетический момент, энергия бина по\n"
     u"форме розыгрыша, кончик тонкой мишени dσ/dk(k = T) = χ(κ = 1); `AMBER140` первый интервал сетки без экстраполяции\n"
     u"вниз; формат тот же, 10; физика 25 — ветка `p180-physics25`, полоса П180 28–29.09.2026,\n"),
    (u"клеймо каждой — `phys=25;<sha256 геометрии, настроек и отпечатка matdb>`",
     u"клеймо каждой — `phys=26;<sha256 геометрии, настроек и отпечатка matdb>`"),
    (u"`phys=25; hist=400000; grid=<низ>-3000 keV/<узлов> std;",
     u"`phys=26; hist=400000; grid=<низ>-3000 keV/<узлов> std;"),
    (u"`MatrixAuditProbe --phys=25 --hist=3000000 --except=RC103_point50:6000000",
     u"`MatrixAuditProbe --phys=26 --hist=3000000 --except=RC103_point50:6000000"),
    (u"⚠ **Плеча «как физика 24» у склада НЕТ:** правки физики 25 ключей склада не имеют (показатель обрыва света —\n"
     u"рычаг пробы `LightScaleProbe --eqp=`, не склада), а клеймо физики 25 несёт отпечаток базы с новым `kb_ev`;\n"
     u"склад физики 24 (rev35) — снимок `D:\\BqMoni_Claude\\p181\\store_backup\\` (156 файлов, 71 816 281 байт), годен\n"
     u"только прежней сборкой (`cac34379`).\n",
     u"⚠ **Плеча «как физика 25» у склада НЕТ:** правки физики 26 ключей склада не имеют (исправления кода тормозного);\n"
     u"склад физики 25 (rev36) — снимок `D:\\BqMoni_Claude\\p188\\store_backup\\` (98 файлов: 49 `.rmx` и `response/`,\n"
     u"70 912 084 байт), годен только прежней сборкой (`af8db260`).\n"
     u"⚠ **Плеча «как физика 24» у склада НЕТ:** правки физики 25 ключей склада не имеют (показатель обрыва света —\n"
     u"рычаг пробы `LightScaleProbe --eqp=`, не склада), а клеймо физики 25 несёт отпечаток базы с новым `kb_ev`;\n"
     u"склад физики 24 (rev35) — снимок `D:\\BqMoni_Claude\\p181\\store_backup\\` (156 файлов, 71 816 281 байт), годен\n"
     u"только прежней сборкой (`cac34379`).\n"),
    (u"сегодня матрица `физика 25` у 92,", u"сегодня матрица `физика 26` у 92,"),
    (u"П161 — физики 24 и П180 — физики 25.", u"П161 — физики 24, П180 — физики 25 и П185 — физики 26."),
]
for old, rep in REPL:
    n = t.count(old)
    if n != 1:
        print(u'⛔ вхождений %d, а не 1: %r' % (n, old[:80])); sys.exit(1)
    t = t.replace(old, rep)

out = t.replace('\n', eol)
open(README, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out.encode('utf-8'))
lone = out.count('\n') - out.count('\r\n')
print(u'BOM %s; перевод строки %r; CRLF %d -> %d; одиночных LF %d; замен %d' % (bom, eol, text.count('\r\n'), out.count('\r\n'), lone, len(REPL)))
sys.exit(0 if (eol == '\n' or lone == 0) else 1)
