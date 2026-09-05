# -*- coding: utf-8 -*-
u"""Ключ, которого нет: `GetString("X")` без `X` в парном resx.

`check_resx.py` рядом сверяет ПАРУ `Foo.resx` / `Foo.ru.resx` — что есть
по-английски, должно быть и по-русски. Класс дефекта, разобранный здесь, она
не видит ПО УСТРОЙСТВУ: ключа нет НИ В ОДНОМ из двух файлов, пара при этом
полна и сходится, а обращение к ключу живёт в коде.

`A91`, 04.09.2026: в `Properties/Resources.Designer.cs` лежали три свойства —
`ResponseMatrixEstimate`, `ResponseMatrixEstimating`, `ResponseMatrixProgressNoEta`, —
и ни одного из трёх ключей не было ни в `Resources.resx`, ни в
`Resources.ru.resx`. `ResourceManager.GetString` на таком возвращает `null`
молча, без исключения; падает не он, а первый же `string.Format` по нему —
`ArgumentNullException` в месте, к ресурсам отношения не имеющем. Читателей у
тех трёх не было, то есть ружьё было заряжено, но не выстрелило.

Откуда они там взялись, тоже стоит знать: строки СНЯЛ коммит `bbbb98ab` (`A46`,
решение Amber «времени в окне матрицы нет вовсе»), а `Resources.Designer.cs` —
файл ГЕНЕРИРУЕМЫЙ, и его после правки resx не перегенерировали. Так что это не
редкая случайность, а обычный след ручной правки resx: генератор и его выход
расходятся, и увидеть это может только машина.

## Два плеча

**Первое — `*.Designer.cs`.** Обращения там порождены генератором, ключ всегда
литерал, парный resx лежит рядом и зовётся так же (`Foo.Designer.cs` →
`Foo.resx`). Проверяется, что ключ в нём есть.

**Второе — обычный код.** Руками написанный `Resources.ResourceManager.GetString("X")`
болеет тем же и опаснее: у него, в отличие от свойства-сироты, ЕСТЬ читатель.
Разбирается только эта форма записи (единственная в дереве, проверено
перебором); ключ ищется в `BecquerelMonitor/Properties/Resources.resx`.

⚠ **Сверяется НЕЙТРАЛЬНЫЙ resx, а не русский.** Русский — сателлит: он
ПЕРЕКРЫВАЕТ строку, а не заводит её. Ключ, лежащий только в `*.ru.resx`, вернёт
`null` любому, у кого культура не русская, — поэтому такая находка не
прощается, а печатается отдельной пометкой «есть только в ru».

**Третье — литерал через ОДИН шаг (`A108`, 04.09.2026).** Имя ключа не обязано
стоять в самом вызове: `GetResourceText("PeakFitChiTableTitle", "…")` уходит в
обёртку, а `GetString(issue.Resource)` берёт имя из поля, куда литерал кладут в
другом файле. Оба пути разбираются НЕ по списку имён, а по устройству: сначала
находится вызов с вычисляемым именем, из него выводится источник (параметр
обёртки либо имя члена), и только потом по дереву собираются литералы. Так
третье такое место проверится само, без правки этого файла — и проверилось:
05.09.2026 в дереве появилась вторая обёртка, `Text()` в `DoseRate.cs`, и
плечо разобрало её без единой правки здесь (в сводке она названа рядом с
`GetResourceText`).

⛔ **Ручная проверка тех же мест уже промахнулась, и это измерено.** До `A108`
шесть ключей были проверены глазами и объявлены целыми. Первый же прогон
третьего плеча нашёл СЕДЬМОЙ: `DCFwhmCalibrationView.cs:704` просил
`PeakFitChiTableScoreColumn`, которого ТОГДА не было ни в `Resources.resx`, ни
в `Resources.ru.resx`. Приложение не падало (у обёртки есть запасное «Score»),
но заголовок столбца не переводился вовсе. Глаза считали ключи у ОДНОГО вызова
обёртки, а их у неё семь. Ключ заведён в оба resx правкой `A154` (05.09.2026);
находки по нему больше нет, и это утверждение — про историю, а не про дерево.

## Чего это НЕ ловит

**Вычисляемое имя, чей источник не литерал.** Разбирается ровно один шаг —
параметр обёртки и присваивание члену. Имя, собранное из кусков или пришедшее
из таблицы, останется недоступным; такие места не пропускаются молча, а
считаются и печатаются числом. На 04.09.2026 недоступных НОЛЬ.

**Обёртка над ЧУЖИМ менеджером ресурсов.** Разбирается только вызов через
общий `Resources.ResourceManager` — у него известен resx. Лучше честный ноль,
чем сверка не с тем файлом.

**Ключ, который есть, но пуст.** Это забота `check_resx.py` (`ResXNullRef`).

**Ключ, у которого нет читателя.** Обратное направление здесь сознательно не
проверяется: в resx форм лежат свойства контролов, которым свойства в
`Designer.cs` не положено вовсе, и такое плечо было бы красным всегда.

## Приёмка (04.09.2026, `A91`)

На дереве до правки — 3 находки, все три названы поимённо, код 1; после снятия
трёх свойств — 0 находок из 707 проверенных обращений, код 0.

**Положительный контроль, плечо первое — на настоящем дереве.** У ЖИВОГО ключа
`ResponseMatrixProgress` имя в `Resources.resx` временно портится на
`ResponseMatrixProgressXX`, файл возвращается ПОБАЙТОВО из копии со сверкой
SHA256. Проверка отказала (код 1), назвала ключ и заодно пометила «есть только
в .ru.resx» — русский файл при порче остался с исходным именем, и это ровно
тот случай, ради которого пометка заведена. После возврата — снова 0 и код 0.

**Плечо второе — на искусственном дереве**, потому что на настоящем у
рукописного плеча НОЛЬ находок, и «сошлось» о нём не говорит ничего.
Собирается пара resx + `Resources.Designer.cs` + рукописный `Hand.cs` +
`Lonely.Designer.cs` без парного resx. На исправном дереве — «СОШЛОСЬ», код 0;
на испорченном все четыре ветви срабатывают поимённо: ключа нет вовсе, ключ
есть только в ru, плохой ключ в рукописном коде, генерируемый файл без пары.

⚠ Сам этот контроль с первого захода напечатал «НЕТ» на четвёртой ветви —
и был неправ он, а не проверка: строка «НЕТ ПАРНОГО RESX» называет ФАЙЛ и
ожидавшийся resx, а контроль искал в ней имя ключа.

**Плечо третье — тоже на искусственном дереве (`A108`, 04.09.2026), потому что
файлы с ключами одного шага заняты другими полосами и портить их нельзя.**
Собирается дерево с обёрткой (`Wrap("Ключ", "запасное")` → `GetString(param)`) и
с членом (`GetString(issue.Resource)`, а `Resource = "Ключ"` кладут в ДРУГОМ
файле). Исправное дерево — код 0; порча ветви обёртки и порча ветви члена по
очереди — код 1, и каждый раз названы файл, строка и ключ.

И рядом стоит естественная мера на НАСТОЯЩЕМ дереве: та же команда на версии до
`A108` даёт 709 обращений, 0 находок, код 0; после — 726 обращений (+17 через
один шаг), недоступных 2 → 0, и ОДНА находка, подтверждённая руками
(`PeakFitChiTableScoreColumn` тогда не было ни в одном из двух resx; закрыто
`A154`). 05.09.2026 та же команда с `--no-format`: 732 обращения с литералом,
3 через один шаг, недоступных 0, по этому ключу находок нет.

    python tools/check_resx_designer.py [--list] [путь]

Возвращает 1, если хоть один ключ не найден.

## ⚠ Формат файла — плечо сторожа (`T156`, 05.09.2026)

Каждый `*.resx` обязан быть UTF-8 С BOM и с переводами строк CRLF, без
примесей (правило, счёт по дереву и причины — в `tools/resx_format.py`).
Файл чужого формата сверка НЕ читает молча: печатает `ФОРМАТ  <файл>: чем
плох` и возвращает 1, даже если по своему предмету всё сошлось. Правка,
написанная под BOM+CRLF, на таком файле отказывает (`A118`), и заход уходит
на выяснение причины — плечо заведено, чтобы причину называл сторож.
Снять плечо — ключ `--no-format` (на дереве с `core.autocrlf=false` всё
лежит LF, и там оно красно на всех файлах по устройству, а не по дефекту).
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

import resx_format

# `ResourceManager.GetString("Foo", resourceCulture)` и `GetObject("Bar", ...)`.
# Имя ключа — обязательно ЛИТЕРАЛ: вычисляемое ловится отдельным выражением.
LITERAL = re.compile(r'ResourceManager\.Get(String|Object)\(\s*"([^"]*)"')
DYNAMIC = re.compile(r'ResourceManager\.Get(?:String|Object)\(\s*(?!")')
# Плечо обычного кода: единственная форма записи, встречающаяся в дереве.
HAND = re.compile(r'\bResources\.ResourceManager\.Get(String|Object)\(\s*"([^"]*)"')

# ---------------------------------------------------------------------------
# Плечо третье: литерал, доезжающий до `GetString` через ОДИН шаг (`A108`).
#
# Имя ключа не обязано стоять в самом вызове. В дереве оно приходит двумя
# путями, и оба до 04.09.2026 были для проверки невидимы:
#
#   * ОБЁРТКА — `GetResourceText("PeakFitChiTableUnavailable", "n/a")`, а внутри
#     `Resources.ResourceManager.GetString(resourceName)`;
#   * ЧЛЕН — `GetString(issue.Resource)`, а `Resource = "GeometryEditorError…"`
#     кладут в другом файле (`EfficiencyMaker/GeometryScenes.cs`).
#
# Оба разбираются НЕ по списку имён, а по устройству: сначала находится сам
# вызов с вычисляемым именем, из него выводится источник (параметр обёртки либо
# имя члена), и только потом по дереву собираются литералы, которые в этот
# источник кладут. Список «какие обёртки бывают» здесь не пишется нарочно — он
# устарел бы ровно так же, как устарела ручная проверка шести ключей.
#
# ⛔ Разбирается только вызов через ОБЩИЙ менеджер (`Resources.ResourceManager`):
# у него известен resx. Обёртка над другим менеджером останется в недоступных —
# лучше честный ноль, чем сверка не с тем файлом.

# `Resources.ResourceManager.GetString(имя)` — простой идентификатор (параметр).
WRAP_CALL = re.compile(r'\bResources\.ResourceManager\.Get(?:String|Object)\(\s*([A-Za-z_]\w*)\s*[,)]')
# `Resources.ResourceManager.GetString(что-то.Член)` — имя из члена.
MEMBER_CALL = re.compile(r'\bResources\.ResourceManager\.Get(?:String|Object)\(\s*[A-Za-z_][\w\.]*\.([A-Za-z_]\w*)\s*[,)]')
# Объявление метода: модификаторы, тип, имя, скобки, открывающая фигурная.
DECL = re.compile(
    r'(?m)^[ \t]*(?:(?:public|private|protected|internal|static|virtual|override|sealed'
    r'|async|new|partial|extern|unsafe)\s+)*'
    r'[A-Za-z_][\w\.<>\[\],\s\?]*?\s+([A-Za-z_]\w*)\s*\(([^()]*)\)\s*(?:\r?\n)?\s*\{')


def split_params(text):
    u"""Имена параметров объявления: последнее слово каждого куска."""
    out = []
    depth = 0
    piece = ''
    for ch in text + ',':
        if ch in '<([':
            depth += 1
        elif ch in '>)]':
            depth -= 1
        if ch == ',' and depth <= 0:
            words = re.findall(r'[A-Za-z_]\w*', piece)
            if words:
                out.append(words[-1])
            piece = ''
        else:
            piece += ch
    return out


def wrappers(root):
    u"""Обёртки: {имя метода: номер параметра, который уходит в GetString}."""
    found = {}
    for path in sources(root, designer=False):
        text = read(path)
        decls = [(m.start(), m.group(1), split_params(m.group(2))) for m in DECL.finditer(text)]
        if not decls:
            continue
        for call in WRAP_CALL.finditer(text):
            # Объемлющий метод — последнее объявление ДО вызова. Конец тела не
            # считается скобками нарочно: следующее объявление и есть граница,
            # а вложенный метод в C# 7.3 внутри тела объявить можно и он тоже
            # попадёт в список — то есть граница выйдет только УЖЕ, не шире.
            owner = None
            for start, name, params in decls:
                if start < call.start():
                    owner = (name, params)
                else:
                    break
            if owner is None:
                continue
            name, params = owner
            arg = call.group(1)
            if arg in params:
                found[name] = params.index(arg)
    return found


def members(root):
    u"""Имена членов, через которые имя ключа доезжает до `GetString`."""
    out = set()
    for path in sources(root, designer=False):
        for m in MEMBER_CALL.finditer(read(path)):
            out.add(m.group(1))
    return out


def skip_literal(text, i):
    u"""Индекс ПОСЛЕ строкового или символьного литерала, начатого в `i`.

    Нужен обоим разборам ниже: скобка и запятая внутри `"…"` не должны считаться
    разделителями. Понимает обычную строку с `\\`-экранированием, дословную
    (`@"…"`, где кавычка удваивается) и символьный литерал.
    """
    n = len(text)
    quote = text[i]
    verbatim = quote == '"' and i > 0 and text[i - 1] == '@'
    i += 1
    while i < n:
        ch = text[i]
        if verbatim:
            if ch == quote:
                if i + 1 < n and text[i + 1] == quote:
                    i += 2
                    continue
                return i + 1
        else:
            if ch == '\\':
                i += 2
                continue
            if ch == quote:
                return i + 1
            if ch == '\n' and quote == '"':
                # Незакрытая строка: дальше идти нельзя, иначе разбор поедет.
                return i
        i += 1
    return n


def balanced_args(text, open_pos):
    u"""Текст аргументов вызова: от открывающей скобки до ПАРНОЙ ей.

    ⛔ `T228`, 05.09.2026: раньше аргументы брались выражением `\\(([^()]*)\\)`
    по ОДНОЙ строке, и вызов, разнесённый на две строки
    (`Text(` — перевод строки — `"Ключ", "запасное")`), не был виден ВОВСЕ, как и
    вызов с вложенными скобками. Так записано большинство обращений в дереве:
    сторож печатал «ключей, которых нет: 2» там, где их 26.
    """
    depth = 0
    i, n = open_pos, len(text)
    while i < n:
        ch = text[i]
        if ch in '"\'':
            i = skip_literal(text, i)
            continue
        if ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
            if depth == 0:
                return text[open_pos + 1:i]
        i += 1
    return None


def line_of(text, pos):
    u"""Номер строки, в которой стоит смещение `pos` (текст уже с LF)."""
    return text.count('\n', 0, pos) + 1


def one_step(root):
    u"""Литералы, доезжающие до `GetString` через один шаг: [(файл, строка, ключ, откуда)]."""
    found = []
    wraps = wrappers(root)
    membs = members(root)
    if not wraps and not membs:
        return found, wraps, membs

    # ⚠ Разбор идёт по ТЕКСТУ ФАЙЛА ЦЕЛИКОМ, а не построчно: выражение находит
    # только имя вызова и открывающую скобку, аргументы отрезает `balanced_args`.
    calls = [(re.compile(r'\b%s\s*\(' % re.escape(name)), name, pos)
             for name, pos in sorted(wraps.items())]
    # `\s` захватывает и перевод строки, поэтому присваивание члену тоже
    # находится разнесённым на строки.
    sets = [(re.compile(r'\b%s\s*=\s*"([^"]*)"' % re.escape(name)), name)
            for name in sorted(membs)]

    for designer in (True, False):
        for path in sources(root, designer=designer):
            text = read(path).replace('\r\n', '\n')
            for pattern, name, pos in calls:
                for match in pattern.finditer(text):
                    args = balanced_args(text, match.end() - 1)
                    if args is None:
                        continue
                    parts = split_args(args)
                    if pos < len(parts):
                        lit = re.match(r'\s*"([^"]*)"\s*\Z', parts[pos], re.S)
                        if lit:
                            found.append((path, line_of(text, match.start()),
                                          lit.group(1), name + '()'))
            for pattern, name in sets:
                for match in pattern.finditer(text):
                    found.append((path, line_of(text, match.start()),
                                  match.group(1), '.' + name))
    return found, wraps, membs


def split_args(text):
    u"""Аргументы вызова по запятым верхнего уровня.

    ⚠ Запятая внутри строкового литерала разделителем не считается: иначе
    `Text("Ключ", "нет, не вышло")` разрезается не там, где написано.
    """
    out, depth, piece, i, n = [], 0, '', 0, len(text)
    while i <= n:
        ch = text[i] if i < n else ','
        if i < n and ch in '"\'':
            end = skip_literal(text, i)
            piece += text[i:end]
            i = end
            continue
        if ch in '<([':
            depth += 1
        elif ch in '>)]':
            depth -= 1
        if ch == ',' and depth <= 0:
            out.append(piece)
            piece = ''
        else:
            piece += ch
        i += 1
    return out

# Общие ресурсы приложения — то, к чему обращается рукописный код. Путь берётся
# ОТ КОРНЯ разбора, а не прибит: иначе проверку нельзя проверить на другом дереве.
SHARED = os.path.join('Properties', 'Resources.resx')


def keys(path):
    u"""Имена ресурсов файла. Служебные `>>name` / `>>type` не в счёт."""
    if not os.path.exists(path):
        return None
    out = set()
    for node in ET.parse(path).getroot().findall('data'):
        name = node.get('name')
        if name and not name.startswith('>>'):
            out.add(name)
    return out


def sources(root, designer):
    u"""Файлы `*.cs` дерева: генерируемые отдельно от рукописных."""
    for path in sorted(glob.glob(os.path.join(root, '**', '*.cs'), recursive=True)):
        if path.endswith('.Designer.cs') == designer:
            yield path


def read(path):
    u"""Текст файла. ⚠ Часть `*.cs` в дереве НЕ в UTF-8 (наследство
    декомпилятора), и строгое чтение на них падает. Имена ключей — ASCII всегда,
    поэтому испорченная буква в комментарии разбору не мешает; файл только
    читается, не пишется."""
    with open(path, encoding='utf-8-sig', errors='replace', newline='') as fh:
        return fh.read()


def scan(path, pattern):
    u"""Строки файла с обращениями: [(номер строки, вид, ключ)]."""
    text = read(path)
    found, dynamic = [], 0
    for num, line in enumerate(text.replace('\r\n', '\n').split('\n'), 1):
        for kind, name in pattern.findall(line):
            found.append((num, kind, name))
        dynamic += len(DYNAMIC.findall(line))
    return found, dynamic


def main(argv):
    show = '--list' in argv
    no_format = '--no-format' in argv
    rest = [a for a in argv if not a.startswith('--')]
    root = rest[0] if rest else 'BecquerelMonitor'

    bad, checked, dynamic_total, unpaired = [], 0, 0, []
    cache = {}

    def resx(path):
        if path not in cache:
            cache[path] = keys(path)
        return cache[path]

    # Плечо первое: генерируемые файлы против resx, лежащего рядом под тем же именем.
    for path in sources(root, designer=True):
        found, dynamic = scan(path, LITERAL)
        dynamic_total += dynamic
        if not found:
            continue
        pair = path[:-len('.Designer.cs')] + '.resx'
        names = resx(pair)
        if names is None:
            unpaired.append((path, pair, len(found)))
            continue
        ru = resx(pair[:-len('.resx')] + '.ru.resx')
        for num, kind, name in found:
            checked += 1
            if name not in names:
                only_ru = bool(ru and name in ru)
                bad.append((path, num, kind, name, only_ru))

    # Плечо второе: рукописные обращения к общим ресурсам приложения.
    shared = os.path.join(root, SHARED)
    names = resx(shared)
    ru = resx(shared[:-len('.resx')] + '.ru.resx')
    for path in sources(root, designer=False):
        found, dynamic = scan(path, HAND)
        dynamic_total += dynamic
        if found and names is None:
            # Сверять не с чем — и молчать об этом нельзя: признак отказа без
            # читателя это не признак.
            unpaired.append((path, shared, len(found)))
            continue
        for num, kind, name in found:
            checked += 1
            if name not in names:
                bad.append((path, num, kind, name, bool(ru and name in ru)))

    # Плечо третье: литерал через ОДИН шаг — обёртка или член (`A108`).
    step, wraps, membs = one_step(root)
    for path, num, key, via in step:
        checked += 1
        if names is None:
            continue
        if key not in names:
            bad.append((path, num, u'String через ' + via, key, bool(ru and key in ru)))

    for path, num, kind, name, only_ru in bad:
        print(u'НЕТ КЛЮЧА  %s:%d  Get%s("%s")%s'
              % (path.replace('\\', '/'), num, kind, name,
                 u'  — есть только в .ru.resx' if only_ru else ''))
    for path, pair, count in unpaired:
        print(u'НЕТ ПАРНОГО RESX  %s  (%d обращений, ждали %s)'
              % (path.replace('\\', '/'), count, os.path.basename(pair)))

    print()
    print(u'обращений с литеральным именем проверено: %d' % checked)
    print(u'  из них через ОДИН шаг (обёртка или член): %d' % len(step))
    print(u'ключей, которых нет в нейтральном resx: %d' % len(bad))
    # ⚠ Вычитается ЧИСЛО ВЫЗОВОВ с вычисляемым именем, а не число разобранных
    # литералов: один такой вызов обслуживает много литералов, и вычитание
    # вторых дало бы отрицательный остаток. Разобранным считается вызов, чей
    # источник имени найден, — их ровно столько, сколько обёрток и членов.
    resolved = len(wraps) + len(membs)
    print(u'обращений с вычисляемым именем: %d, из них разобрано одним шагом: %d'
          % (dynamic_total, resolved))
    print(u'обращений с вычисляемым именем (проверке недоступны): %d'
          % max(dynamic_total - resolved, 0))
    if wraps:
        print(u'      обёртки: %s'
              % u', '.join(u'%s(аргумент %d)' % (n, p + 1) for n, p in sorted(wraps.items())))
    if membs:
        print(u'      члены:   %s' % u', '.join(sorted(membs)))
    if show:
        for path in sources(root, designer=False):
            for num, _kind, _name in scan(path, HAND)[0]:
                print(u'      рукописное: %s:%d' % (path.replace('\\', '/'), num))

    # Плечо формата (`T156`): по ВСЕМ resx дерева, не только прочитанным, —
    # чтобы четыре сверки называли одно и то же число.
    fmt = [] if no_format else resx_format.problems(
        sorted(glob.glob(os.path.join(root, '**', '*.resx'), recursive=True)))
    for line in fmt:
        print(line)
    print(u'файлов чужого формата (не BOM+CRLF): %d' % len(fmt))

    failed = bool(bad or unpaired or fmt)
    print(u'РАЗОШЛОСЬ' if failed else u'СОШЛОСЬ')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
