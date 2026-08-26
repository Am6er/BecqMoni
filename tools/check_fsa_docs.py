# -*- coding: utf-8 -*-
u"""Машинная сверка XML-ОПИСАНИЙ настроек разбора с тем, что ставит конструктор.

Заведён по строке `T82`. Родня: `T70` (третья копия числа узлов, в описании),
`T65` (вторая копия, в пробе). Ловится один класс дефекта, и он уже стоил
четырёх находок подряд:

    ⚠ описание называет ЧИСЛО или ПОЛЯРНОСТЬ умолчания, конструктор ставит
      другое, и разойтись они могут молча — компилятор комментарии не читает.

Четвёртую копию нашла ПЯТАЯ пара глаз, после того как `T70` объявила сверку
описаний сплошной. Значит глазами этот файл не проверяется в принципе, и
проверка обязана быть счётной.

Приём тот же, что в `CorpusFsaProbe` (`T65`): единственный источник значения —
конструктор, всё остальное с ним СЛИЧАЕТСЯ. Разница в том, что проба сличает
НАСТРОЙКИ отражением на живом объекте, а здесь сличается ТЕКСТ, поэтому читать
приходится исходник: XML-комментарий до `new FsaAnalyzer()` не доживает.

## Что считается находкой

1. **ПРОТУХЛО** — описание называет значение, а конструктор ставит другое.
   Ровно это было в `T70` (описание «64», конструктор `128`) и в пункте (3)
   `T82` (описание «Выключено», конструктор `true`).
2. **ВТОРАЯ КОПИЯ** — описание называет значение, и сегодня оно верное.
   Тоже находка, и это не придирка: протухшая копия начинается верной. Правило
   репозитория — «второй копии числа быть не должно»; лечение одно и то же —
   убрать число из описания и сослаться на конструктор.
3. **НЕ ПРОВЕРЯЕТСЯ** — описание называет значение настройки, а умолчание
   разобрать не удалось (присваивание не литералом, свойство переименовано).
   Тоже отказ, и НАРОЧНО: сторож, который при непонятном входе молчит, —
   это сторож, который всегда молчит.

## Чего этот сторож НЕ ловит, и это надо понимать

Числа в описаниях лежат ПРОЗОЙ, и общего правила «это число — умолчание»
не существует. Проверяются три вида записи:

* СВОЁ-К-СВОЕМУ, без всяких оборотов: любое отдельно стоящее число в описании
  члена, РАВНОЕ умолчанию этого же члена. Оборота не требует, значит переживает
  любую переформулировку, и новая настройка попадает под него сама. Ноль
  пропускается: им описывают СМЫСЛ («0 — выключено»), а не умолчание;
* привязанные к своему члену оборотом: `умолчание <число>` / `по умолчанию
  <число>` — нужны там, где число НЕ равно умолчанию, то есть уже протухло; и
  полярность (`Выключено` / `Включено`) у булевых настроек;
* сквозные обороты, которыми умолчания называют В ЧУЖИХ описаниях (таблица
  `PROSE_RULES`): `1/64 диапазона`, `ноль ±8 кэВ, 17 узлов`, `сетка 9×9`.
  Именно так лежали обе копии из `T82` — в описании МЕТОДА и в описании
  КЛАССА, то есть привязка к члену их бы не нашла.

Написанное иначе — «делитель вдвое больше прежнего», «шаг около сорока кэВ» —
сторож не поймает. Поэтому таблица `PROSE_RULES` расширяется вместе с
находками, а `--self-test` держит на КАЖДОЕ правило положительный контроль:
образец текста, на котором правило ОБЯЗАНО отказать.

⛔ Почему счётное правило только СВОЁ-К-СВОЕМУ, а не по всему файлу. Тот же
поиск без привязки к члену измерен 27.08.2026 на этом же файле: 11 совпадений
с умолчаниями, из них ПЯТЬ случайные — «у них кривая с 40 кэВ» против
`MinEnergy` = 40, «выше 10 % у десяти спектров» против пола 10, «фантомы
5/37 → 4/32» и «у 4 спектров из 61» против `ContinuumKnotFwhm` = 4. По виду
случайное от настоящего не отличается: и то и другое — число в русской фразе.
Сторож, у которого почти половина отказов ложная, перестают читать, и это тот
же отказ, что молчание.

⚠ Кросс-файловые равенства не проверяются вовсе. Пример живой: описание
`FsaBand.DefaultFloorKev` утверждает, что пол равен `FsaSampleSpec.MinEnergyKev`
(`FsaSampleLibrary.cs`), и 27.08.2026 это правда — но правда непроверенная.

## Самопроверка при каждом прогоне

Разбор, который ничего не нашёл, неотличим от чистого файла. Поэтому перед
правилами проверяются якоря (`REQUIRED_DEFAULTS`): перечисленные настройки
обязаны быть найдены и разобраны. Не нашлись — отказ с кодом 3, а не «чисто».

## Коды возврата

* 0 — сошлось;
* 2 — находки;
* 3 — разбор не удался (файл, якоря, самопроверка).

## Кто зовёт

⚠ **На 27.08.2026 — НИКТО, и это открытый остаток `T82`, а не мелочь.** Место
для читателя одно и оно известно: раздел «Проверка машинная, а не на глаз» в
шапке `TODO.md`, где уже стоит `tools/check_registry.py`; строку туда
дописывает Amber. Пока её там нет, этот файл — признак без читателя, ровно то,
на чём репозиторий уже обжигался. Не переписывать эту оговорку, не проверив,
что строка появилась.

Запуск:

    python tools/check_fsa_docs.py
    python tools/check_fsa_docs.py --self-test
"""

