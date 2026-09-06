# -*- coding: utf-8 -*-
u"""Вещество кристалла у прибора: ГЕОМЕТРИЯ ПЕРВИЧНА, поле — запасной источник
(`A276`).

## Откуда взялся

06.09.2026 починка ~~`A271`~~ вернула отбору родителей образов вылета SE/DE
физический ограничитель — долю рождения пар в веществе кристалла. Половина
починки, восстановление вещества по названным элементам, у ЧЕЛОВЕКА не
работала: `FsaCompositionInference` заполнял состав кристалла ТОЛЬКО из
геометрии, а у прибора без геометрии её нет. Решением Amber заведено поле
конфигурации прибора «вещество кристалла» с выбором из библиотеки веществ.

⚠ Класс дефекта, ради которого написан этот сторож, — НЕ «поле не читается».
Такое видно первым же замером. Опасно обратное: поле начнёт читаться ТАМ, ГДЕ
ЕСТЬ ГЕОМЕТРИЯ, и тихо сдвинет числа сцен, у которых всё и так работало. Ровно
это и не ловится глазами: старшинство источников живёт в порядке двух условий
внутри одного метода, и переставить их местами можно правкой в одну строку.

## Что проверяется

1. `DeviceConfigInfo.CrystalMaterialName` объявлено, а поле под ним
   инициализировано ПУСТОЙ строкой: старая конфигурация без этого элемента
   обязана читаться, а не отказывать;
2. глубокая копия (`DeviceConfigInfo(DeviceConfigInfo)`) поле переносит — иначе
   правка формы не переживёт «ОК»;
3. `FsaSampleSpec.CrystalMaterialName` объявлено;
4. ⛔ ПОРЯДОК ИСТОЧНИКОВ в `FsaSampleLibrary.CrystalFractionsOf`: сперва
   `spec.CrystalFractions` (геометрия), потом
   `FractionsOfMaterial(spec.CrystalMaterialName)` (поле прибора), и только
   потом восстановление по `spec.CrystalElements`;
5. `FsaCompositionInference` кладёт в спецификацию имя из
   `DeviceConfig.CrystalMaterialName`;
6. `FsaAnalysisSession` берёт вещество поля ТОЛЬКО ветвью `else` от проверки
   геометрии — путь по пикам (умолчание разбора) обязан слушаться того же
   старшинства;
7. форма прибора связана с обоих концов: `LoadFormContents` зовёт
   `LoadCrystalMaterial`, а запись присваивает `config.CrystalMaterialName`
   из `CrystalMaterialFromForm()`;
8. подписи лежат в ОБОИХ `resx` формы — и подпись поля, и пункт «не задано»;
9. ⛔ ИМЁН ВЕЩЕСТВ В КОДЕ FSA НЕТ (решение Amber 01.09.2026): ни одно имя из
   засева библиотеки веществ не встречается строковым литералом в файлах
   разбора. Вещество берётся по ссылке, а не по таблице имён;
10. замер существует: проба `CrystalMaterialProbe.cs` на месте.

## Самопроверка

⛔ Все десять проверок прошли бы и на пустом чтении, поэтому на каждом прогоне
сторож судит ещё и ДВЕ ПОРЧЕНЫЕ КОПИИ: в первой источники в
`CrystalFractionsOf` переставлены (поле спрашивается раньше геометрии), во
второй поле конфигурации инициализировано не пустой строкой. Обе обязаны быть
названы поимённо; не назвал — сторож красный, что бы ни показало дерево.

Коды возврата: 0 — сошлось; 1 — не сошлось; 2 — нечего читать.
"""
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(REPO, 'BecquerelMonitor')
FSA = os.path.join(APP, 'FullSpectrumAnalysis')

INFO = os.path.join(APP, 'DeviceConfigInfo.cs')
FORM = os.path.join(APP, 'DeviceConfigForm.cs')
SAMPLE = os.path.join(FSA, 'FsaSampleLibrary.cs')
INFER = os.path.join(FSA, 'FsaCompositionInference.cs')
SESSION = os.path.join(FSA, 'FsaAnalysisSession.cs')
LIBRARY = os.path.join(APP, 'EfficiencyMaker', 'GeometryMaterialLibrary.cs')
PROBE = os.path.join(REPO, 'tools', 'effmaker', 'probes', 'CrystalMaterialProbe.cs')
RESX = os.path.join(APP, 'DeviceConfigForm.resx')
RESX_RU = os.path.join(APP, 'DeviceConfigForm.ru.resx')

FIELD = u'CrystalMaterialName'

# Файлы разбора, в которых имени вещества быть не должно.
FSA_FILES = [SAMPLE, INFER, SESSION, os.path.join(FSA, 'FsaLibrary.cs')]


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def read(path):
    with io.open(path, encoding='utf-8-sig', newline='') as handle:
        return handle.read()


