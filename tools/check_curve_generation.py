# -*- coding: utf-8 -*-
u"""ПОКОЛЕНИЕ КРИВОЙ ЭФФЕКТИВНОСТИ ПРОТИВ ПОКОЛЕНИЯ МАТРИЦЫ И СБОРКИ (`A119`).

## Откуда взялся

Поколений расчёта по геометрии в одной сцене ТРИ, и расходятся они независимо:

  * поколение КРИВОЙ — `phys=N` в её клейме (`EfficiencyConfigData.ComputeStamp`,
    лежит числами в конфигурации прибора);
  * поколение МАТРИЦЫ — `phys=N` в клейме файла склада `config\\device\\response`;
  * поколение СБОРКИ — `ResponseMatrix.PhysicsVersion`.

Матрица пересчитывается сама: её клеймо перестаёт сходиться при смене физики, и
`IsValidFor` отвечает «нет». Кривая — НЕТ: она переживает любое поколение
переноса, потому что это просто числа в XML. Отсюда дефект `A119`: пересчёт
одних матриц оставляет рядом кривую прежнего поколения, и увидеть это можно
только чтением клейм.

⚠ Замер 11.09.2026 по живому складу (19 матриц, 11 кривых): у кривых «Точка» и
«Маринелли» прибора `1.Atom Spectra Nano 16 Pro RadiaScan 701A` клеймо
`phys=11` при матрицах поколения 16 — разрыв в ПЯТЬ поколений в одной сцене.
Числа самой строки `A119` (13, 13, 12) к этому дню устарели: склад с 04.09
пересчитывался, а эти две кривые остались.

⛔ Класс дефекта, ради которого написан сторож, — НЕ «приложение не умеет
сравнивать». Это видно первым же запуском. Опасно обратное: сравнение уберут
или обездвижат (передадут поколение числом, перестанут спрашивать склад,
оставят строку без перевода) — и вкладка снова замолчит, а молчание неотличимо
от «всё сошлось».

## Что проверяется в ДЕРЕВЕ (всегда)

1. клеймо кривой печатается ОДНИМ местом (`EfficiencyCalculation.Run`) и берёт
   `ResponseMatrix.PhysicsVersion`, а не число;
2. `DeviceConfigForm.GenerationNotes` объявлен СТАТИЧЕСКИМ (иначе его не
   измерить без окна) и разбирает клеймо `ResponseMatrix.PhysicsFromStamp`;
3. ⛔ в теле `GenerationNotes` НЕТ ЛИТЕРАЛЬНОГО НОМЕРА ПОКОЛЕНИЯ: сравнение идёт
   с доводом, иначе при подъёме физики правило окаменеет молча;
4. `UpdateEfficiencyView` ЗОВЁТ `GenerationNotes` и передаёт ему
   `ResponseMatrix.PhysicsVersion`;
5. ⛔ `UpdateEfficiencyView` СПРАШИВАЕТ СКЛАД (`ResponseMatrixStore.PeekVersions`):
   без этого поколение матрицы всегда ноль и половина правила мертва — а
   молчит она ровно так же, как согласная пара;
6. `ResponseMatrixStore.PeekVersions` есть и делегирует
   `ResponseMatrix.PeekVersions` (складывать путь склада руками потребитель не
   должен: сложивший сам однажды сложит иначе);
7. обе подписи лежат в ОБОИХ `Properties/Resources*.resx` и несут ДВА места
   подстановки — `{0}` и `{1}`;
8. обе объявлены в `Resources.Designer.cs`;
9. замер существует: `tools/effmaker/probes/CurveGenerationProbe.cs` на месте;
10. ⛔ сообщения идут СВОЕЙ подписью, а не хвостом к сводке: сводка живёт в
    подписи 466×30 с `AutoSize = false` и уже занимает две строки — дописанное
    к ней ушло бы за нижний край МОЛЧА;
11. ⛔ высота этой подписи СЧИТАЕТСЯ `TextRenderer.MeasureText` от настоящего
    текста, а не пишется числом: русская пара обеих подписей длиннее английской
    на треть, и число устарело бы на первом же переводе.

## Что проверяется в СКЛАДЕ (по ключу)

    python tools/check_curve_generation.py --store="<каталог с config\\device>"

Читает клейма кривых из `config\\device\\*.xml` и заголовки `.rmx` из
`config\\device\\response`, сверяет поколения кривой, её матрицы и сборки.
Расхождение — код 1 с поимённым перечнем.

⚠ Каталог склада НЕ УГАДЫВАЕТСЯ: он у каждого свой и лежит вне дерева. Без
ключа сторож судит только дерево и потому зелен везде. Читатель живого склада —
САМО ПРИЛОЖЕНИЕ: вкладка «Эффективность» с 11.09.2026 называет расхождение
словами (это и есть починка `A119`), а сторож держит то, чтобы её не сняли.

## Самопроверка

⛔ Все одиннадцать правил прошли бы и на пустом чтении, поэтому на каждом прогоне
сторож судит СЕМЬ ПОРЧЕНЫХ КОПИЙ дерева и ДВЕ подложенные сцены склада.
Каждая обязана быть названа поимённо; не назвал — сторож красный, что бы ни
показало дерево. Среди подложенных сцен есть СОГЛАСНАЯ (все поколения равны):
она обязана пройти молча, иначе правило «говорить всегда» тоже сошло бы за
работающее.

Коды возврата: 0 — сошлось; 1 — не сошлось; 2 — нечего читать.
"""
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(REPO, 'BecquerelMonitor')
EFFDIR = os.path.join(APP, 'EfficiencyMaker')