from __future__ import print_function

import io
import os
import re
import sys


REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

DEFAULT_TARGET = os.path.join(
    REPO_ROOT, u'BecquerelMonitor', u'FullSpectrumAnalysis', u'FsaAnalyzer.cs')

# Настройки, без которых разбор считается несостоявшимся. Список — НЕ копия
# значений: здесь только имена, значения берутся у конструктора.
REQUIRED_DEFAULTS = [
    u'ContinuumKnotDivisor',
    u'ContinuumKnotFwhm',
    u'GainRange',
    u'GainSteps',
    u'OffsetRangeKev',
    u'OffsetSteps',
    u'CascadeIsomerPartners',
    u'CascadeXrayPartners',
    u'EscapeGate',
]

# Наименьшее правдоподобное число описаний и умолчаний. Числа — не мерка
# файла, а порог «разбор явно сломан»: в файле на четыре с половиной тысячи
# строк описаний заведомо десятки, и конструктор ставит заведомо больше
# десятка настроек.
MIN_DOC_BLOCKS = 50
MIN_CTOR_DEFAULTS = 15


# ---------------------------------------------------------------- чтение --

def count_doc_runs(lines):
    u"""Сколько в файле СПЛОШНЫХ блоков `///`. Считается отдельно и нарочно
    примитивно: это встречная мерка для разбора. Разошлись — разбор потерял
    описания, и молчать после этого нельзя."""
    runs = 0
    previous = False
    for raw in lines:
        current = bool(_DOC.match(raw))
        if current and not previous:
            runs += 1
        previous = current
    return runs


def read_lines(path):
    with io.open(path, u'r', encoding=u'utf-8-sig', newline=u'') as handle:
        text = handle.read()
    return text.replace(u'\r\n', u'\n').replace(u'\r', u'\n').split(u'\n')


# --------------------------------------------------------- значения C# ----

_NUMBER = re.compile(r'^-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?[dDfFmM]?$')


class Value(object):
    u"""Разобранное значение: число, булево или неразобранный текст."""

    def __init__(self, raw, number=None, boolean=None):
        self.raw = raw
        self.number = number
        self.boolean = boolean

    @property
    def known(self):
        return self.number is not None or self.boolean is not None

    def __repr__(self):
        if self.boolean is not None:
            return u'true' if self.boolean else u'false'
        if self.number is not None:
            return format_number(self.number)
        return self.raw


def format_number(value):
    if abs(value - round(value)) < 1e-12:
        return u'%d' % int(round(value))
    return (u'%g' % value)


def parse_literal(expr):
    expr = expr.strip()
    if expr == u'true':
        return Value(expr, boolean=True)
    if expr == u'false':
        return Value(expr, boolean=False)
    if _NUMBER.match(expr):
        return Value(expr, number=float(re.sub(r'[dDfFmM]$', u'', expr)))
    return Value(expr)


# --------------------------------------------------------- разбор файла --

