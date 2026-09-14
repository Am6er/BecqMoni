# -*- coding: utf-8 -*-
u"""Сторож ПЕЧАТИ ЧИСЕЛ в оснастке `tools/effmaker` (`T245`, `T247`).

## Что судится и почему

Правило Amber 05.09.2026 про числа состоит из двух половин, и обе половины
здесь читаются машинно, потому что компилятор не видит ни одной.

### 1. Разделитель дробной части лечится ИНВАРИАНТОМ, а не подменой (`T245`)

Дефектный приём выглядит так:

    culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
    culture.NumberFormat.NumberDecimalSeparator = ".";
    Thread.CurrentThread.CurrentCulture = culture;

Он чинит ПЕЧАТЬ и оставляет РАЗБОР системным, а `CurrentCulture` управляет
обеими сторонами: файл, записанный с точкой и прочитанный с запятой, даёт не
отказ, а ДРУГОЕ ЧИСЛО, тихо. Лечение — культура целиком инвариантная
(`CultureInfo.CurrentCulture = CultureInfo.InvariantCulture`), как у соседей
по каталогу.

⛔ Скопом это правило не применимо, и в том вся сложность: у части проб подмена
разделителя и ЕСТЬ их предмет — они воспроизводят костыль `MainForm.cs:158-160`,
чтобы показать, что видит человек на русской системе. Слепая замена стёрла бы
ровно то, что такая проба меряет. Поэтому законное место помечается в коде
меткой `T245: НАРОЧНО` в комментарии над ним; всё непомеченное — нарушение.

⚠ Метка требуется РЯДОМ (в пределах `MARK_WINDOW` строк выше), а не где-нибудь
в файле: файл `CultureProbeO14.cs` держит и законные подмены, и обычную печать,
и метка «где-то в файле» разрешила бы там что угодно.

### 2. Группировки разрядов нет вовсе, значит нет и `P` (`T247`)

Решение Amber 05.09.2026: `n1`/`n2` -> `f1`/`f2`, печатать `1234.50`, ни
запятой, ни пробела в группах. Формат `P` про это молчит до поры: он ставит
разделитель разрядов только выше 1000 %, и потому дефект ЛАТЕНТЕН — но именно
он дал «худший 10,000.00 %» в закрытой `T246`. Лечение: `F` со знаком процента
текстом, множитель 100.0 у аргумента, явная инвариантная культура.

⛔ Живых записей ТРИ, а не две: подстановка `{0:P2}`, `ToString("P2")` и формат,
отданный АРГУМЕНТОМ чужому печатнику (`GridStep(x, n, "P3", "")`). Третью
завела полоса П8 10.09.2026, поймав ровно такое место в `CorpusFsaProbe.cs:957`,
которого сторож не видел, — остаток `T247` был на одно место больше объявленного.
Аргумент судится только в позиции аргумента: то же самое присваиванием — это имя
(`cfg.Name = "P3"`), и ложная тревога ослепила бы сторожа.

⚠ Правило `P` намеренно НЕ распространено на `N`: `Guid.ToString("N")` — это
не числовой формат, а форма записи GUID, и таких мест в каталоге больше
десятка. Отделять их от числовых `n2` надо разбором типа аргумента, а не
образцом по тексту; здесь это не делается, чтобы сторож не начал тревожить
ложно (сторож, тревожащий ложно, ослепнет за неделю).

### 3. Проба, которая печатает число, обязана ПОСТАВИТЬ КУЛЬТУРУ (`T247`, ост. 2)

Первые две половины судят, ЧЕМ печатают; эта — печатают ли вообще с культурой.
`Console.WriteLine("{0:F2}", x)` берёт `CultureInfo.CurrentCulture`, и на
русской машине выводит `0,50` — включая `--csv=`, который потом кто-то
разбирает. Законных способов два, и оба здесь принимаются: культура потока
целиком инвариантная в точке входа (`CurrentCulture = CultureInfo.InvariantCulture`)
либо `CultureInfo.InvariantCulture` поставщиком В ТОМ ЖЕ ВЫЗОВЕ, что печатает.

⚠ Судится ВЫЗОВ, а не строка и не оператор: в одном операторе рядом живут
`Console.WriteLine("{0:F2}", …)` и чужой `ToString(…, InvariantCulture)`, и
пооператорный счёт объявил бы такое место чистым. Замер 10.09.2026 (полоса П8):
пооператорно выходило 5 голых мест, по вызовам — 224 в 28 файлах.

⛔ И здесь скопом нельзя, по той же причине, что в половине 1: у трёх проб
культура — ПРЕДМЕТ замера (`NumericCultureProbeF68` подменяет её сама и печатает
её имя, `ImportEmptyConfigProbeF23` печатает разделитель потока, `CultureProbeO14`
воспроизводит костыль `MainForm`). Они судятся общим правилом и проходят его
законно: у каждого своего числа поставщик стоит явно.

## Область

`tools/effmaker/**/*.cs` — вся оснастка, а не одни пробы: печать числа сидит и
в довесках без точки входа, и они ходят в каждую пробу.

  python tools/check_probe_numbers.py [--selftest]

Коды возврата:
  0 — нарушений нет, самопроверка поймала все подставленные порчи;
  1 — правило нарушено (нарушители названы файлом и строкой);
  2 — самопроверка не прошла: сторож слеп к подставленной порче;
  3 — судить нечего (каталога оснастки нет, или в нём ни одного `.cs`).

Печать без знаков вне cp1251: консоль здесь cp1251, и `⛔`/`⚠` в ней
превращаются в `?` — кода возврата это не меняет, а читателя лишает (`T219`).
"""

