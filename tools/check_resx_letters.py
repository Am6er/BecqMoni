# -*- coding: utf-8 -*-
"""Письмо ВНУТРИ значения ресурса: кириллица в английских `*.resx`,
омоглиф-чужак внутри слова в русских `*.ru.resx`.

`check_resx.py` рядом сверяет НАЛИЧИЕ ключей — переведено или нет. Что именно
написано в значении, она не смотрит вовсе, и потому годами не видела трёх
подмен (`A88`, 03.09.2026):

* `DocEnergySpectrum.resx` → `setUpperThresholdHToolStripMenuItem.Text`
  кончался на `(&Н)`, где `Н` — U+041D, КИРИЛЛИЦА. Это УСКОРИТЕЛЬ меню:
  в английском интерфейсе Alt+H пункт не вызывал, хотя контрол зовётся
  `…HToolStripMenuItem`. Единственная из трёх, что ломала поведение, а не вид.
* `DCEnergyCalibrationView.resx` → `checkBox1.Text` = `AutoСalibration`,
  `С` = U+0421.
* `Properties/Resources.resx` → `ERRUploadCoefficeintsToDevice` =
  `Error! Сoefficients uploaded…`, `С` = U+0421.

Глазами такое не ловится по определению: подмена и есть буква, неотличимая на
вид. Ловить обязана машина.

## Два плеча, и они РАЗНЫЕ

**Английский `*.resx` — кириллицы не должно быть ВООБЩЕ.** Правило жёсткое и
ложных срабатываний не даёт: перебором дерева 03.09.2026 по 44 английским
файлам кириллица нашлась ровно в трёх значениях, и все три — дефект.

**Русский `*.ru.resx` — «латиницы быть не должно» НЕВЕРНО.** Латинские слова
там законны и их много: `COM Порт`, `Windows`, `BecqMoni`, `Atom Spectra`,
`ROI`, `Gaussian`. Тем же перебором смешанных строк оказалось 239 — сторож на
таком правиле был бы красным всегда, то есть мёртвым. Поэтому в русских файлах
ищется не буква, а СЛОВО, В КОТОРОМ ПИСЬМО СМЕШАНО: непрерывный ряд букв, где
есть и латиница, и кириллица. Именно так выглядит омоглиф — `AutoСalibration`,
`Сoefficients`, — и именно так НЕ выглядит `COM Порт`.

⚠ Отсюда следствие, которое надо знать: одиночная подменённая буква в русском
файле (например `(&Н)` вместо `(&H)`) вторым плечом НЕ ловится — слова вокруг
неё нет. В английском плече она ловится, в русском нет, и это цена того, чтобы
второе плечо не тонуло в 239 законных строках.

## Чего этот сторож НЕ смотрит, и почему

**Ресурсы с атрибутом `type` или `mimetype`.** Там лежат картинки в base64, и
наивный поиск буквы по тексту файла даёт десятки попаданий ВНУТРЬ картинки —
на этом уже спотыкались. Текстовым считается ресурс БЕЗ обоих атрибутов, ровно
как в `check_resx.py`. Служебные `>>name` / `>>type` пропускаются тоже: там
имена контролов и имена типов .NET, они латинские всегда.

**Греческую «мю».** В `DeviceConfigForm.resx` английское `μSv/h` написано через
U+03BC (греческая мю), а не через U+00B5 (знак микро) — это тоже омоглиф, но
другого рода, и английские единицы решением Amber 03.09.2026 законны и не
правятся. Заводить на них сторожа значит красить дерево в красный по вопросу,
который решён.

## Приёмка (03.09.2026, `A88`)

Оба плеча на одних и тех же трёх подменах. На дереве `HEAD` до правки —
3 находки, все три названы поимённо с кодом точки; после правки — 0.

    python tools/check_resx_letters.py [--list] [путь]

Возвращает 1, если находки есть.
"""
import glob
import os
import re
import sys
import unicodedata
import xml.etree.ElementTree as ET

CYRILLIC = re.compile(r'[Ѐ-ӿԀ-ԯ]')
LATIN = re.compile(r'[A-Za-z]')
# Слово: непрерывный ряд букв двух алфавитов. Цифры, знаки и пробелы его рвут,
# поэтому `250mA` — латинское слово `mA`, а не смесь.
WORD = re.compile(r'[A-Za-zЀ-ӿԀ-ԯ]+')