_CTOR = re.compile(r'^\s*public\s+FsaAnalyzer\s*\(\s*\)\s*$')
_ASSIGN = re.compile(r'^\s*this\.(\w+)\s*=\s*([^;]+);\s*$')
_FIELD_INIT = re.compile(
    r'^\s{4,}(?:public|internal|protected|private)\s+'
    r'(?:static\s+|readonly\s+|const\s+|volatile\s+)*'
    r'[\w<>\[\],.?]+(?:\s*<[^<>]*>)?\s+(\w+)\s*=\s*([^;]+);\s*$')

_DOC = re.compile(r'^\s*///\s?(.*)$')
_ATTRIBUTE = re.compile(r'^\s*\[')

_DECL_PROPERTY = re.compile(r'\b(\w+)\s*(?:\{|$)')
_DECL_METHOD = re.compile(r'\b(\w+)\s*\(')
_DECL_TYPE = re.compile(r'\b(?:class|struct|enum|interface)\s+(\w+)')
_DECL_FIELD = re.compile(r'\b(\w+)\s*(?:=|;)')


class DocBlock(object):
    def __init__(self, member, kind, text, line):
        self.member = member
        self.kind = kind
        self.text = text
        self.line = line


def parse_source(lines):
    u"""Вернуть (умолчания, описания). Умолчания — из конструктора и из
    инициализаторов полей; описания — блоки `///` с именем того, к чему
    относятся."""

    field_inits = {}
    for raw in lines:
        match = _FIELD_INIT.match(raw)
        if match and u'(' not in match.group(2):
            field_inits.setdefault(match.group(1), match.group(2).strip())

    ctor_assignments = {}
    depth = 0
    inside = False
    started = False
    for raw in lines:
        if not inside:
            if _CTOR.match(raw):
                inside = True
                started = False
            continue
        depth += raw.count(u'{') - raw.count(u'}')
        if raw.count(u'{'):
            started = True
        match = _ASSIGN.match(raw)
        if match:
            ctor_assignments[match.group(1)] = match.group(2).strip()
        if started and depth <= 0:
            break

    def resolve(expr, seen):
        value = parse_literal(expr)
        if value.known:
            return value
        name = expr.split(u'.')[-1].strip()
        if name and name not in seen and name in field_inits:
            seen.add(name)
            return resolve(field_inits[name], seen)
        return value

    defaults = {}
    for name, expr in field_inits.items():
        defaults[name] = (resolve(expr, set([name])), u'инициализатор поля')
    for name, expr in ctor_assignments.items():
        defaults[name] = (resolve(expr, set()), u'конструктор')

    blocks = []
    buffer_ = []
    start = 0
    for index, raw in enumerate(lines):
        match = _DOC.match(raw)
        if match:
            if not buffer_:
                start = index + 1
            buffer_.append(match.group(1))
            continue
        if not buffer_:
            continue
        if not raw.strip() or _ATTRIBUTE.match(raw):
            continue
        member, kind = declaration_name(raw)
        blocks.append(DocBlock(member, kind, u'\n'.join(buffer_), start))
        buffer_ = []
    return defaults, blocks


def declaration_name(raw):
    stripped = raw.strip()
    match = _DECL_TYPE.search(stripped)
    if match:
        return match.group(1), u'тип'
    if u'(' in stripped:
        match = _DECL_METHOD.search(stripped)
        if match:
            return match.group(1), u'метод'
    if u'{' in stripped or stripped.endswith(u'{'):
        match = _DECL_PROPERTY.findall(stripped.split(u'{')[0])
        if match:
            return match[-1], u'свойство'
    match = _DECL_FIELD.search(stripped)
    if match:
        return match.group(1), u'поле'
    return None, u'?'


# ------------------------------------------------------------- правила ----

def strip_markup(text):
    u"""Убрать из описания то, что заведомо не является значением: ссылки
    `<see cref="..."/>`, код в обратных кавычках и в `<c>`, номера строк
    реестра, даты."""
    text = re.sub(r'<see\s+cref="[^"]*"\s*/?>', u' ', text)
    text = re.sub(r'<[^<>]*>', u' ', text)
    text = re.sub(r'`[^`]*`', u' ', text)
    text = re.sub(r'\b\d{2}\.\d{2}\.\d{4}\b', u' ', text)
    return text