import io
import os
import re
import sys

ROOT = os.path.join(u'tools', u'effmaker')

# Присвоение разделителя дробной части — тот самый дефектный приём.
ASSIGN = re.compile(r'NumberDecimalSeparator\s*=\s*["\']')

# Метка законного места. Ищется в комментарии, потому что доводу место в
# комментарии; в коде она была бы строкой и попала бы в вывод пробы.
MARK = re.compile(r'//.*T245\s*:\s*НАРОЧНО')

# Сколько строк выше присвоения ищется метка. Шесть — довод плюс сам клон:
# больше окно — и метка начнёт разрешать соседнее, ниже — и не влезет
# двухстрочный довод над двухстрочным клоном.
MARK_WINDOW = 6

# `P`-формат в двух живых записях: подстановка `{N:P2}` и `ToString("P2")`.
# Регистр обеих букв важен: `p2` работает так же, как `P2`.
PFORMAT = re.compile(r'\{[^{}]*:[Pp][0-9]*\}|ToString\(\s*"[Pp][0-9]*"')

# ⛔ ТРЕТЬЯ ЖИВАЯ ЗАПИСЬ: формат отдан АРГУМЕНТОМ, а не стоит в подстановке.
# Заведена 10.09.2026 полосой П8: ровно такой `P` сидел в
# `probes/CorpusFsaProbe.cs:957` — `GridStep(head.GainRange, head.GainSteps,
# "P3", "")`, где `GridStep` печатает шаг сетки этим форматом, — и обе записи
# выше его НЕ ВИДЕЛИ. То есть остаток `T247` был на одно место больше, чем
# показывал сторож, и узнать это можно было только чтением глазами.
#
# ⚠ Судится ТОЛЬКО позиция аргумента (`, "P3",` либо `, "P3")`). То же самое
# присваиванием — это имя, а не формат (`probes/CultureProbeO14.cs`:
# `cfg.Name = "P3";`), и ложная тревога ослепила бы сторожа за неделю. На
# дереве 10.09.2026 образец даёт НОЛЬ совпадений после правки и ровно одно до
# неё — то есть ловит дефект и не ловит соседей.
PARGUMENT = re.compile(r',\s*"[Pp][0-9]*"\s*[,)]')

# --- половина 3: печать числа без поставщика культуры ----------------------

