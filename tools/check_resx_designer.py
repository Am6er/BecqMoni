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

## Чего это НЕ ловит

**Обращение с ВЫЧИСЛЯЕМЫМ именем.** `GetString(resourceName)`,
`GetString(issue.Resource)` — имя ключа известно только на ходу. Такие места
НЕ пропускаются молча: они считаются и печатаются числом, чтобы было видно,
какая доля дерева этой проверке недоступна. На 04.09.2026 их ДВА, и оба
проверены руками: `DCFwhmCalibrationView.GetResourceText` зовут только с
литералом `PeakFitChiTableUnavailable` и у неё ЕСТЬ запасное значение на
`null`; `GeometryEditorPanel` берёт имя из поля `Resource`, а туда в
`EfficiencyMaker/GeometryScenes.cs` кладут пять литералов
`GeometryEditorError*`. Все шесть ключей в `Resources.resx` есть.

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

    python tools/check_resx_designer.py [--list] [путь]

Возвращает 1, если хоть один ключ не найден.
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

# `ResourceManager.GetString("Foo", resourceCulture)` и `GetObject("Bar", ...)`.
# Имя ключа — обязательно ЛИТЕРАЛ: вычисляемое ловится отдельным выражением.
LITERAL = re.compile(r'ResourceManager\.Get(String|Object)\(\s*"([^"]*)"')
DYNAMIC = re.compile(r'ResourceManager\.Get(?:String|Object)\(\s*(?!")')
# Плечо обычного кода: единственная форма записи, встречающаяся в дереве.
HAND = re.compile(r'\bResources\.ResourceManager\.Get(String|Object)\(\s*"([^"]*)"')

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


def scan(path, pattern):
    u"""Строки файла с обращениями: [(номер строки, вид, ключ)]."""
    # ⚠ Часть `*.cs` в дереве НЕ в UTF-8 (наследство декомпилятора), и строгое
    # чтение на них падает. Имена ключей — ASCII всегда, поэтому испорченная
    # буква в комментарии разбору не мешает; файл только читается, не пишется.
    with open(path, encoding='utf-8-sig', errors='replace', newline='') as fh:
        text = fh.read()
    found, dynamic = [], 0
    for num, line in enumerate(text.replace('\r\n', '\n').split('\n'), 1):
        for kind, name in pattern.findall(line):
            found.append((num, kind, name))
        dynamic += len(DYNAMIC.findall(line))
    return found, dynamic


def main(argv):
    show = '--list' in argv
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

    for path, num, kind, name, only_ru in bad:
        print(u'НЕТ КЛЮЧА  %s:%d  Get%s("%s")%s'
              % (path.replace('\\', '/'), num, kind, name,
                 u'  — есть только в .ru.resx' if only_ru else ''))
    for path, pair, count in unpaired:
        print(u'НЕТ ПАРНОГО RESX  %s  (%d обращений, ждали %s)'
              % (path.replace('\\', '/'), count, os.path.basename(pair)))

    print()
    print(u'обращений с литеральным именем проверено: %d' % checked)
    print(u'ключей, которых нет в нейтральном resx: %d' % len(bad))
    print(u'обращений с вычисляемым именем (проверке недоступны): %d' % dynamic_total)
    if show:
        for path in sources(root, designer=False):
            for num, _kind, _name in scan(path, HAND)[0]:
                print(u'      рукописное: %s:%d' % (path.replace('\\', '/'), num))

    failed = bool(bad or unpaired)
    print(u'РАЗОШЛОСЬ' if failed else u'СОШЛОСЬ')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