# Обороты, которыми умолчание называют В ЧУЖОМ описании — там, где привязки
# к члену нет. Каждый оборот обязан иметь положительный контроль в SELF_TESTS.
#
#   ключ      — имя настройки, у которой берётся истинное значение;
#   pattern   — что искать в очищенном тексте описания;
#   extract   — из группы в число, СРАВНИМОЕ с умолчанием
#               (проценты приводятся к доле, как в конструкторе).
PROSE_RULES = [
    {
        u'name': u'делитель шага узлов континуума',
        u'setting': u'ContinuumKnotDivisor',
        u'pattern': re.compile(r'1\s*/\s*(\d+)\s*(?:часть|части|доли)?\s*диапазона'),
        u'extract': lambda m: float(m.group(1)),
    },
    {
        u'name': u'полуширина сетки дрейфа по нулю',
        u'setting': u'OffsetRangeKev',
        u'pattern': re.compile(r'ноль\w*\s*±\s*(\d+(?:[.,]\d+)?)\s*кэВ'),
        u'extract': lambda m: float(m.group(1).replace(u',', u'.')),
    },
    {
        # Найдено 27.08.2026 при правке описаний по `T82`: шапка КЛАССА
        # называла густой край шага узлов числом («не меньше 4 ПШПВ»), и это
        # была ПЯТАЯ копия — привязка к члену её не видит (описание чужое), а
        # оборота такого в таблице не было.
        u'name': u'густой край шага узлов, в ПШПВ',
        u'setting': u'ContinuumKnotFwhm',
        u'pattern': re.compile(
            r'шаг\w*\s+узл\w*\s+не\s+(?:меньше|реже\s+чем)\s+'
            r'(\d+(?:[.,]\d+)?)\s*ПШПВ'),
        u'extract': lambda m: float(m.group(1).replace(u',', u'.')),
    },
    {
        u'name': u'полуширина сетки дрейфа по усилению',
        u'setting': u'GainRange',
        u'pattern': re.compile(r'усилени\w*\s*±\s*(\d+(?:[.,]\d+)?)\s*%'),
        u'extract': lambda m: float(m.group(1).replace(u',', u'.')) / 100.0,
    },
    {
        u'name': u'число узлов сетки дрейфа по нулю',
        u'setting': u'OffsetSteps',
        u'pattern': re.compile(
            r'ноль\w*\s*±\s*\d+(?:[.,]\d+)?\s*кэВ[^.;()]{0,40}?(\d+)\s*узл'),
        u'extract': lambda m: float(m.group(1)),
    },
    {
        u'name': u'сетка дрейфа NxN',
        u'setting': u'OffsetSteps',
        u'pattern': re.compile(r'сетк\w*\s+дрейфа\s+(\d+)\s*[x×]\s*\d+'),
        u'extract': lambda m: float(m.group(1)),
    },
]

# Привязанное к своему члену: «умолчание <число>». Окно узкое НАРОЧНО —
# на широком «умолчание ISO 11929» читается как значение 11929.
_OWN_NUMBER = re.compile(
    r'(?:[Уу]молчани\w*|по умолчанию|[Пп]оставочно\w*)[ \n]*(?:—|-|:)?[ \n]*'
    r'(\d+(?:[.,]\d+)?)\s*(?![\d]*\s*(?:ISO|ГОСТ|IEC|ANSI))')

# Любое отдельно стоящее число. Нужно правилу «своё умолчание в своём же
# описании» — оно работает БЕЗ оборотов и потому переживает любую
# переформулировку: новая настройка попадает под него сама, дописывать в
# таблицу ничего не надо.
#
# ⛔ Правило нарочно СВОЁ-К-СВОЕМУ. Тот же поиск ПО ВСЕМУ ФАЙЛУ проверен
# 27.08.2026 и оказался негодным: из 11 совпадений с умолчаниями пять —
# случайные («кривая с 40 кэВ» против `MinEnergy` = 40, «выше 10 % у десяти
# спектров» против пола 10, «фантомы 4/32» против `ContinuumKnotFwhm` = 4),
# и по виду они не отличаются от настоящих. Отсюда разделение: своё-к-своему
# ловится счётом, чужое — только оборотом из `PROSE_RULES`.
#
# ⚠ Хвост правила — `(?![\w]|[.,]\d)`, а не `(?![\w.,])`: с последним число в
# конце фразы («взято 100.») и число перед запятой («80, 100, 120») в счёт не
# шли ВОВСЕ, то есть сторож видел одну копию из трёх. Проверено на
# `ResponseContinuumTrustFloorKev`: было 1 совпадение, стало 3.
_ANY_NUMBER = re.compile(r'(?<![\w.,\-−])(\d+(?:[.,]\d+)?)(?![\w]|[.,]\d)')