# Точка входа пробы. Судится только она: довесок без `Main` культуру потока не
# ставит и ставить не должен — её ставит тот, кто запустился.
ENTRY = re.compile(r'static\s+(?:async\s+)?(?:int|void)\s+Main\s*\(')

# Культура потока целиком инвариантная — первый законный способ.
THREAD_INVARIANT = re.compile(r'CurrentCulture\s*=\s*CultureInfo\.InvariantCulture')

# Место форматной печати числа. Чувствительны к разделителю `F E G N P` и свои
# образцы из `0`/`#`; `D` и `X` — целочисленные, их тут нет нарочно.
# ⚠ `ToString("N")` БЕЗ ЦИФР — это `Guid.ToString("N")`, форма записи GUID, а не
# числовой формат; поэтому у `N` цифра обязательна (тот же довод, что в половине 2).
NUMFORMAT = re.compile(r'\{\d+[,\-\d]*:(?:[FfEeGgPp][0-9]*|[Nn][0-9]+|[0#][0#.,;%eE+\-]*)\}'
                       r'|ToString\(\s*"(?:[FfEeGgPp][0-9]*|[Nn][0-9]+|[0#][0#.,;%eE+\-]*)"\s*\)')

PROVIDER = u'CultureInfo.InvariantCulture'


def read(path):
    fh = io.open(path, u'r', encoding=u'utf-8', newline=u'')
    try:
        return fh.read()
    finally:
        fh.close()


def sources(root):
    u"""Все `.cs` оснастки: (относительное имя, текст). Порядок устойчивый."""
    out = []
    for base, dirs, names in os.walk(root):
        dirs[:] = sorted(d for d in dirs if not d.startswith(u'build'))
        for name in sorted(names):
            if name.endswith(u'.cs'):
                path = os.path.join(base, name)
                out.append((os.path.relpath(path, root).replace(u'\\', u'/'),
                            read(path)))
    return out


def strip_comments(text):
    u"""Текст без комментариев, длина СОХРАНЯЕТСЯ (пробелы вместо вырезанного).

    Нужно обоим читателям половины 3: `//   * печать — value.ToString("F2", …)`
    в шапке `NumericCultureProbeF68` — это ОПИСАНИЕ чужого кода, и без вырезания
    комментариев сторож объявлял бы его нарушением. Длина сохраняется, чтобы
    номера строк и позиции оставались номерами ИСХОДНОГО файла.
    """
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == u'"':
            j = i + 1
            while j < n:
                if text[j] == u'\\':
                    j += 2
                    continue
                if text[j] == u'"':
                    break
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
            continue
        if c == u'/' and i + 1 < n and text[i + 1] == u'/':
            j = text.find(u'\n', i)
            j = n if j < 0 else j
            out.append(u' ' * (j - i))
            i = j
            continue
        if c == u'/' and i + 1 < n and text[i + 1] == u'*':
            j = text.find(u'*/', i)
            j = n if j < 0 else j + 2
            # Переводы строк внутри блочного комментария СОХРАНЯЮТСЯ: иначе
            # съедут номера строк у всего, что ниже.
            out.append(u''.join(ch if ch == u'\n' else u' ' for ch in text[i:j]))
            i = j
            continue
        out.append(c)
        i += 1
    return u''.join(out)


def enclosing_call(text, pos):
    u"""Текст ближайшего охватывающего вызова `(…)` вокруг позиции.

    ⛔ Судить надо ВЫЗОВ, а не оператор: `Console.WriteLine("{0:F2}", Foo(x))`
    и соседний `bar.ToString(CultureInfo.InvariantCulture)` живут в одном
    операторе сплошь и рядом, и пооператорная проверка объявляла бы первый
    чистым. Замер 10.09.2026 развёл эти два счёта: 5 мест против 224.
    """
    depth, i = 0, pos
    while i >= 0:
        if text[i] == u')':
            depth += 1
        elif text[i] == u'(':
            if depth == 0:
                break
            depth -= 1
        i -= 1
    if i < 0:
        return text[max(0, pos - 200):pos + 200]
    start, depth, j, n = i, 0, i, len(text)
    while j < n:
        if text[j] == u'(':
            depth += 1
        elif text[j] == u')':
            depth -= 1
            if depth == 0:
                return text[start:j + 1]
        j += 1
    return text[start:]