def body_of(text, signature):
    u"""Тело метода от его подписи до закрывающей скобки того же уровня."""
    start = text.find(signature)
    if start < 0:
        return None
    brace = text.find('{', start)
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                return text[brace:i + 1]
    return None


def seed_names(text):
    u"""Имена веществ засева библиотеки — второй довод add(...) и fill(...)."""
    names = set()
    for match in re.finditer(u'add\\("[^"]*",\\s*"([^"]+)"', text):
        names.add(match.group(1))
    for match in re.finditer(u'fill\\("[^"]*",\\s*"([^"]+)"', text):
        names.add(match.group(1))
    return names


def judge(sources, names, loud=True):
    u"""Десять правил над готовыми текстами. Возвращает список находок."""
    found = []

    def say(text):
        if loud:
            print(text)

    info = sources['info']
    form = sources['form']
    sample = sources['sample']
    infer = sources['infer']
    session = sources['session']

    # --- 1. поле объявлено и умолчание безопасно ------------------------
    if u'public string %s' % FIELD not in info:
        found.append(u'1. в DeviceConfigInfo нет свойства %s' % FIELD)
    else:
        say(u'  1. свойство %s объявлено' % FIELD)

    default = re.search(u'string\\s+crystalMaterialName\\s*=\\s*("");', info)
    if default is None:
        found.append(u'1. поле crystalMaterialName не инициализировано пустой строкой — '
                     u'старая конфигурация без этого элемента получит не «пусто»')
    else:
        say(u'  1. умолчание поля — пустая строка (старый конфиг читается)')

    # --- 2. глубокая копия переносит ------------------------------------
    copy = body_of(info, u'public DeviceConfigInfo(DeviceConfigInfo info)')
    if copy is None:
        found.append(u'2. не нашёлся конструктор глубокой копии DeviceConfigInfo')
    elif u'crystalMaterialName = info.crystalMaterialName' not in copy:
        found.append(u'2. глубокая копия не переносит crystalMaterialName — '
                     u'правка формы не переживёт сохранения')
    else:
        say(u'  2. глубокая копия переносит поле')

    # --- 3. поле спецификации -------------------------------------------
    if u'public string %s' % FIELD not in sample:
        found.append(u'3. в FsaSampleSpec нет поля %s' % FIELD)
    else:
        say(u'  3. FsaSampleSpec.%s объявлено' % FIELD)

    # --- 4. ПОРЯДОК ИСТОЧНИКОВ ------------------------------------------
    order_body = body_of(sample, u'public static Dictionary<int, double> CrystalFractionsOf(')
    if order_body is None:
        found.append(u'4. не нашлось тело CrystalFractionsOf')
    else:
        geometry = order_body.find(u'spec.CrystalFractions.Count > 0')
        named = order_body.find(u'FractionsOfMaterial(spec.%s)' % FIELD)
        elements = order_body.find(u'spec.CrystalElements.Count == 0')
        if geometry < 0:
            found.append(u'4. в CrystalFractionsOf нет ветки геометрии')
        elif named < 0:
            found.append(u'4. в CrystalFractionsOf не читается поле прибора '
                         u'(FractionsOfMaterial)')
        elif elements < 0:
            found.append(u'4. в CrystalFractionsOf нет ветки восстановления по элементам')
        elif not (geometry < named < elements):
            found.append(u'4. ПОРЯДОК ИСТОЧНИКОВ НАРУШЕН: геометрия на %d, поле на %d, '
                         u'элементы на %d — геометрия обязана быть первой'
                         % (geometry, named, elements))
        else:
            say(u'  4. порядок источников: геометрия -> поле прибора -> элементы')

    # --- 5. вывод состава кладёт имя ------------------------------------
    if not re.search(u'spec\\.%s\\s*=\\s*resultData\\.DeviceConfig\\.%s' % (FIELD, FIELD), infer):
        found.append(u'5. FsaCompositionInference не кладёт имя вещества из конфигурации прибора')
    else:
        say(u'  5. вывод состава берёт имя у конфигурации прибора')

    # --- 6. путь по пикам: только ветвью else ---------------------------
    capture = body_of(session, u'Job Capture(')
    if capture is None:
        found.append(u'6. не нашлось тело FsaAnalysisSession.Capture')
    elif u'FsaSampleLibrary.FractionsOfMaterial(' not in capture:
        found.append(u'6. путь по пикам не читает вещество поля вовсе — '
                     u'у большинства людей (DbLookupsForFsa выключена) поле молчит')
    else:
        head = capture.find(u'efficiencyConfig.HasGeometry')
        branch = capture.find(u'else if (resultData.DeviceConfig != null)')
        named = capture.find(u'FsaSampleLibrary.FractionsOfMaterial(')
        if not (0 <= head < branch < named):
            found.append(u'6. вещество поля в Capture читается НЕ ветвью else от геометрии — '
                         u'геометрия перестаёт быть первичной на пути по пикам')
        else:
            say(u'  6. путь по пикам берёт поле только там, где геометрии нет')

    # --- 7. форма связана с обоих концов --------------------------------
    if u'this.LoadCrystalMaterial(config)' not in form:
        found.append(u'7. форма не наполняет список веществ (нет вызова LoadCrystalMaterial)')
    elif not re.search(u'config\\.%s\\s*=\\s*this\\.CrystalMaterialFromForm\\(\\)' % FIELD, form):
        found.append(u'7. форма не сохраняет выбранное вещество в конфигурацию')
    else:
        say(u'  7. форма связана с обоих концов: показ и запись')

    # --- 8. подписи в обоих resx ----------------------------------------
    for key in (u'crystalMaterialLabel.Text', u'crystalMaterialNotSet'):
        for label, text in ((u'resx', sources['resx']), (u'ru.resx', sources['resx_ru'])):
            if u'name="%s"' % key not in text:
                found.append(u'8. ключа %s нет в %s' % (key, label))
    if not [x for x in found if x.startswith(u'8.')]:
        say(u'  8. подписи поля и пункта «не задано» есть в обоих resx')

    # --- 9. имён веществ в коде FSA нет ---------------------------------
    guilty = []
    for path, text in sources['fsa'].items():
        for name in names:
            if u'"%s"' % name in text:
                guilty.append(u'%s: «%s»' % (os.path.basename(path), name))
    if guilty:
        found.append(u'9. имя вещества литералом в коде FSA: ' + u'; '.join(sorted(guilty)))
    else:
        say(u'  9. имён веществ в коде разбора нет (сверено %d имён засева)' % len(names))

    # --- 10. замер существует -------------------------------------------
    if not sources['probe']:
        found.append(u'10. нет пробы CrystalMaterialProbe.cs — правило без замера')
    else:
        say(u'  10. проба CrystalMaterialProbe.cs на месте')

    return found