# Полярность булевой настройки. `Выключатель`, `Включатель` под правило не
# попадают: у них после корня стоит буква, а не граница слова.
_OWN_POLARITY = re.compile(
    r'(?:^|[.;:!?»)]\s|\s)(Выключено|выключено|ВЫКЛЮЧЕНО|Включено|включено|ВКЛЮЧЕНО)'
    r'(?![\w])')

_OFF_WORDS = (u'Выключено', u'выключено', u'ВЫКЛЮЧЕНО')


class Finding(object):
    def __init__(self, kind, line, member, what, said, actual, source):
        self.kind = kind
        self.line = line
        self.member = member
        self.what = what
        self.said = said
        self.actual = actual
        self.source = source

    def render(self):
        return (u'  %-14s %s:%d  %s\n'
                u'                 описание говорит: %s\n'
                u'                 %s ставит:  %s'
                % (self.kind, self.member or u'?', self.line, self.what,
                   self.said, self.source, self.actual))


def check(defaults, blocks):
    findings = []

    for block in blocks:
        text = strip_markup(block.text)

        # 1. Сквозные обороты — ищутся в ЛЮБОМ описании файла.
        for rule in PROSE_RULES:
            for match in rule[u'pattern'].finditer(text):
                said = rule[u'extract'](match)
                findings.append(prose_finding(rule, said, block, defaults))

        # 2. Привязанное к своему члену.
        if not block.member or block.member not in defaults:
            continue
        value, source = defaults[block.member]

        # 2а. СВОЁ умолчание, названное в СВОЁМ же описании, — без всяких
        # оборотов. Ноль пропускается: им описывают СМЫСЛ («0 — выключено»),
        # он же достаётся невыставленному полю, и запрет на него запретил бы
        # описывать контракт настройки.
        if value.number is not None and value.number != 0.0:
            for match in _ANY_NUMBER.finditer(text):
                said = float(match.group(1).replace(u',', u'.'))
                if abs(value.number - said) > 1e-9:
                    continue
                findings.append(Finding(
                    u'ВТОРАЯ КОПИЯ', block.line, block.member,
                    u'своё умолчание названо числом в своём же описании',
                    format_number(said), repr(value), source))

        for match in _OWN_NUMBER.finditer(text):
            said = float(match.group(1).replace(u',', u'.'))
            # Совпавшее уже поймано правилом 2а — второй строкой не печатаем.
            if value.number is not None and abs(value.number - said) <= 1e-9:
                continue
            if not value.known:
                findings.append(Finding(
                    u'НЕ ПРОВЕРЯЕТСЯ', block.line, block.member,
                    u'описание называет умолчание числом',
                    format_number(said), u'разобрать не удалось: ' + value.raw,
                    source))
            else:
                # Сюда доходит только РАСХОЖДЕНИЕ: совпавшее отсеяно выше.
                findings.append(Finding(
                    u'ПРОТУХЛО', block.line, block.member,
                    u'умолчание названо числом в своём же описании',
                    format_number(said), repr(value), source))

        if value.boolean is not None:
            for match in _OWN_POLARITY.finditer(text):
                said = match.group(1) not in _OFF_WORDS
                if said != value.boolean:
                    findings.append(Finding(
                        u'ПРОТУХЛО', block.line, block.member,
                        u'полярность умолчания в своём же описании',
                        match.group(1), repr(value), source))

    return findings


def prose_finding(rule, said, block, defaults):
    setting = rule[u'setting']
    if setting not in defaults:
        return Finding(u'НЕ ПРОВЕРЯЕТСЯ', block.line, block.member,
                       rule[u'name'], format_number(said),
                       u'настройка %s не найдена' % setting, u'—')
    value, source = defaults[setting]
    if not value.known:
        return Finding(u'НЕ ПРОВЕРЯЕТСЯ', block.line, block.member,
                       rule[u'name'], format_number(said),
                       u'разобрать не удалось: ' + value.raw, source)
    if value.number is None or abs(value.number - said) > 1e-9:
        return Finding(u'ПРОТУХЛО', block.line, block.member,
                       u'%s (%s)' % (rule[u'name'], setting),
                       format_number(said), repr(value), source)
    return Finding(u'ВТОРАЯ КОПИЯ', block.line, block.member,
                   u'%s (%s)' % (rule[u'name'], setting),
                   format_number(said), repr(value), source)