def judge_culture(text):
    u"""Половина 3: места печати числа без поставщика культуры.

    Пусто, если файл — не точка входа либо культура потока уже инвариантна.
    """
    if not ENTRY.search(text) or THREAD_INVARIANT.search(text):
        return []

    clean = strip_comments(text)
    bad = []
    for m in NUMFORMAT.finditer(clean):
        if PROVIDER in enclosing_call(clean, m.start()):
            continue
        bad.append((clean.count(u'\n', 0, m.start()) + 1, u'T247',
                    u'число печатается форматом «%s» БЕЗ поставщика культуры:'
                    u' на русской машине выйдет запятая. Либо'
                    u' CultureInfo.CurrentCulture = CultureInfo.InvariantCulture'
                    u' в точке входа, либо InvariantCulture в этом же вызове'
                    % m.group(0)))
    return bad


def judge(text):
    u"""Нарушения одного файла: список (номер строки, разряд, довод)."""
    lines = text.split(u'\n')
    bad = list(judge_culture(text))

    for i, line in enumerate(lines):
        if ASSIGN.search(line):
            window = lines[max(0, i - MARK_WINDOW):i]
            if not any(MARK.search(w) for w in window):
                bad.append((i + 1, u'T245',
                            u'подмена разделителя дробной части без метки'
                            u' «T245: НАРОЧНО» рядом: культура ставится клоном'
                            u' системной, и РАЗБОР остаётся системным'))

        for m in PFORMAT.finditer(line):
            bad.append((i + 1, u'T247',
                        u'формат «%s» несёт группировку разрядов выше 1000 %%:'
                        u' нужен F с множителем 100.0 и знаком процента текстом'
                        % m.group(0)))

        for m in PARGUMENT.finditer(line):
            bad.append((i + 1, u'T247',
                        u'формат «%s» отдан АРГУМЕНТОМ и печатается где-то ещё:'
                        u' та же группировка выше 1000 %%, нужен F с множителем'
                        u' 100.0 у значения и знаком процента текстом'
                        % m.group(0).strip()))

    return bad


