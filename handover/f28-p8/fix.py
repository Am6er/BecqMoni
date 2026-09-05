# -*- coding: utf-8 -*-
"""F28: точечная правка мест печати/разбора чисел.

Читает и пишет с newline='' и utf-8-sig — переводы строк и BOM файла не
трогаются (грабли «питон рвёт текст на одиночном CR» и «считать байтами»).
Каждая пара несёт ОЖИДАЕМОЕ число совпадений: не сошлось — отказ, файл не
переписан.
"""
import io, re, sys

INV = 'CultureInfo.InvariantCulture'


def patch(path, pairs, need_globalization=True):
    # ⛔ BOM СОХРАНЯЕТСЯ КАК БЫЛ. `encoding='utf-8-sig'` на ЗАПИСЬ ставит BOM
    #    ВСЕГДА — на файле без BOM это молчаливая правка формата, и `git diff`
    #    показывает её первой строкой как «изменение» (поймано на
    #    `PulseView.cs` и `Utils/BecquerelCoefficient.cs`).
    raw = io.open(path, 'rb').read()
    had_bom = raw[:3] == b'\xef\xbb\xbf'
    s = io.open(path, encoding='utf-8-sig', newline='').read()
    total = 0
    for a, b, want in pairs:
        got = s.count(a)
        if got != want:
            raise AssertionError('%s: %r найдено %d, ждали %d' % (path, a[:70], got, want))
        s = s.replace(a, b)
        total += got
    if need_globalization and 'using System.Globalization;' not in s:
        m = re.search(r'using [A-Za-z0-9_.]+;(\r?\n)', s)
        if not m:
            raise AssertionError('%s: некуда добавить using' % path)
        s = s[:m.end()] + 'using System.Globalization;' + m.group(1) + s[m.end():]
    io.open(path, 'w', encoding='utf-8-sig' if had_bom else 'utf-8',
            newline='').write(s)
    return total