CALC = os.path.join(EFFDIR, 'EfficiencyCalculation.cs')
MATRIX = os.path.join(EFFDIR, 'ResponseMatrix.cs')
STORE = os.path.join(EFFDIR, 'ResponseMatrixStore.cs')
TAB = os.path.join(APP, 'DeviceConfigForm.Efficiency.cs')
RESX = os.path.join(APP, 'Properties', 'Resources.resx')
RESX_RU = os.path.join(APP, 'Properties', 'Resources.ru.resx')
DESIGNER = os.path.join(APP, 'Properties', 'Resources.Designer.cs')
PROBE = os.path.join(REPO, 'tools', 'effmaker', 'probes', 'CurveGenerationProbe.cs')

STRINGS = [u'EfficiencyTabCurveOldPhysics', u'EfficiencyTabCurveVsMatrix']


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


def args_of(text, start):
    u"""Доводы вызова от его открывающей скобки до парной ей; None — не вызов."""
    opening = text.find(u'(', start)
    if opening < 0:
        return None
    depth = 0
    for i in range(opening, len(text)):
        if text[i] == u'(':
            depth += 1
        elif text[i] == u')':
            depth -= 1
            if depth == 0:
                return text[opening:i + 1]
    return None


def strip_comments(text):
    u"""Без комментариев: в них номера поколений стоят законно и часто."""
    text = re.sub(u'/\\*.*?\\*/', u' ', text, flags=re.S)
    return re.sub(u'//[^\n]*', u' ', text)


def resx_value(text, name):
    u"""Значение строки ресурса; None — строки нет."""
    match = re.search(u'<data name="%s"[^>]*>\\s*<value>(.*?)</value>' % re.escape(name),
                      text, re.S)
    return match.group(1) if match else None


# ----------------------------------------------------------------------
# Одиннадцать правил над деревом
# ----------------------------------------------------------------------