def selftest(root):
    u"""ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: подставленная порча обязана быть поймана.

    Портятся НАСТОЯЩИЕ файлы дерева, в памяти. Сторож, проверенный на
    выдуманном тексте, меряет выдумку, а не себя.
    """
    missed = []

    def take(name):
        path = os.path.join(root, u'probes', name)
        return path if os.path.exists(path) else None

    # (а) Законное место: с меткой чисто, без метки — отказ.
    legal = take(u'CultureProbeO14.cs')
    if legal is None:
        return [u'самопроверке не на чем работать: нет probes/CultureProbeO14.cs']

    good = read(legal)
    if [b for b in judge(good) if b[1] == u'T245']:
        missed.append(u'помеченное законное место в CultureProbeO14.cs объявлено'
                      u' нарушением — сторож ложно тревожит')

    stripped = re.sub(r'^[ \t]*//.*T245\s*:\s*НАРОЧНО.*\r?\n', u'', good, flags=re.M)
    if stripped == good:
        missed.append(u'порча «метка снята» не подставилась: текст не изменился')
    elif not [b for b in judge(stripped) if b[1] == u'T245']:
        missed.append(u'порча «метка снята» НЕ ПОЙМАНА')

    # (б) Починенный файл: возвращаем ему дефектный приём.
    fixed = take(u'FsaFlagsProbe.cs')
    if fixed is None:
        return missed + [u'самопроверке не на чем работать: нет probes/FsaFlagsProbe.cs']

    clean = read(fixed)
    if judge(clean):
        missed.append(u'починенный FsaFlagsProbe.cs объявлен нарушением —'
                      u' сторож ложно тревожит')

    spoiled = clean.replace(
        u'Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;',
        u'CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();\r\n'
        u'            culture.NumberFormat.NumberDecimalSeparator = ".";\r\n'
        u'            Thread.CurrentThread.CurrentCulture = culture;', 1)
    if spoiled == clean:
        missed.append(u'порча «подмена вернулась» не подставилась: текст не изменился')
    elif not [b for b in judge(spoiled) if b[1] == u'T245']:
        missed.append(u'порча «подмена вернулась» НЕ ПОЙМАНА')

    # (в) `P`-формат в обеих живых записях.
    pf = take(u'ResponseChannelProbe.cs')
    if pf is None:
        return missed + [u'самопроверке не на чем работать: нет probes/ResponseChannelProbe.cs']

    pclean = read(pf)
    if [b for b in judge(pclean) if b[1] == u'T247']:
        missed.append(u'починенный ResponseChannelProbe.cs объявлен нарушением —'
                      u' сторож ложно тревожит')

    for what, spoiled in [
            (u'вернулась подстановка {0:P2}',
             pclean.replace(u'"доли: пик {0:F2} %', u'"доли: пик {0:P2}', 1)),
            (u'вернулась подстановка со строчной буквой {0:p1}',
             pclean.replace(u'"доли: пик {0:F2} %', u'"доли: пик {0:p1}', 1)),
            (u'вернулась запись ToString("P3")',
             pclean.replace(u'double total = Sum(plain);',
                            u'double total = Sum(plain);\r\n'
                            u'                string s = total.ToString("P3", CultureInfo.InvariantCulture);', 1)),
    ]:
        if spoiled == pclean:
            missed.append(u'порча «%s» не подставилась: текст не изменился' % what)
        elif not [b for b in judge(spoiled) if b[1] == u'T247']:
            missed.append(u'порча «%s» НЕ ПОЙМАНА' % what)

    # (г) Формат АРГУМЕНТОМ — та самая порча, которой сторож был слеп до
    # 10.09.2026. Подставляется ДОСЛОВНО то, что стояло в дереве.
    arg = take(u'CorpusFsaProbe.cs')
    if arg is None:
        return missed + [u'самопроверке не на чем работать: нет probes/CorpusFsaProbe.cs']

    aclean = read(arg)
    if [b for b in judge(aclean) if b[1] == u'T247']:
        missed.append(u'починенный CorpusFsaProbe.cs объявлен нарушением —'
                      u' сторож ложно тревожит')

    spoiled = aclean.replace(
        u'GridStep(100.0 * head.GainRange, head.GainSteps, "F3", " %")',
        u'GridStep(head.GainRange, head.GainSteps, "P3", "")', 1)
    if spoiled == aclean:
        missed.append(u'порча «формат аргументом» не подставилась:'
                      u' текст не изменился')
    elif not [b for b in judge(spoiled) if b[1] == u'T247']:
        missed.append(u'порча «формат аргументом» НЕ ПОЙМАНА')

    # ⛔ И обратное плечо: имя, случайно совпавшее с форматом, нарушением НЕ
    # объявляется. Без него образец аргумента можно было бы расширить до
    # ложно тревожащего, и никто бы этого не заметил.
    legal_name = read(legal)
    if [b for b in judge(legal_name) if b[1] == u'T247']:
        missed.append(u'`cfg.Name = "P3"` в CultureProbeO14.cs объявлено'
                      u' нарушением — сторож ложно тревожит на ИМЕНИ')

    # (д) Половина 3: снятая культура потока обязана быть поймана.
    cult = take(u'MineProbe.cs')
    if cult is None:
        return missed + [u'самопроверке не на чем работать: нет probes/MineProbe.cs']

    cclean = read(cult)
    if judge_culture(cclean):
        missed.append(u'починенный MineProbe.cs объявлен нарушением —'
                      u' сторож ложно тревожит на культуре')

    spoiled = cclean.replace(
        u'CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;', u'', 1)
    if spoiled == cclean:
        missed.append(u'порча «культура потока снята» не подставилась:'
                      u' текст не изменился')
    elif not judge_culture(spoiled):
        missed.append(u'порча «культура потока снята» НЕ ПОЙМАНА')

    # ⛔ Обратное плечо половины 3: проба, у которой поставщик стоит В КАЖДОМ
    # вызове, законна и без культуры потока. Без этой проверки правило легко
    # ужесточить до «культура потока обязательна всем», и три пробы, у которых
    # культура — ПРЕДМЕТ замера, стали бы вечно красными.
    subject = take(u'NumericCultureProbeF68.cs')
    if subject is not None and judge_culture(read(subject)):
        missed.append(u'NumericCultureProbeF68.cs (культура — ПРЕДМЕТ замера,'
                      u' поставщик стоит у каждого числа) объявлен нарушением'
                      u' — сторож ложно тревожит')

    return missed