# Поимённые исключения: ключи, которым кириллица в английском файле
# ПОЛОЖЕНА (например образец русского написания рядом с английским).
# Пусто — таких сегодня нет; список заведён, чтобы будущее исключение было
# видно строкой, а не размытым правилом.
ALLOW_CYRILLIC_IN_EN = frozenset()


def textual(name, node):
    """Ресурс, который человек ВИДИТ. Картинка и геометрия объявляют свой тип."""
    return not (name.startswith('>>') or node.get('mimetype') or node.get('type'))


def values(path):
    for node in ET.parse(path).getroot().findall('data'):
        name = node.get('name') or ''
        if textual(name, node):
            yield name, (node.findtext('value') or '')


def point(ch):
    return 'U+%04X %s' % (ord(ch), unicodedata.name(ch, '?'))


#: Знаки, которые в ЕДИНИЦАХ измерения выглядят как нужные, но ими не являются.
#: Ключ — самозванец, значение — (что ставить, чем это плохо). Решение Amber
#: 03.09.2026 по `A100`: микро пишется знаком микро, а не греческой буквой.
#: ⚠ Проверяется в ОБОИХ письмах: единица не переводится, и подмена в русском
#: файле так же ломает поиск и сравнение, как в английском.
UNIT_LOOKALIKES = {
    u'μ': (u'µ', u'греческая мю вместо знака микро'),
}


def check_units(path):
    """Самозванец в единице измерения (`A100`). Общий для обоих писем."""
    out = []
    for name, value in values(path):
        bad = []
        for ch in sorted(set(value)):
            if ch in UNIT_LOOKALIKES:
                want, why = UNIT_LOOKALIKES[ch]
                bad.append(u'%s — %s, ставить %s' % (point(ch), why, point(want)))
        if bad:
            out.append((name, value, bad))
    return out


def check_english(path):
    """Кириллица в английском ресурсе — всегда подмена."""
    out = []
    for name, value in values(path):
        if name in ALLOW_CYRILLIC_IN_EN:
            continue
        bad = sorted({c for c in value if CYRILLIC.match(c)})
        if bad:
            out.append((name, value, [point(c) for c in bad]))
    return out


def check_russian(path):
    """Слово со смешанным письмом в русском ресурсе — подмена буквы."""
    out = []
    for name, value in values(path):
        mixed = [w for w in WORD.findall(value)
                 if CYRILLIC.search(w) and LATIN.search(w)]
        if mixed:
            bad = []
            for word in mixed:
                minority = LATIN if len(CYRILLIC.findall(word)) >= len(LATIN.findall(word)) else CYRILLIC
                bad += ['%s в «%s»' % (point(c), word) for c in word if minority.match(c)]
            out.append((name, value, bad))
    return out


def main(argv):
    show = '--list' in argv
    rest = [a for a in argv if not a.startswith('--')]
    root = rest[0] if rest else 'BecquerelMonitor'

    seen_en = seen_ru = 0
    findings = []
    for path in sorted(glob.glob(os.path.join(root, '**', '*.resx'), recursive=True)):
        if path.endswith('.ru.resx'):
            seen_ru += 1
            rows = check_russian(path)
            arm = 'смешанное слово в русском'
        else:
            seen_en += 1
            rows = check_english(path)
            arm = 'кириллица в английском'
        for name, value, bad in rows:
            findings.append((path, arm, name, value, bad))
        # Третье плечо — общее для обоих писем (`A100`).
        for name, value, bad in check_units(path):
            findings.append((path, u'самозванец в единице', name, value, bad))

    for path, arm, name, value, bad in findings:
        print('%s | %s | %s' % (path.replace(os.sep, '/'), name, arm))
        if show:
            print('      значение: %r' % value)
            for item in bad:
                print('      %s' % item)

    print()
    print('английских resx просмотрено: %d, русских: %d' % (seen_en, seen_ru))
    print('находок: %d' % len(findings))
    print('РАЗОШЛОСЬ' if findings else 'СОШЛОСЬ')
    return 1 if findings else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