def judge(sources, loud=True):
    found = []

    def say(text):
        if loud:
            print(text)

    calc = sources['calc']
    store = sources['store']
    tab = sources['tab']

    # --- 1. клеймо кривой берёт версию сборки ---------------------------
    #
    # ⚠ Довод берётся БАЛАНСИРОВКОЙ СКОБОК, а не «до первой точки с запятой»:
    # точка с запятой стоит внутри самой строки образца (`phys={0}; hist=…`),
    # и наивная нарезка обрывала вызов ПЕРЕД доводами — сторож объявлял находку
    # на здоровом дереве (поймано первым же прогоном 11.09.2026).
    at = calc.find(u'result.ComputeStamp = string.Format(')
    stamp = args_of(calc, at) if at >= 0 else None
    if stamp is None:
        found.append(u'1. клеймо кривой не печатается: нет `result.ComputeStamp = string.Format(`')
    elif u'ResponseMatrix.PhysicsVersion' not in stamp:
        found.append(u'1. клеймо кривой не берёт `ResponseMatrix.PhysicsVersion` — поколение окаменеет')
    else:
        say(u'  1. клеймо кривой печатается версией сборки')

    # --- 2. решение статическое и разбирает клеймо ----------------------
    sign = u'internal static List<string> GenerationNotes('
    notes = body_of(tab, sign)
    if notes is None:
        found.append(u'2. нет статического `%s…`: решение нечем измерить без окна' % sign.strip())
    else:
        if u'ResponseMatrix.PhysicsFromStamp' not in notes:
            found.append(u'2. `GenerationNotes` не разбирает клеймо `ResponseMatrix.PhysicsFromStamp`')
        else:
            say(u'  2. решение статическое, клеймо разбирается общим приёмом')

    # --- 3. литерала поколения в решении нет ----------------------------
    if notes is not None:
        bare = strip_comments(notes)
        # Числа в подстановках и индексах законны; ищем сравнение с числом.
        bad = re.findall(u'(?:curvePhysics|buildPhysics|matrixPhysics)\\s*(?:!=|==|<|>|<=|>=)\\s*(\\d+)',
                         bare)
        bad = [x for x in bad if x != u'0']
        if bad:
            found.append(u'3. в `GenerationNotes` сравнение с ЛИТЕРАЛОМ поколения: %s'
                         % u', '.join(sorted(set(bad))))
        else:
            say(u'  3. литерального номера поколения в решении нет')

    # --- 4. вкладка зовёт решение и даёт ему версию сборки --------------
    view = body_of(tab, u'void UpdateEfficiencyView()')
    if view is None:
        found.append(u'4. нет `UpdateEfficiencyView()` — подпись вкладки собирается не здесь')
    else:
        # ⚠ `(?<!Show)` — не украшение: `ShowGenerationNotes(` СОДЕРЖИТ в себе
        # `GenerationNotes(`, и поиск подстрокой прошёл бы на дереве, где
        # решение не зовут вовсе, а показывают пустоту (поймано самопроверкой
        # 11.09.2026, порча «(а) вызов решения снят» осталась неназванной).
        call = re.search(u'(?<!Show)GenerationNotes\\(([^;]*?)\\)\\s*\\)', view, re.S)
        if re.search(u'(?<!Show)GenerationNotes\\(', view) is None:
            found.append(u'4. `UpdateEfficiencyView` не зовёт `GenerationNotes` — вкладка молчит')
        elif call is None or u'ResponseMatrix.PhysicsVersion' not in call.group(1):
            found.append(u'4. `GenerationNotes` зовётся НЕ с `ResponseMatrix.PhysicsVersion`')
        else:
            say(u'  4. вкладка зовёт решение и даёт ему версию сборки')

    # --- 5. вкладка спрашивает склад ------------------------------------
    if view is not None:
        if u'ResponseMatrixStore.PeekVersions(' not in strip_comments(view):
            found.append(u'5. `UpdateEfficiencyView` не спрашивает склад '
                         u'(`ResponseMatrixStore.PeekVersions`) — поколение матрицы всегда 0')
        else:
            say(u'  5. вкладка спрашивает склад о поколении матрицы')

    # --- 6. обёртка склада есть и делегирует ----------------------------
    peek = body_of(store, u'public static bool PeekVersions(string efficiencyGuid')
    if peek is None:
        found.append(u'6. нет `ResponseMatrixStore.PeekVersions` — путь склада складывают руками')
    elif u'ResponseMatrix.PeekVersions(PathOf(' not in peek:
        found.append(u'6. `ResponseMatrixStore.PeekVersions` не делегирует `ResponseMatrix.PeekVersions(PathOf(…))`')
    else:
        say(u'  6. обёртка склада делегирует общему чтению заголовка')

    # --- 7. подписи в ОБОИХ resx, по два места подстановки --------------
    for name in STRINGS:
        for tag, text in ((u'английской', sources['resx']), (u'русской', sources['resx_ru'])):
            value = resx_value(text, name)
            if value is None:
                found.append(u'7. `%s` нет в %s поставке ресурсов' % (name, tag))
            elif u'{0}' not in value or u'{1}' not in value:
                found.append(u'7. `%s` в %s поставке без двух мест подстановки' % (name, tag))
    if not [x for x in found if x.startswith(u'7.')]:
        say(u'  7. обе подписи в обоих resx, по два места подстановки')

    # --- 8. объявлены в Designer ----------------------------------------
    missing = [n for n in STRINGS
               if u'GetString("%s"' % n not in sources['designer']]
    if missing:
        found.append(u'8. в `Resources.Designer.cs` нет: %s' % u', '.join(missing))
    else:
        say(u'  8. обе подписи объявлены в Designer')

    # --- 9. замер существует --------------------------------------------
    if not sources['probe']:
        found.append(u'9. нет пробы `tools/effmaker/probes/CurveGenerationProbe.cs`')
    else:
        say(u'  9. замер на месте: CurveGenerationProbe.cs')

    # --- 10. сообщения идут СВОЕЙ подписью, а не хвостом к сводке --------
    if view is not None:
        bare = strip_comments(view)
        if u'ShowGenerationNotes(' not in bare:
            found.append(u'10. `UpdateEfficiencyView` не зовёт `ShowGenerationNotes` — '
                         u'сообщение некуда положить')
        elif re.search(u'parts\\.AddRange\\(\\s*GenerationNotes\\(', bare):
            found.append(u'10. сообщения дописываются в сводку (`parts`) — подпись 466×30 '
                         u'обрежет их МОЛЧА')
        else:
            say(u'  10. сообщения идут своей подписью, а не хвостом к сводке')

    # --- 11. высота подписи СЧИТАЕТСЯ, а не пишется числом ---------------
    show = body_of(tab, u'void ShowGenerationNotes(')
    measure = body_of(tab, u'internal static int GenerationLabelHeight(')
    if show is None or measure is None:
        found.append(u'11. нет `ShowGenerationNotes` или `GenerationLabelHeight` — '
                     u'высоту подписи считать нечем')
    elif u'GenerationLabelHeight(' not in strip_comments(show):
        found.append(u'11. `ShowGenerationNotes` не считает высоту `GenerationLabelHeight`')
    elif u'TextRenderer.MeasureText' not in strip_comments(measure):
        found.append(u'11. `GenerationLabelHeight` не меряет текст `TextRenderer.MeasureText` — '
                     u'высота числом устареет на первом же переводе')
    else:
        say(u'  11. высота подписи считается по настоящему тексту')

    return found