def repo():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main(argv):
    try:
        sys.stdout.reconfigure(encoding=u'utf-8')
    except Exception:
        pass

    root = os.path.join(repo(), ROOT)
    if not os.path.isdir(root):
        print(u'ОСТАНОВ: каталога оснастки нет — %s' % root)
        return 3

    files = sources(root)
    if not files:
        print(u'ОСТАНОВ: ни одного .cs в оснастке не нашлось — так не бывает')
        return 3

    print(u'сторож печати чисел в оснастке (T245, T247): разделитель — только'
          u' инвариантом, формат P — не применяется, у каждого числа —'
          u' поставщик культуры; судятся %d файлов %s'
          % (len(files), ROOT.replace(u'\\', u'/')))

    bad = {}
    marked = 0
    for name, text in files:
        for line in text.split(u'\n'):
            if MARK.search(line):
                marked += 1
        found = judge(text)
        if found:
            bad[name] = found

    print(u'  законных подмен, помеченных «T245: НАРОЧНО»: %d' % marked)

    # ПРИГОВОР ПЕРВЫМ, САМОПРОВЕРКА ВТОРОЙ: самопроверка портит те же настоящие
    # файлы, что судятся, и при настоящей поломке дала бы код 2 «сторож слеп»
    # там, где на деле сломана оснастка.
    #
    # Исключение — `--selftest`: он спрашивает не «цело ли дерево», а «видит ли
    # сторож порчу», и обязан отвечать в том числе на КРАСНОМ дереве. Иначе
    # положительный контроль недоступен ровно тогда, когда он нужнее всего —
    # пока нарушения ещё не сняты. Опора самопроверки (файлы, которые она
    # портит) названа поимённо и от найденных нарушений не зависит.
    selfonly = u'--selftest' in argv
    if bad and not selfonly:
        total = sum(len(v) for v in bad.values())
        print(u'ОСТАНОВ: печать чисел нарушена в %d файлах, мест %d:'
              % (len(bad), total))
        for name in sorted(bad):
            for lineno, kind, why in bad[name]:
                print(u'   %s:%d [%s] %s' % (name, lineno, kind, why))
        print(u'  (самопроверка не гонялась: её опора — те же файлы, что сломаны)')
        return 1

    missed = selftest(root)
    if missed:
        print(u'ОСТАНОВ: САМОПРОВЕРКА НЕ ПРОШЛА — сторож слеп:')
        for m in missed:
            print(u'   ' + m)
        return 2
    print(u'  самопроверка: семь подставленных порч пойманы, целые файлы чисты'
          u' (в том числе имя «P3» и проба, у которой культура — ПРЕДМЕТ замера)')

    if selfonly:
        if bad:
            print(u'  (нарушения в дереве ЕСТЬ — %d мест в %d файлах, см. выше;'
                  u' --selftest судит сторожа, а не дерево)'
                  % (sum(len(v) for v in bad.values()), len(bad)))
            for name in sorted(bad):
                for lineno, kind, why in bad[name]:
                    print(u'   %s:%d [%s] %s' % (name, lineno, kind, why))
        return 0

    print(u'ПЕЧАТЬ ЧИСЕЛ ЦЕЛА: подмен разделителя без метки нет, форматов P'
          u' нет ни одного, числа печатаются только с поставщиком культуры.')
    return 0


if __name__ == u'__main__':
    sys.exit(main(sys.argv[1:]))
