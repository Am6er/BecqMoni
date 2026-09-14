# -*- coding: utf-8 -*-
u"""Отсев родителей образов вылета НЕ ВЫКЛЮЧАЕТСЯ САМ (`A271`).

## Откуда взялся

06.09.2026 правка `S141` завела образы одиночного и двойного вылета в
библиотеку из баз — ту, которой идёт весь корпус. Единственный физический
ограничитель отбора родителей, доля рождения пар в веществе кристалла,
требует ВЕЩЕСТВА, а у спектра без геометрии его нет. Отсев был записан так:

    double share = PairShare(crystalFractions, line.Energy);
    if (!double.IsNaN(share) && !(share >= EscapeMinPairShare)) continue;

— то есть при неизвестном веществе он отключался ЦЕЛИКОМ. Родителем
становилась любая линия выше 1022 кэВ по сырому выходу: наверх выходила
`Bi-214` 1120, её образ `SE-1120` вставал на 609.0 — ПОВЕРХ настоящей
`Bi-214` 609.3 — и забирал до 76 % отсчётов спектра.

⚠ Заглавным числом это выглядело ВЫИГРЫШЕМ: свободный образ без верхней
границы всегда уменьшает невязку, и Σχ² непонятной части при дефекте УЛУЧШИЛСЯ
(205.3 → 193.2) при recall 96 % → 89 %. Правка прошла приёмку с двойным
положительным контролем. Поэтому здесь читатель СТАТИЧЕСКИЙ: он судит не
числа, а форму правила.

## Что проверяется

1. `FsaLibrary.EscapeParentMarginKev` объявлена и строго больше нуля — запас
   над порогом рождения пар, который работает и без вещества;
2. в `FsaLibrary.EscapeImages` энергия линии сравнивается с СУММОЙ
   `PairThresholdKev + EscapeParentMarginKev`, а не с голым порогом;
3. проверка запаса стоит РАНЬШЕ проверки `double.IsNaN(share)`: отсев по доле
   имеет право молчать при неизвестном веществе только тогда, когда до него
   уже отработал тот, который молчать не умеет;
4. в `EscapeImages` нет условия вида `!double.IsNaN(...) && !(... >= ...)` —
   именно этой формой отсев и выключал себя;
5. собиратель из баз (`FsaSampleLibrary.AddEscapeImages`) НЕ берёт
   `spec.CrystalFractions` напрямую, а зовёт `CrystalFractionsOf` — ту, что
   восстанавливает вещество по названным элементам кристалла;
6. запас ЗАМЕРЕН, а не выбран: проба `EscapeMarginProbe.cs` существует и
   печатает наименьшее пересечение доли пар с порогом по кристаллам
   библиотеки. Число без замера — не число.

## Самопроверка

⛔ Все шесть проверок прошли бы и на пустом чтении, поэтому на каждом прогоне
сторож судит ещё и ПОРЧЕНУЮ копию: в ней запас снят из условия отбора
(остаётся голый `PairThresholdKev`), и сторож обязан назвать это поимённо.
Не назвал — сторож красный, что бы ни показало настоящее дерево.

Коды возврата: 0 — сошлось; 1 — не сошлось; 2 — нечего читать.
"""
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FSA = os.path.join(REPO, 'BecquerelMonitor', 'FullSpectrumAnalysis')
LIB = os.path.join(FSA, 'FsaLibrary.cs')
SAMPLE = os.path.join(FSA, 'FsaSampleLibrary.cs')
PROBE = os.path.join(REPO, 'tools', 'effmaker', 'probes', 'EscapeMarginProbe.cs')

MARGIN = u'EscapeParentMarginKev'
THRESHOLD = u'PairThresholdKev'

RE_MARGIN_DECL = re.compile(
    u'const\\s+double\\s+%s\\s*=\\s*([0-9.]+)\\s*;' % MARGIN)
# Сумма порога и запаса — в любом порядке слагаемых.
RE_SUM = re.compile(
    u'(%s\\s*\\+\\s*%s|%s\\s*\\+\\s*%s)' % (THRESHOLD, MARGIN, MARGIN, THRESHOLD))
# Та самая форма, которой отсев выключал себя.
RE_SELF_OFF = re.compile(u'!\\s*double\\.IsNaN\\s*\\([^)]*\\)\\s*&&\\s*!\\s*\\(')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def read(path):
    with io.open(path, 'r', encoding='utf-8-sig', newline='') as fh:
        return fh.read()


def strip_comments(text):
    u"""Тело без описаний и заметок: правило судится по КОДУ, а не по словам о нём."""
    text = re.sub(u'/\\*.*?\\*/', u' ', text, flags=re.S)
    return re.sub(u'^[ \\t]*//.*$', u' ', text, flags=re.M)


def method_body(text, name):
    u"""Тело метода по имени; None — метода нет.

    ⚠ Поиск продолжается ПОЗИЦИЕЙ у скомпилированного шаблона
    (`pattern.search(text, pos)`), а не третьим доводом `re.search` — там
    стоят ФЛАГИ, и попытка передать позицию так дала вечный цикл на первом
    же прогоне: поиск каждый раз начинался с начала строки.
    """
    pattern = re.compile(u'\\b%s\\s*\\(' % re.escape(name))
    pos = 0
    while True:
        head = pattern.search(text, pos)
        if head is None:
            return None
        pos = head.end()
        start = text.find(u'{', pos)
        semi = text.find(u';', pos)
        if start < 0 or (0 <= semi < start):
            continue                      # вызов или объявление, не тело
        depth = 0
        for i in range(start, len(text)):
            if text[i] == u'{':
                depth += 1
            elif text[i] == u'}':
                depth -= 1
                if depth == 0:
                    return text[start:i + 1]
        return None