# ----------------------------------------------------------------------
# Правило склада
# ----------------------------------------------------------------------

def judge_store(curves, matrices, build_physics):
    u"""Поколения кривой, её матрицы и сборки.

    `curves` — список (прибор, имя, guid, поколение кривой); поколение 0 значит
    «клейма нет», такую кривую судить не за что. `matrices` — guid -> (формат,
    поколение). Возвращает список находок.
    """
    found = []
    for (device, name, guid, curve_phys) in curves:
        if curve_phys <= 0:
            continue
        if curve_phys != build_physics:
            found.append(u'кривая «%s» (%s): поколение %d, сборка %d'
                         % (name, device, curve_phys, build_physics))
        pair = matrices.get((guid or u'').lower())
        if pair is None:
            continue
        matrix_phys = pair[1]
        if matrix_phys > 0 and matrix_phys != curve_phys:
            found.append(u'кривая «%s» (%s): поколение %d, а её матрица — %d'
                         % (name, device, curve_phys, matrix_phys))
    return found


def phys_from_stamp(stamp):
    u"""Тот же разбор, что `ResponseMatrix.PhysicsFromStamp`."""
    if not stamp or not stamp.startswith(u'phys='):
        return 0
    end = stamp.find(u';')
    if end <= 5:
        return 0
    try:
        return int(stamp[5:end])
    except ValueError:
        return 0