# --------------------------------------------------- положительный контроль

# На КАЖДОЕ правило — образец, на котором оно ОБЯЗАНО отказать. Порченый
# образец вставляется в описание указанного члена и разбирается тем же кодом.
SELF_TESTS = [
    (u'ContinuumKnotDivisor', u'BuildHatBasis',
     u'шаг узлов, но не реже чем 1/64 диапазона', u'ПРОТУХЛО'),
    (u'ContinuumKnotDivisor', u'BuildHatBasis',
     u'шаг узлов, но не реже чем 1/128 диапазона', u'ВТОРАЯ КОПИЯ'),
    (u'OffsetRangeKev', u'FsaBand',
     u'сетка дрейфа (ноль ±3 кэВ)', u'ПРОТУХЛО'),
    (u'OffsetRangeKev', u'FsaBand',
     u'сетка дрейфа (ноль ±8 кэВ)', u'ВТОРАЯ КОПИЯ'),
    (u'GainRange', u'FsaBand',
     u'сетка дрейфа (усиление ±0,8 %)', u'ПРОТУХЛО'),
    (u'OffsetSteps', u'FsaBand',
     u'сетка дрейфа (ноль ±8 кэВ, 9 узлов)', u'ПРОТУХЛО'),
    (u'OffsetSteps', u'FsaBand',
     u'считалась сетка дрейфа 9x9', u'ПРОТУХЛО'),
    (u'ContinuumKnotFwhm', u'ContinuumKnotFwhm',
     u'Умолчание 7 — то, при котором считали.', u'ПРОТУХЛО'),
    (u'ContinuumKnotFwhm', u'FsaAnalyzer',
     u'базис с шагом узлов не меньше 7 ПШПВ', u'ПРОТУХЛО'),
    (u'ContinuumKnotFwhm', u'FsaAnalyzer',
     u'базис с шагом узлов не меньше 4 ПШПВ', u'ВТОРАЯ КОПИЯ'),
    (u'CascadeIsomerPartners', u'CascadeIsomerPartners',
     u'Выключено — прежнее поведение.', u'ПРОТУХЛО'),
    # Правило 2а — БЕЗ оборота: своё умолчание в своём же описании.
    (u'LimitQuantileK', u'LimitQuantileK',
     u'Берётся 1.6449, как велит ISO 11929.', u'ВТОРАЯ КОПИЯ'),
    # Он же — положительный контроль хвоста `_ANY_NUMBER`: число в КОНЦЕ
    # фразы прежде не считалось вовсе.
    (u'ResponseContinuumTrustFloorKev', u'ResponseContinuumTrustFloorKev',
     u'Из четырёх точек скана взято 100.', u'ВТОРАЯ КОПИЯ'),
]

_INSERT_AT = re.compile(r'^(\s*)///\s*<summary>\s*$')


def inject(lines, member, sentence):
    u"""Вставить порченую фразу первой строкой описания указанного члена."""
    defaults, blocks = parse_source(lines)
    for block in blocks:
        if block.member != member:
            continue
        head = block.line - 1            # индекс строки `<summary>` либо первой
        match = _INSERT_AT.match(lines[head])
        indent = match.group(1) if match else u'        '
        at = head + 1 if match else head
        return lines[:at] + [u'%s/// %s' % (indent, sentence)] + lines[at:]
    raise AssertionError(u'член %s в файле не найден' % member)