def judge(lib_text, sample_text, probe_text):
    u"""Список отказов словами; пустой — сошлось."""
    bad = []
    lib = strip_comments(lib_text)
    sample = strip_comments(sample_text)

    # 1. запас объявлен и положителен
    decl = RE_MARGIN_DECL.search(lib)
    if decl is None:
        bad.append(u'%s не объявлена в FsaLibrary.cs' % MARGIN)
    elif float(decl.group(1)) <= 0.0:
        bad.append(u'%s = %s — запас обязан быть строго больше нуля'
                   % (MARGIN, decl.group(1)))

    images = method_body(lib, u'EscapeImages')
    if images is None:
        bad.append(u'в FsaLibrary.cs нет тела EscapeImages — судить нечем')
        return bad

    # 2. отбор сравнивает энергию с СУММОЙ порога и запаса
    sum_at = RE_SUM.search(images)
    if sum_at is None:
        bad.append(u'EscapeImages не сравнивает энергию родителя с '
                   u'%s + %s: отсев без вещества выключен' % (THRESHOLD, MARGIN))

    # 3. запас спрашивается РАНЬШЕ доли
    nan_at = images.find(u'double.IsNaN')
    if sum_at is not None and nan_at >= 0 and nan_at < sum_at.start():
        bad.append(u'в EscapeImages проверка double.IsNaN стоит РАНЬШЕ запаса '
                   u'по энергии: при неизвестном веществе отсева не остаётся')

    # 4. запрещённая форма «выключаю себя при NaN»
    if RE_SELF_OFF.search(images) is not None:
        bad.append(u'в EscapeImages есть условие вида !double.IsNaN(x) && !(…) — '
                   u'этой формой отсев и выключал себя (`A271`)')

    # 5. собиратель из баз идёт через восстановление вещества
    add = method_body(sample, u'AddEscapeImages')
    if add is None:
        bad.append(u'в FsaSampleLibrary.cs нет тела AddEscapeImages — судить нечем')
    else:
        if u'CrystalFractionsOf' not in add:
            bad.append(u'AddEscapeImages не зовёт CrystalFractionsOf: вещество '
                       u'кристалла по названным элементам не восстанавливается')
        if u'spec.CrystalFractions' in add:
            bad.append(u'AddEscapeImages берёт spec.CrystalFractions напрямую — '
                       u'мимо восстановления вещества')

    # 6. замер запаса живёт пробой
    if probe_text is None:
        bad.append(u'нет пробы EscapeMarginProbe.cs — запас ничем не замерен')
    elif MARGIN not in probe_text:
        bad.append(u'EscapeMarginProbe.cs не читает %s — замер не про запас' % MARGIN)

    return bad


def spoil_margin(lib_text):
    u"""Порча первая: запас снят из условия отбора, остаётся голый порог пар."""
    spoiled = re.sub(u'%s\\s*\\+\\s*%s' % (THRESHOLD, MARGIN), THRESHOLD, lib_text)
    spoiled = re.sub(u'%s\\s*\\+\\s*%s' % (MARGIN, THRESHOLD), THRESHOLD, spoiled)
    return spoiled if spoiled != lib_text else None


def spoil_self_off(lib_text):
    u"""Порча вторая: возвращена та самая форма, которой отсев выключал себя."""
    spoiled = lib_text.replace(
        u'if (!double.IsNaN(share) && share < EscapeMinPairShare)',
        u'if (!double.IsNaN(share) && !(share >= EscapeMinPairShare))')
    return spoiled if spoiled != lib_text else None


def main():
    for path in (LIB, SAMPLE):
        if not os.path.isfile(path):
            print(u'⛔ нет файла: %s' % path)
            return 2

    lib_text = read(LIB)
    sample_text = read(SAMPLE)
    probe_text = read(PROBE) if os.path.isfile(PROBE) else None

    print(u'=== ОТСЕВ РОДИТЕЛЕЙ ОБРАЗОВ ВЫЛЕТА (`A271`) ===')
    decl = RE_MARGIN_DECL.search(strip_comments(lib_text))
    print(u'  запас над порогом пар: %s кэВ'
          % (decl.group(1) if decl else u'НЕ ОБЪЯВЛЕН'))

    bad = judge(lib_text, sample_text, probe_text)
    for line in bad:
        print(u'  ⛔ %s' % line)
    if not bad:
        print(u'  все шесть правил сошлись')

    print()
    print(u'=== САМОПРОВЕРКА: две порченые копии обязаны быть названы ===')
    checks = (
        (u'запас снят из условия отбора', spoil_margin,
         lambda line: MARGIN in line and THRESHOLD in line),
        (u'возвращена форма !double.IsNaN(x) && !(…)', spoil_self_off,
         lambda line: u'IsNaN' in line),
    )
    for what, make, hits in checks:
        spoiled = make(lib_text)
        if spoiled is None:
            print(u'  ⛔ %s: порчу подставить не удалось' % what)
            bad.append(u'самопроверка')
            continue
        caught = judge(spoiled, sample_text, probe_text)
        if any(hits(line) for line in caught):
            print(u'  подставлено: %s — названо поимённо' % what)
        else:
            print(u'  ⛔ САМОПРОВЕРКА ПРОВАЛЕНА (%s): названо %s'
                  % (what, u'; '.join(caught) or u'ничего'))
            bad.append(u'самопроверка')

    print()
    if bad:
        print(u'НЕ СОШЛОСЬ: %d' % len(bad))
        return 1
    print(u'СОШЛОСЬ: отсев родителей вылета работает и без вещества кристалла')
    return 0


if __name__ == '__main__':
    sys.exit(main())