def read_rmx_header(path):
    u"""Метка, формат и клеймо из заголовка — так же, как `PeekVersions`."""
    with open(path, 'rb') as handle:
        if handle.read(4) != b'BQRM':
            return None, None
        fmt = int.from_bytes(handle.read(4), 'little', signed=True)
        length = 0
        shift = 0
        while True:
            byte = handle.read(1)
            if not byte:
                return fmt, None
            byte = byte[0]
            length |= (byte & 0x7F) << shift
            if not byte & 0x80:
                break
            shift += 7
        return fmt, handle.read(length).decode('utf-8', 'replace')


def collect_store(root):
    u"""Кривые и матрицы каталога поставки прибора."""
    devdir = os.path.join(root, 'config', 'device')
    if not os.path.isdir(devdir):
        devdir = root
    if not os.path.isdir(devdir):
        return None, None
    curves = []
    for name in sorted(os.listdir(devdir)):
        if not name.lower().endswith('.xml'):
            continue
        try:
            tree = ET.parse(os.path.join(devdir, name))
        except ET.ParseError:
            continue
        for node in tree.iter():
            if not node.tag.endswith('EfficiencyConfigData'):
                continue
            guid = node.find('Guid')
            title = node.find('Name')
            stamp = node.find('ComputeStamp')
            curves.append((name,
                           title.text if title is not None else u'',
                           guid.text if guid is not None else u'',
                           phys_from_stamp(stamp.text if stamp is not None else u'')))
    matrices = {}
    resp = os.path.join(devdir, 'response')
    if os.path.isdir(resp):
        for name in sorted(os.listdir(resp)):
            if not name.lower().endswith('.rmx'):
                continue
            fmt, stamp = read_rmx_header(os.path.join(resp, name))
            if fmt is None:
                continue
            matrices[name[:-4].lower()] = (fmt, phys_from_stamp(stamp))
    return curves, matrices


def build_physics(text):
    u"""`ResponseMatrix.PhysicsVersion` — числом из исходника, не из памяти."""
    match = re.search(u'const int PhysicsVersion\\s*=\\s*(\\d+)', text)
    return int(match.group(1)) if match else 0


# ----------------------------------------------------------------------

def collect():
    need = [CALC, MATRIX, STORE, TAB, RESX, RESX_RU, DESIGNER]
    for path in need:
        if not os.path.exists(path):
            print(u'НЕЧЕГО ЧИТАТЬ: нет ' + path)
            sys.exit(2)
    return {
        'calc': read(CALC),
        'matrix': read(MATRIX),
        'store': read(STORE),
        'tab': read(TAB),
        'resx': read(RESX),
        'resx_ru': read(RESX_RU),
        'designer': read(DESIGNER),
        'probe': os.path.exists(PROBE),
    }