def integrity(lines, defaults, blocks):
    u"""Проверка САМОГО РАЗБОРА. Сторож, который при поломке разбора печатает
    «чисто», хуже отсутствующего: он выдаёт молчание за проверку."""
    problems = []

    runs = count_doc_runs(lines)
    if len(blocks) != runs:
        problems.append(u'блоков `///` в файле %d, разбор привязал %d — '
                        u'описания потеряны' % (runs, len(blocks)))
    if runs < MIN_DOC_BLOCKS:
        problems.append(u'блоков `///` всего %d, ожидалось не меньше %d'
                        % (runs, MIN_DOC_BLOCKS))

    ctor_count = len([n for n, (v, s) in defaults.items()
                      if s == u'конструктор'])
    if ctor_count < MIN_CTOR_DEFAULTS:
        problems.append(u'умолчаний из конструктора разобрано %d, '
                        u'ожидалось не меньше %d'
                        % (ctor_count, MIN_CTOR_DEFAULTS))

    for name in REQUIRED_DEFAULTS:
        if name not in defaults:
            problems.append(u'якорь %s не найден — разбор сломан либо член '
                            u'переименован' % name)
        elif not defaults[name][0].known:
            problems.append(u'якорь %s не разобран: %s'
                            % (name, defaults[name][0].raw))
    return problems, runs, ctor_count


def self_test(path):
    lines = read_lines(path)
    defaults, blocks = parse_source(lines)
    problems, runs, ctor_count = integrity(lines, defaults, blocks)

    print(u'  разбор: блоков `///` %d, привязано %d, умолчаний %d '
          u'(конструктор ставит %d)'
          % (runs, len(blocks), len(defaults), ctor_count))
    print(u'  якоря и умолчания, как их видит разбор:')
    for name in REQUIRED_DEFAULTS:
        got = defaults.get(name)
        print(u'    %-26s %-8s (%s)'
              % (name, repr(got[0]) if got else u'—',
                 got[1] if got else u'НЕ НАЙДЕНО'))

    print(u'  порченые образцы (каждый ОБЯЗАН дать находку):')
    for setting, member, sentence, expect in SELF_TESTS:
        spoiled = inject(lines, member, sentence)
        s_defaults, s_blocks = parse_source(spoiled)
        # Находка должна быть ИМЕННО того рода и ИМЕННО у того члена, куда
        # вставлена порча: «где-то что-то нашлось» контролем не является.
        hits = [f for f in check(s_defaults, s_blocks)
                if f.kind == expect and f.member == member]
        status = u'поймано' if hits else u'ПРОПУЩЕНО'
        if not hits:
            problems.append(u'образец «%s» у %s не пойман (ждали %s)'
                            % (sentence, member, expect))
        print(u'    %-9s %-24s %-13s %s' % (status, member, expect, sentence))

    # отрицательный контроль: чистое дерево не должно давать находок от
    # самого факта прогона self-test
    print(u'  отрицательный контроль (без порчи): находок %d'
          % len(check(defaults, blocks)))

    if problems:
        print(u'\nСАМОПРОВЕРКА НЕ ПРОШЛА:')
        for line in problems:
            print(u'  * ' + line)
        return 3
    print(u'\nСАМОПРОВЕРКА ПРОШЛА: все %d образцов пойманы, якоря разобраны.'
          % len(SELF_TESTS))
    return 0


# ---------------------------------------------------------------- запуск --

def main(argv):
    args = [a for a in argv[1:] if not a.startswith(u'--')]
    flags = set(a for a in argv[1:] if a.startswith(u'--'))
    path = args[0] if args else DEFAULT_TARGET

    if not os.path.isfile(path):
        print(u'НЕТ ФАЙЛА: %s' % path)
        return 3

    if u'--self-test' in flags:
        print(u'САМОПРОВЕРКА check_fsa_docs по %s' % path)
        return self_test(path)

    lines = read_lines(path)
    defaults, blocks = parse_source(lines)

    broken, runs, ctor_count = integrity(lines, defaults, blocks)
    if broken:
        print(u'РАЗБОР НЕ СОСТОЯЛСЯ — молчать нельзя:')
        for line in broken:
            print(u'  * ' + line)
        return 3

    findings = check(defaults, blocks)
    print(u'%s: описаний %d, умолчаний %d (из них конструктор ставит %d)'
          % (os.path.basename(path), len(blocks), len(defaults), ctor_count))

    if not findings:
        print(u'ОПИСАНИЯ СОШЛИСЬ С КОНСТРУКТОРОМ: копий значений в описаниях нет.')
        return 0

    print(u'\nНАХОДКИ (%d):' % len(findings))
    for finding in findings:
        print(finding.render())
    print(u'\nЛечение одно: убрать число из описания и сослаться на конструктор,')
    print(u'где стоит довод. Второй копии значения быть не должно.')
    return 2


if __name__ == u'__main__':
    sys.exit(main(sys.argv))