def collect():
    need = [INFO, FORM, SAMPLE, INFER, SESSION, LIBRARY, RESX, RESX_RU]
    for path in need:
        if not os.path.exists(path):
            print(u'НЕЧЕГО ЧИТАТЬ: нет ' + path)
            sys.exit(2)

    sources = {
        'info': read(INFO),
        'form': read(FORM),
        'sample': read(SAMPLE),
        'infer': read(INFER),
        'session': read(SESSION),
        'resx': read(RESX),
        'resx_ru': read(RESX_RU),
        'probe': os.path.exists(PROBE),
        'fsa': dict((path, read(path)) for path in FSA_FILES if os.path.exists(path)),
    }
    return sources, seed_names(read(LIBRARY))


def selfcheck(sources, names):
    u"""Две порченые копии; каждая обязана быть названа."""
    print(u'')
    print(u'--- САМОПРОВЕРКА: две порченые копии ---')
    bad = 0

    # (а) источники переставлены: поле спрашивается раньше геометрии.
    spoiled = dict(sources)
    body = body_of(sources['sample'], u'public static Dictionary<int, double> CrystalFractionsOf(')
    if body is None:
        print(u'  ⛔ нечего портить: тело CrystalFractionsOf не найдено')
        return 1
    swapped = body.replace(u'spec.CrystalFractions.Count > 0', u'@@GEOMETRY@@')
    swapped = swapped.replace(u'FractionsOfMaterial(spec.%s)' % FIELD,
                              u'spec.CrystalFractions.Count > 0')
    swapped = swapped.replace(u'@@GEOMETRY@@', u'FractionsOfMaterial(spec.%s)' % FIELD)
    spoiled['sample'] = sources['sample'].replace(body, swapped)
    hits = [x for x in judge(spoiled, names, loud=False) if x.startswith(u'4.')]
    print(u'  (а) источники переставлены  -> %s' % (hits[0] if hits else u'НЕ НАЗВАНА'))
    bad += 0 if hits else 1

    # (б) умолчание поля не пусто.
    spoiled = dict(sources)
    spoiled['info'] = sources['info'].replace(
        u'string crystalMaterialName = "";', u'string crystalMaterialName = "NaI";')
    hits = [x for x in judge(spoiled, names, loud=False) if x.startswith(u'1.')]
    print(u'  (б) умолчание не пусто      -> %s' % (hits[0] if hits else u'НЕ НАЗВАНА'))
    bad += 0 if hits else 1

    return bad


def main():
    sources, names = collect()
    print(u'=== вещество кристалла у прибора: геометрия первична (A276) ===')
    found = judge(sources, names)

    bad = selfcheck(sources, names)
    if bad:
        print(u'')
        print(u'⛔ СТОРОЖ КРАСЕН: самопроверка не назвала %d порчи — дереву он не судья' % bad)
        return 1

    print(u'')
    if found:
        for line in found:
            print(u'  ⛔ ' + line)
        print(u'НЕ СОШЛОСЬ: находок %d' % len(found))
        return 1

    print(u'СОШЛОСЬ: поле прибора читается запасным источником, геометрия первична')
    return 0


if __name__ == '__main__':
    sys.exit(main())