def selfcheck(sources):
    u"""Семь порченых копий дерева и две подложенные сцены склада."""
    print(u'')
    print(u'--- САМОПРОВЕРКА: семь порченых копий и две сцены склада ---')
    bad = 0

    def spoil(title, key, before, after, rule):
        spoiled = dict(sources)
        if before not in sources[key]:
            print(u'  %-38s -> ⛔ НЕЧЕГО ПОРТИТЬ: не найдено «%s»' % (title, before))
            return 1
        spoiled[key] = sources[key].replace(before, after, 1)
        hits = [x for x in judge(spoiled, loud=False) if x.startswith(rule)]
        print(u'  %-38s -> %s' % (title, hits[0] if hits else u'НЕ НАЗВАНА'))
        return 0 if hits else 1

    bad += spoil(u'(а) вызов решения снят', 'tab',
                 u'ShowGenerationNotes(GenerationNotes(', u'ShowGenerationNotes(NoNotes(', u'4.')
    bad += spoil(u'(б) поколение сборки числом', 'tab',
                 u'ResponseMatrix.PhysicsVersion));', u'16));', u'4.')
    bad += spoil(u'(в) склад не спрашивается', 'tab',
                 u'ResponseMatrixStore.PeekVersions(config.Guid',
                 u'NoStore.PeekVersions(config.Guid', u'5.')
    bad += spoil(u'(г) клеймо кривой числом', 'calc',
                 u'ResponseMatrix.PhysicsVersion, simulator.Histories',
                 u'16, simulator.Histories', u'1.')
    bad += spoil(u'(д) сообщения хвостом к сводке', 'tab',
                 u'this.ShowGenerationNotes(GenerationNotes(',
                 u'parts.AddRange(GenerationNotes(', u'10.')
    bad += spoil(u'(е) высота подписи числом', 'tab',
                 u'return TextRenderer.MeasureText(', u'return 30; //', u'11.')
    bad += spoil(u'(ё) русской подписи нет', 'resx_ru',
                 u'<data name="EfficiencyTabCurveVsMatrix"',
                 u'<data name="EfficiencyTabCurveVsMatrixXX"', u'7.')

    # Сцены склада: подложенное расхождение обязано быть названо, а согласная
    # сцена — пройти молча (иначе «говорить всегда» тоже сошло бы за работу).
    curves = [(u'дежурный.xml', u'Цилиндр', u'g-1', 11),
              (u'дежурный.xml', u'Точка', u'g-2', 16),
              (u'дежурный.xml', u'По измерениям', u'g-3', 0)]
    matrices = {u'g-1': (8, 16), u'g-2': (8, 16)}
    hits = judge_store(curves, matrices, 16)
    print(u'  %-38s -> %s' % (u'(ж) склад с расхождением',
                              u'; '.join(hits) if hits else u'НЕ НАЗВАНА'))
    bad += 0 if len(hits) == 2 else 1

    ok_curves = [(u'дежурный.xml', u'Цилиндр', u'g-1', 16),
                 (u'дежурный.xml', u'По измерениям', u'g-3', 0)]
    hits = judge_store(ok_curves, {u'g-1': (8, 16)}, 16)
    print(u'  %-38s -> %s' % (u'(з) склад согласный (контроль)',
                              u'молчит' if not hits else u'⛔ ЗАГОВОРИЛ: ' + u'; '.join(hits)))
    bad += 0 if not hits else 1

    return bad


def main(argv):
    store_root = None
    for arg in argv[1:]:
        if arg.startswith('--store='):
            store_root = arg[len('--store='):]
        else:
            print(u'ОТКАЗ: неизвестный ключ %s' % arg)
            return 2

    sources = collect()
    print(u'=== поколение кривой против поколения матрицы и сборки (A119) ===')
    phys = build_physics(sources['matrix'])
    print(u'  поколение сборки: %d' % phys)
    found = judge(sources)

    bad = selfcheck(sources)
    if bad:
        print(u'')
        print(u'⛔ СТОРОЖ КРАСЕН: самопроверка не назвала %d порчи — дереву он не судья' % bad)
        return 1

    if store_root:
        print(u'')
        print(u'--- СКЛАД: %s ---' % store_root)
        curves, matrices = collect_store(store_root)
        if curves is None:
            print(u'  НЕЧЕГО ЧИТАТЬ: нет каталога приборов')
            return 2
        print(u'  кривых %d, матриц %d' % (len(curves), len(matrices)))
        for line in judge_store(curves, matrices, phys):
            found.append(u'склад: ' + line)

    print(u'')
    if found:
        for line in found:
            print(u'  ⛔ ' + line)
        print(u'НЕ СОШЛОСЬ: находок %d' % len(found))
        return 1

    print(u'СОШЛОСЬ: расхождение поколений названо словами и сравнение не обездвижено')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
