# -*- coding: utf-8 -*-
u"""Сторож СОСТАВА КЛЮЧЕЙ ОБОИХ ПУТЕЙ РАСЧЁТА ПО ГЕОМЕТРИИ (`T242`).

## Что судится и почему

Путей два, и они СЧИТАЮТ РАЗНОЕ ОДНИМ И ТЕМ ЖЕ переносом:

  * путь МАТРИЦЫ ОТКЛИКА  -- `ResponseMatrixOptions` -> `ResponseMatrixBuilder`
    -> `EfficiencySimulator`. Им ходят и скрипт корпуса (`CorpusMatrixProbe`),
    и кнопка «Пересчитать» формы «Матрица отклика»: построитель ОДИН, разнится
    только заполнение настроек.
  * путь КРИВОЙ ЭФФЕКТИВНОСТИ -- `EfficiencyCalculationOptions` ->
    `EfficiencyCalculation.Run` -> тот же `EfficiencySimulator`. Им ходит
    кнопка «Посчитать из геометрии».

Расхождение состава ключей между ними уже оплачено трижды, и все три раза
молча -- ни отказа, ни предупреждения, ни следа в побитовом замере:

  1. `E34`: `MakeSimulator` ставил `PeakHalfWidthKev = 0`, а путь кривой брал
     ту же величину из геометрии. Поправка на однократное рассеяние считалась
     впустую, забирая 1.45...1.96 счёта узла.
  2. `S130` («ключ, не доехавший до построителя, МЁРТВ»): чтобы настройка
     включилась, её надо завести в НЕСКОЛЬКИХ местах -- объявление, потребитель,
     клеймо, файл, рычаг. Побитовый замер дыру не ловит: числа верные,
     испорчено ПРОИСХОЖДЕНИЕ.
  3. `T114` / `A66` / `E34` / `T242`: ключ входит в КЛЕЙМО, а в файл матрицы не
     пишется. После чтения с диска он возвращается умолчанием, клеймо
     пересчитывается без него, и матрица НЕ СХОДИТСЯ САМА С СОБОЙ -- разбор
     печатает «БЕЗ МАТРИЦЫ» навсегда. Четыре случая одной ошибки: зерно,
     пятёрка физики, допуск пика, рулетка с флуоресценцией.

Сводная таблица обоих путей живёт в
`handover/handover-2026-09-06-p12-matrix-keys.md`. Этот сторож -- её приёмка:
таблица, которую не сверяют машинно, протухает за неделю (06.09.2026 доказано
дважды: перечень внутри `T127` протух на 14 номеров из 24).

## Восемь правил

  A. РЕЕСТР РАВЕН ИСХОДНИКУ. Поля `ResponseMatrixOptions`,
     `EfficiencyCalculationOptions` и настроечные поля `EfficiencySimulator`
     перечислены здесь ПОИМЁННО. Появилось поле, которого в реестре нет, --
     отказ с именем. Это и есть положительный контроль правила «ключ, заведённый
     только в одном месте, обязан быть найден».
  B. У КАЖДОЙ НАСТРОЙКИ ЕСТЬ ПОТРЕБИТЕЛЬ на пути сборки матрицы: её читает
     `MakeSimulator`, тело `Build` или `BuildGrid`. Иначе ключ мёртв.
  C. ЧТО В КЛЕЙМЕ -- ТО В ФАЙЛЕ. Поле, попадающее в `ComputeStamp`, обязано
     писаться и читаться (`WriteOptions`/`ReadOptions` либо хвост `Save`/`Load`).
  D. РЕЕСТР НЕ ВРЁТ ПРО ИСХОДНИК: объявленные здесь клеймо, рычаг пробы и поле
     формы обязаны находиться в исходниках дословно.
  E. УМОЛЧАНИЕ ПОЛЯ ФОРМЫ РАВНО УМОЛЧАНИЮ КЛАССА (`A39`: «два места, и оба
     обязаны совпадать») -- у формы матрицы и у полей вкладки расчёта кривой.
  F. НАСТРОЙКИ КРИВОЙ ПЕРЕЧИСЛЕНЫ и все доходят до `EfficiencyCalculation.Run`.
  G. КТО ЧТО СТАВИТ СИМУЛЯТОРУ: реестр называет для каждого настроечного поля
     `EfficiencySimulator`, ставит ли его путь матрицы и ставит ли путь кривой.
     Разошлось с исходником -- отказ.
  H. У КАЖДОГО ОДНОСТОРОННЕГО КЛЮЧА НАЗВАНА ПРИЧИНА одним из трёх слов:
     «намеренно», «забыто», «неприменимо». Слово «забыто» -- отказ: забытое
     чинится, а не описывается.

  python tools/check_matrix_keys.py [--selftest] [--table]

  --selftest  только самопроверка: подставить порчу и убедиться, что видно;
  --table     напечатать сводку реестра (чем считает каждый путь).

Коды возврата:
  0 -- все восемь правил соблюдены;
  1 -- правило нарушено (нарушители названы поимённо);
  2 -- самопроверка не прошла: сторож слеп к подставленной порче;
  3 -- судить нечего (исходники не найдены).

Печать без знаков вне cp1251: консоль здесь cp1251, и «стоп»/«внимание»/стрелки
в ней превращаются в «?». Кода возврата это не меняет, а читателя лишает
(`T219`).
"""

import io
import os
import re
import sys

RM = os.path.join(u'BecquerelMonitor', u'EfficiencyMaker', u'ResponseMatrix.cs')
RMB = os.path.join(u'BecquerelMonitor', u'EfficiencyMaker', u'ResponseMatrixBuilder.cs')
EC = os.path.join(u'BecquerelMonitor', u'EfficiencyMaker', u'EfficiencyCalculation.cs')
ES = os.path.join(u'BecquerelMonitor', u'EfficiencyMaker', u'EfficiencySimulator.cs')
RMF = os.path.join(u'BecquerelMonitor', u'ResponseMatrixForm.cs')
RMFL = os.path.join(u'BecquerelMonitor', u'ResponseMatrixForm.Layout.cs')
EMF = os.path.join(u'BecquerelMonitor', u'EfficiencyMakerForm.cs')
PROBE = os.path.join(u'tools', u'effmaker', u'probes', u'CorpusMatrixProbe.cs')

SOURCES = [RM, RMB, EC, ES, RMF, RMFL, EMF, PROBE]

# ---------------------------------------------------------------------------
# РЕЕСТР 1. Настройки пути МАТРИЦЫ ОТКЛИКА (`ResponseMatrixOptions`).
#
# Поля кортежа:
#   имя            -- поле класса;
#   клеймо         -- входит ли в `ComputeStamp` (True/False);
#   рычаг пробы    -- строка ключа `CorpusMatrixProbe` или None;
#   поле формы     -- имя поля формы «Матрица отклика» или None;
#   причина        -- одно из трёх слов, если ключ живёт не на обоих путях,
#                     иначе 'общая';
#   довод          -- чем причина подтверждается.
#
# «Путь UI» здесь -- ДВА разных потребителя, и их надо различать:
#   * форма «Матрица отклика» зовёт ТОТ ЖЕ `ResponseMatrixBuilder`, поэтому
#     умолчания у неё те же; разнится только то, что она даёт трогать;
#   * форма «Посчитать из геометрии» (кривая) до этого класса не доходит вовсе,
#     и её настройки перечислены реестром 2.
# ---------------------------------------------------------------------------
MATRIX = [
    (u'MinEnergyKev', True, u'--emin=', u'minEnergyBox', u'общая', u''),
    (u'MaxEnergyKev', True, u'--emax=', u'maxEnergyBox', u'общая', u''),
    (u'NodeCount', True, u'--nodes=', u'nodesBox', u'общая', u''),
    (u'BinKev', True, u'--bin=', u'binBox', u'общая',
     u'ключ пробы заведён 06.09.2026 сводкой T242: поле формы было, рычага не было'),
    (u'Histories', True, u'--n=', u'historiesBox', u'общая', u''),
    (u'Threads', False, u'--threads=', u'threadsBox', u'общая',
     u'усилие, а не содержание: в клеймо не идёт намеренно'),
    (u'XrayEscape', True, u'--xray=', None, u'намеренно',
     u'абляция физики; форма её не даёт трогать, иначе уровень кривой стал бы подгоночным'),
    (u'LXrayEscape', True, u'--no-lxray', None, u'намеренно', u'абляция физики 15 (A60)'),
    (u'KLCascade', True, u'--klcasc=', None, u'намеренно', u'абляция физики 16 (A101)'),
    (u'CoherentPassesThrough', True, u'--coh=', None, u'намеренно',
     u'абляция; рычаг заведён 06.09.2026 сводкой T242'),
    (u'Bremsstrahlung', True, u'--brem=', None, u'намеренно',
     u'абляция; рычаг заведён 06.09.2026 сводкой T242'),
    (u'SingleScatter', True, u'--scat=', None, u'намеренно', u'абляция (доля вклада ~15 %)'),
    (u'LightNonproportionality', True, u'--npl=', None, u'намеренно', u'абляция F11'),
    (u'AnalogContinuum', True, u'--acont=', None, u'намеренно', u'абляция физики 6 (F14)'),
    (u'BoundScattering', True, u'--bound=', None, u'намеренно', u'абляция физики 7 (N11)'),
    (u'BremFromData', True, u'--bremsb=', None, u'намеренно',
     u'абляция физики 8; рычаг заведён 06.09.2026 сводкой T242'),
    (u'ScatterRoulette', True, u'--roulette=', None, u'намеренно',
     u'размен времени на шум, мерка -- время до цели по шуму'),
    (u'SampleFluorescence', True, u'--fluo=', None, u'намеренно', u'абляция F27'),
    (u'XcomPairThreshold', True, u'--pairth=', None, u'намеренно', u'рычаг замера S125'),
    (u'PositronTransport', True, u'--positron=', None, u'намеренно', u'первая половина S126'),
    (u'PositronOffset', True, u'--posoffset=', None, u'намеренно', u'вторая половина S126'),
    (u'RayleighToCrystal', True, u'--rayl2=', None, u'намеренно', u'рычаг замера S127'),
    (u'AnalogConeSampling', True, u'--cone=', None, u'намеренно',
     u'оценщик дисперсии, не физика; A57'),
    (u'PeakToleranceFromGeometry', True, u'--peakw=', None, u'намеренно',
     u'E34: включение двигает пик ВСЕХ матриц склада, решение о базе за Amber'),
    (u'Seed', True, u'--seed=', None, u'намеренно',
     u'T43: независимая выборка тем же кодом, мерка «в пределах шума ГСЧ»'),
    (u'ResolveEdges', False, u'--edges=', None, u'намеренно',
     u'E31/T42: в клеймо идёт САМА СЕТКА, а не ключ; форме поле не нужно'),
    (u'ContinuumErrorTarget', False, u'--target=', None, u'намеренно',
     u'усилие, а не содержание (T36): умолчание 3 % одно на обоих путях'),
    (u'PilotDivisor', False, None, None, u'намеренно',
     u'внутренняя доля пробного прохода; рычага нет НИ НА ОДНОМ пути, расхождения нет'),
    (u'MaxHistoriesFactor', False, None, None, u'намеренно',
     u'потолок историй на узел; рычага нет НИ НА ОДНОМ пути, расхождения нет'),
    (u'JointNodes', True, u'--jnodes=', None, u'намеренно',
     u'S112/T255: узлов сетки κ. В клеймо идёт УСЛОВНО (решение Amber '
     u'10.09.2026 «внести УСЛОВНО, как SingleScatter»): nojoint=1 там, где пара НЕ '
     u'считается (JointNodes <= 1), и jnodes=<N> там, где считается И число отлично '
     u'от умолчания (правило T42: нет отличия -- нет строки, склад не чужой). '
     u'Форме поле не нужно: κ считается всегда, а её отключение -- абляция, не настройка'),
    (u'JointHistories', True, u'--jn=', None, u'намеренно',
     u'S112/T255: точек на замер κ. Прежний довод «шум таблицы, а не модель» СНЯТ '
     u'решением Amber 10.09.2026: шум κ входит в поправку каскада, то есть в числа '
     u'разбора. В клеймо идёт УСЛОВНО, рядом с JointNodes; с диска читается хвостом JNTH'),
]

# Поля, которые форма матрицы заполняет из своих полей; остальные она оставляет
# умолчаниями класса. Умолчание поля формы ОБЯЗАНО совпадать с умолчанием класса
# (`A39`), иначе «Пересчитать» даёт другую матрицу, чем скрипт корпуса.
MATRIX_FORM_DEFAULTS = {
    u'MinEnergyKev': u'5',
    u'MaxEnergyKev': u'3000',
    u'NodeCount': u'140',
    u'BinKev': u'2',
    u'Histories': u'3000000',
    # Threads: у класса 0 («по числу ядер минус один»), у поля -- это же число
    # числом. Значение равно по смыслу, но не по литералу, и сверяется отдельно.
}

# ---------------------------------------------------------------------------
# РЕЕСТР 2. Настройки пути КРИВОЙ (`EfficiencyCalculationOptions`).
# Физики здесь нет НАМЕРЕННО: ключи переноса калиброваны сверкой с Geant4, и
# выведенные наружу свободными числами они превратили бы абсолютный уровень
# кривой в подгоночный (шапка класса).
# ---------------------------------------------------------------------------
CURVE = [
    (u'Histories', u'calcHistoriesBox', u'200000', u'общая', u''),
    (u'MinEnergyKev', u'calcMinEnergyBox', u'5', u'общая', u''),
    (u'MaxEnergyKev', u'calcMaxEnergyBox', u'3000', u'общая', u''),
    (u'GridMode', u'calcGridBox', None, u'неприменимо',
     u'у матрицы сетка всегда логарифмическая плюс K-края; штатного списка узлов у неё нет'),
    (u'NodeCount', u'calcPointsBox', u'34', u'общая',
     u'умолчания РАЗНЫЕ по делу: 34 узла у кривой против 140 у матрицы'),
    (u'Threads', u'calcThreadsBox', None, u'общая', u''),
]

# ---------------------------------------------------------------------------
# РЕЕСТР 3. Настроечные поля `EfficiencySimulator` -- КТО ИХ СТАВИТ.
#   имя, ставит ли путь матрицы (`MakeSimulator`), ставит ли путь кривой
#   (`EfficiencyCalculation.Run`), причина, довод.
#
# Путь кривой ставит РОВНО ТРИ поля -- `Histories`, `Seed`, `PeakHalfWidthKev`;
# всё остальное берётся умолчаниями самого симулятора. Это и есть главное
# различие путей, и оно намеренное.
# ---------------------------------------------------------------------------
SIM = [
    (u'Histories', True, True, u'общая', u''),
    (u'Seed', True, True, u'общая', u'оба пути отбивают поток от НОМЕРА узла (ResetStream)'),
    (u'PeakHalfWidthKev', True, True, u'намеренно',
     u'E34: у кривой ВСЕГДА из геометрии, у матрицы -- только при PeakToleranceFromGeometry, '
     u'потому что включение двигает пик всего склада'),
    (u'XrayEscape', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'LXrayEscape', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'KLCascade', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'CoherentPassesThrough', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'Bremsstrahlung', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'BremFromData', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'SingleScatter', True, False, u'намеренно',
     u'E34: у матрицы ветка ГАСИТСЯ, когда её выход заведомо стирается '
     u'(ResponseMatrix.SingleScatterErased); у кривой она работает по-настоящему'),
    (u'LightNonproportionality', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'AnalogContinuum', True, False, u'намеренно',
     u'кривой континуум не нужен вовсе: она отдаёт число на узел, а не гистограмму'),
    (u'BoundCompton', True, False, u'намеренно', u'у матрицы один ключ BoundScattering на три поля'),
    (u'DopplerBroadening', True, False, u'намеренно', u'у матрицы один ключ BoundScattering на три поля'),
    (u'RayleighScatter', True, False, u'намеренно', u'у матрицы один ключ BoundScattering на три поля'),
    (u'RayleighToCrystal', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'XcomPairThreshold', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'AnalogConeSampling', True, False, u'намеренно', u'оценщик аналоговой ветки, у кривой её нет'),
    (u'PositronTransport', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'PositronOffset', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'ScatterRouletteWeight', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    (u'SampleFluorescenceOutside', True, False, u'намеренно', u'кривая берёт умолчание симулятора'),
    # Ниже -- поля, которых НЕ СТАВИТ НИ ОДИН из двух путей. Расхождения между
    # путями у них нет по определению: оба берут умолчание симулятора. Их
    # двигают только выделенные пробы замера, и это правильное место.
    (u'ElectronEscape', False, False, u'неприменимо', u'калибровка переноса; двигает G4RawProbe'),
    (u'ElectronEscapeSlope', False, False, u'неприменимо', u'калибровка переноса; двигает G4RawProbe'),
    (u'ElectronEscapeT0Kev', False, False, u'неприменимо', u'калибровка переноса; двигает G4RawProbe'),
    (u'ElectronEscapeSoftAmp', False, False, u'неприменимо', u'калибровка переноса; двигает G4RawProbe'),
    (u'ElectronEscapeSoftKev', False, False, u'неприменимо', u'калибровка переноса; двигает G4RawProbe'),
    (u'ElectronEscapeCurve', False, False, u'неприменимо', u'калибровка переноса; двигает G4RawProbe'),
    (u'ElectronCarryDetour', False, False, u'неприменимо', u'калибровка заноса; двигает CoincCfProbe'),
    (u'TotalFullSphere', False, False, u'неприменимо', u'мерка полной эффективности; двигает CoincCfProbe'),
    (u'KFractionByEnergy', False, False, u'неприменимо', u'константа модели, рычага нет нигде'),
    (u'MeasuredFluorescenceYield', False, False, u'неприменимо', u'константа модели, рычага нет нигде'),
    (u'CoherentFractionOfTotal', False, False, u'неприменимо', u'выходное число сцены, не настройка'),
    (u'MountingInFront', False, False, u'неприменимо', u'признак сцены; двигает LayerProbe'),
    (u'ScoreEntranceOnly', False, False, u'неприменимо', u'режим замера; двигает ResponseProbe'),
]

# Поля `EfficiencySimulator`, которые настройками НЕ являются: выход прогона,
# счётчики, кэш. Перечислены поимённо, чтобы правило A не считало их ключами.
SIM_NOT_SETTINGS = set([
    u'LastContinuumRelativeError', u'LastContinuumIntegralError',
    u'WeightPeakBinDropped', u'CountPeakBinDroppedScattered',
    # Поля ВЛОЖЕННОГО типа (описание области сцены), а не настройки симулятора:
    # тело класса читается целиком, вложенные объявления попадают в тот же кусок.
    u'IsBox', u'IsCrystal',
])

REASONS = (u'общая', u'намеренно', u'забыто', u'неприменимо')


def read(root, rel):
    path = os.path.join(root, rel)
    if not os.path.isfile(path):
        return None
    with io.open(path, encoding=u'utf-8-sig', errors=u'replace') as fh:
        return fh.read()


def strip(text):
    u"""Убрать комментарии и строковые литералы: скобки внутри них ломали бы
    поиск тела метода, а имена полей в комментариях -- поиск потребителя."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == u'/' and i + 1 < n and text[i + 1] == u'/':
            j = text.find(u'\n', i)
            i = n if j < 0 else j
            continue
        if c == u'/' and i + 1 < n and text[i + 1] == u'*':
            j = text.find(u'*/', i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == u'"':
            i += 1
            while i < n and text[i] != u'"':
                i += 2 if text[i] == u'\\' else 1
            i += 1
            out.append(u'""')
            continue
        out.append(c)
        i += 1
    return u''.join(out)


def block(text, anchor):
    u"""Тело от якоря до парной закрывающей скобки. Текст должен быть очищен.

    Якорь -- ВЫРАЖЕНИЕ, а не подстрока: у `Load` и у `Run` есть по короткой
    перегрузке-обёртке, и поиск подстрокой брал тело обёртки. Дважды поймано
    самим сторожем при заведении (правила C и G молчали не по делу)."""
    m = re.search(anchor, text)
    i = m.start() if m else -1
    if i < 0:
        return u''
    j = text.find(u'{', i)
    if j < 0:
        return u''
    depth, k = 0, j
    while k < len(text):
        if text[k] == u'{':
            depth += 1
        elif text[k] == u'}':
            depth -= 1
            if depth == 0:
                return text[j:k + 1]
        k += 1
    return text[j:]


def class_fields(text, name):
    u"""(поле, умолчание) публичных полей класса; свойства пропускаются."""
    body = block(text, r'class\s+' + name + r'\b')
    found = []
    for m in re.finditer(r'public\s+(?:readonly\s+)?'
                         r'(bool|int|long|double|string|EfficiencyGridMode)\s+'
                         r'(\w+)\s*(=\s*([^;]+?))?;', body):
        found.append((m.group(2), (m.group(4) or u'').strip()))
    return found


def assigned_in(body, names):
    u"""Какие из `names` присваиваются внутри тела: и `Foo = ...` инициализатора,
    и `x.Foo = ...`."""
    hit = set()
    for name in names:
        if re.search(r'(^|[\s,{(.])' + name + r'\s*=[^=]', body):
            hit.add(name)
    return hit


def with_callees(text, body):
    u"""Тело метода ВМЕСТЕ с телами статических методов, которые оно зовёт.

    Заведено 09.09.2026 (`S112`): чтение шестого хвоста `JNTK` вынесено из
    `Load` отдельным методом `ReadJoint` -- вложенность чтения хвостов дошла
    до восьми уровней, -- и правило C ослепло: поле читалось, а сторож видел
    только тело самого `Load`.

    ⛔ Разбор ПО ВЫЗОВАМ, а не по списку имён: список пришлось бы дописывать
    при каждом следующем вынесении, то есть сторож слеп бы молча ровно там, где
    правку делают неаккуратно. Один уровень вглубь -- этого хватает разбору
    хвостов и не тянет за собой полдерева.
    """
    seen = [body]
    for name in sorted(set(re.findall(r'\b([A-Z]\w+)\s*\(', body))):
        callee = block(text, r'static\s+[\w\[\]<>.]+\s+' + name + r'\s*\(')
        if callee:
            seen.append(callee)
    return u'\n'.join(seen)


def used_in(body, names, prefix):
    hit = set()
    for name in names:
        if re.search(re.escape(prefix) + name + r'\b', body):
            hit.add(name)
    return hit


# ---------------------------------------------------------------------------


def judge(src):
    u"""Приговор по восьми правилам. Возвращает список строк-нарушений."""
    bad = []

    # Структурный разбор идёт по ОЧИЩЕННОМУ тексту: скобки внутри строковых
    # литералов («{0}» у string.Format) и внутри комментариев рвут поиск тела
    # метода, а имена полей в комментариях выдают несуществующего потребителя.
    # `probe` остаётся СЫРЫМ нарочно: ключи там -- строковые литералы, и очистка
    # съела бы ровно то, что проверяется.
    rm, rmb, ec, es = strip(src[RM]), strip(src[RMB]), strip(src[EC]), strip(src[ES])
    rmf, rmfl, emf = strip(src[RMF]), src[RMFL], src[EMF]
    probe = src[PROBE]

    # --- правило A: реестр равен исходнику -------------------------------
    have = [f for f, _ in class_fields(rm, u'ResponseMatrixOptions')]
    want = [r[0] for r in MATRIX]
    for f in have:
        if f not in want:
            bad.append(u'A: поле ResponseMatrixOptions.%s есть в исходнике, но НЕ В РЕЕСТРЕ '
                       u'сторожа. Ключ, заведённый в одном месте, дальше не поедет: '
                       u'дописать строку в MATRIX и в сводную таблицу T242.' % f)
    for f in want:
        if f not in have:
            bad.append(u'A: поле ResponseMatrixOptions.%s числится в реестре, а в исходнике '
                       u'его нет -- реестр отстал.' % f)

    haveC = [f for f, _ in class_fields(ec, u'EfficiencyCalculationOptions')]
    wantC = [r[0] for r in CURVE]
    for f in haveC:
        if f not in wantC:
            bad.append(u'A: поле EfficiencyCalculationOptions.%s есть в исходнике, но НЕ В '
                       u'РЕЕСТРЕ сторожа (реестр CURVE).' % f)
    for f in wantC:
        if f not in haveC:
            bad.append(u'A: поле EfficiencyCalculationOptions.%s числится в реестре, а в '
                       u'исходнике его нет.' % f)

    haveS = [f for f, _ in class_fields(es, u'EfficiencySimulator') if f not in SIM_NOT_SETTINGS]
    wantS = [r[0] for r in SIM]
    for f in haveS:
        if f not in wantS:
            bad.append(u'A: настроечное поле EfficiencySimulator.%s есть в исходнике, но НЕ В '
                       u'РЕЕСТРЕ сторожа (реестр SIM). Назвать, ставит ли его путь матрицы и '
                       u'ставит ли путь кривой.' % f)
    for f in wantS:
        if f not in haveS:
            bad.append(u'A: поле EfficiencySimulator.%s числится в реестре, а в исходнике его '
                       u'нет -- реестр отстал.' % f)

    # --- правило B: у настройки есть потребитель --------------------------
    consumer = (used_in(rmb, want, u'options.')
                | used_in(block(rm, r'class\s+ResponseMatrixOptions\b'), want, u'this.'))
    for f in want:
        if f not in consumer:
            bad.append(u'B: ResponseMatrixOptions.%s НЕ ЧИТАЕТСЯ ни построителем, ни BuildGrid '
                       u'-- ключ мёртв (S130).' % f)

    # --- правило C: что в клейме, то в файле ------------------------------
    stamp_body = block(rm, r'static\s+string\s+ComputeStamp')
    save_body = block(rm, r'public\s+void\s+Save')
    wopt = block(rm, r'static\s+void\s+WriteOptions')
    ropt = block(rm, r'static\s+ResponseMatrixOptions\s+ReadOptions')
    load_body = with_callees(rm, block(rm, r'ResponseMatrix\s+Load\(string\s+path,\s*out\s+MatrixRefusal'))
    in_stamp = used_in(stamp_body, want, u'options.')
    written = used_in(wopt, want, u'o.') | used_in(save_body, want, u'flags.')
    read_back = assigned_in(ropt, want) | used_in(load_body, want, u'matrix.Options.')
    for f in sorted(in_stamp):
        if f not in written:
            bad.append(u'C: ResponseMatrixOptions.%s входит в КЛЕЙМО, но не пишется в файл '
                       u'матрицы. Матрица с этим ключом не сойдётся сама с собой и будет '
                       u'числиться негодной навсегда (T114, A66, E34).' % f)
        if f not in read_back:
            bad.append(u'C: ResponseMatrixOptions.%s входит в КЛЕЙМО, но не читается обратно '
                       u'из файла матрицы.' % f)

    # --- правило D: реестр не врёт про исходник ---------------------------
    for name, stamped, key, field, reason, _ in MATRIX:
        if stamped and name not in in_stamp:
            bad.append(u'D: реестр говорит, что %s входит в клеймо, а ComputeStamp его не '
                       u'читает.' % name)
        if not stamped and name in in_stamp:
            bad.append(u'D: реестр говорит, что %s в клеймо НЕ входит, а ComputeStamp его '
                       u'читает.' % name)
        if key and key not in probe:
            bad.append(u'D: реестр обещает у %s рычаг пробы «%s», а в CorpusMatrixProbe.cs его '
                       u'нет.' % (name, key))
        if field and (u'this.' + field) not in rmf:
            bad.append(u'D: реестр обещает у %s поле формы «%s», а в ResponseMatrixForm.cs его '
                       u'нет.' % (name, field))
        if field and (name + u' = (') not in block(rmf, r'ResponseMatrixOptions\s+CurrentOptions'):
            bad.append(u'D: поле формы «%s» есть, но CurrentOptions() не кладёт его в '
                       u'ResponseMatrixOptions.%s -- значение до построителя не доедет.'
                       % (field, name))

    # --- правило E: умолчание поля формы равно умолчанию класса -----------
    defaults = dict(class_fields(rm, u'ResponseMatrixOptions'))
    layout = strip(rmfl)
    for name, want_default in sorted(MATRIX_FORM_DEFAULTS.items()):
        field = dict((r[0], r[3]) for r in MATRIX)[name]
        m = re.search(r'this\.' + field + r'\s*=\s*this\.Field\((.*?)\);', layout, re.S)
        if not m:
            bad.append(u'E: не нашёл заведения поля формы %s в ResponseMatrixForm.Layout.cs.'
                       % field)
            continue
        got = m.group(1).split(u',')[-1].strip()
        if got != want_default:
            bad.append(u'E: умолчание поля формы %s равно %s, а реестр ждёт %s.'
                       % (field, got, want_default))
        cls = defaults.get(name, u'').rstrip(u'0').rstrip(u'.') or defaults.get(name, u'')
        if float(want_default) != float(defaults.get(name) or 0):
            bad.append(u'E: умолчание поля формы %s (%s) разошлось с умолчанием класса '
                       u'ResponseMatrixOptions.%s (%s) -- A39: два места, и оба обязаны '
                       u'совпадать.' % (field, want_default, name, defaults.get(name)))

    # --- правило F: настройки кривой доходят до Run -----------------------
    curve_opts = block(emf, r'EfficiencyCalculationOptions\s+CurrentCalcOptions')
    cdefaults = dict(class_fields(ec, u'EfficiencyCalculationOptions'))
    emf_plain = emf
    for name, field, want_default, reason, _ in CURVE:
        if (name + u' =') not in curve_opts:
            bad.append(u'F: CurrentCalcOptions() не кладёт %s -- поле вкладки до расчёта не '
                       u'доедет.' % name)
        if field and field not in emf_plain:
            bad.append(u'F: реестр обещает у %s поле «%s», а в EfficiencyMakerForm.cs его нет.'
                       % (name, field))
        if want_default is None:
            continue
        m = re.search(r'this\.' + field + r'\s*=\s*CalcField\((.*?)\);', emf_plain, re.S)
        if not m:
            bad.append(u'F: не нашёл заведения поля %s в EfficiencyMakerForm.cs.' % field)
            continue
        got = m.group(1).split(u',')[-1].strip()
        if float(got) != float(cdefaults.get(name) or 0):
            bad.append(u'F: умолчание поля %s (%s) разошлось с умолчанием класса '
                       u'EfficiencyCalculationOptions.%s (%s) -- A39.'
                       % (field, got, name, cdefaults.get(name)))

    # --- правило G: кто что ставит симулятору -----------------------------
    make = block(rmb, r'static\s+EfficiencySimulator\s+MakeSimulator')
    run = block(ec, r'EfficiencyFitResult\s+Run\(GeometryModel\s+geometry,\s*EfficiencyCalculationOptions')
    sim_names = [r[0] for r in SIM]
    by_matrix = assigned_in(make, sim_names)
    by_curve = assigned_in(run, sim_names)
    # Зерно оба пути задают не присваиванием поля, а ResetStream -- считаем его
    # заданным там, где этот вызов есть.
    for body, hit in ((make, by_matrix), (run, by_curve)):
        if u'ResetStream' in body:
            hit.add(u'Seed')
    for name, want_m, want_c, reason, _ in SIM:
        if (name in by_matrix) != want_m:
            bad.append(u'G: EfficiencySimulator.%s -- реестр говорит «путь матрицы %s», а '
                       u'MakeSimulator говорит «%s».'
                       % (name, u'ставит' if want_m else u'не ставит',
                          u'ставит' if name in by_matrix else u'не ставит'))
        if (name in by_curve) != want_c:
            bad.append(u'G: EfficiencySimulator.%s -- реестр говорит «путь кривой %s», а '
                       u'EfficiencyCalculation.Run говорит «%s».'
                       % (name, u'ставит' if want_c else u'не ставит',
                          u'ставит' if name in by_curve else u'не ставит'))

    # --- правило H: причина названа, «забыто» -- отказ ---------------------
    for row in MATRIX:
        name, reason = row[0], row[4]
        if reason not in REASONS:
            bad.append(u'H: у ResponseMatrixOptions.%s причина «%s» -- не одно из трёх слов.'
                       % (name, reason))
        if reason == u'забыто':
            bad.append(u'H: ResponseMatrixOptions.%s помечен «забыто». Забытое чинится, а не '
                       u'описывается: завести рычаг или переписать причину.' % name)
    for row in CURVE + SIM:
        name, reason = row[0], row[3]
        if reason not in REASONS:
            bad.append(u'H: у %s причина «%s» -- не одно из трёх слов.' % (name, reason))
        if reason == u'забыто':
            bad.append(u'H: %s помечен «забыто» -- чинить, а не описывать.' % name)

    return bad


# ---------------------------------------------------------------------------


def selftest(src):
    u"""Подставить порчу и убедиться, что сторож её видит. Слепой сторож хуже
    отсутствующего: он выдаёт приёмку за проверку."""
    missed = []

    def spoiled(rel, old, new, what, count=1):
        # `count` -- сколько вхождений подменить. Единица по умолчанию, но у
        # ключа пробы имя стоит и в шапке-справке, и в разборе: подмена ПЕРВОГО
        # вхождения правила D не касается (оно спрашивает «есть ли имя в файле
        # вообще»), и самопроверка тогда мерит пустоту.
        copy = dict(src)
        if old not in copy[rel]:
            missed.append(u'самопроверка не смогла подставить порчу «%s»: якорь не найден'
                          % what)
            return
        copy[rel] = copy[rel].replace(old, new, count)
        if not judge(copy):
            missed.append(u'порча «%s» НЕ ЗАМЕЧЕНА' % what)

    # A: подброшенный ключ, заведённый только в объявлении класса.
    spoiled(RM, u'public int Seed;',
            u'public bool PlantedKeyForSelftest;\n\n        public int Seed;',
            u'новое поле ResponseMatrixOptions мимо реестра')
    # C: ключ вынут из записи в файл, оставшись в клейме.
    spoiled(RM, u'writer.Write(o.Seed);', u'writer.Write(0);',
            u'зерно перестало писаться в файл, оставшись в клейме')
    # D: рычаг пробы снят.
    spoiled(PROBE, u'--peakw=', u'--peakwidth=',
            u'ключ --peakw= переименован в пробе', count=-1)
    # E: умолчание поля формы разошлось с умолчанием класса.
    spoiled(RMFL, u'row, 8, 500, 0, 140)', u'row, 8, 500, 0, 100)',
            u'умолчание поля «узлов» формы матрицы стало 100 при классе 140')
    # G: путь кривой перестал брать допуск пика из геометрии (это и есть E34).
    spoiled(EC, u'worker.PeakHalfWidthKev = geometry.PeakHalfWidthKev',
            u'worker.Histories = worker.Histories; // geometry.PeakHalfWidthKev',
            u'путь кривой перестал ставить PeakHalfWidthKev (механизм E34)')
    # G: путь матрицы перестал доносить ключ до симулятора.
    spoiled(RMB, u'AnalogConeSampling = options.AnalogConeSampling,', u'',
            u'ключ конуса не доехал до построителя (механизм S130)')
    return missed


def table():
    print(u'=== путь МАТРИЦЫ ОТКЛИКА (ResponseMatrixOptions) ===')
    print(u'  %-27s %-7s %-13s %-14s %s'
          % (u'настройка', u'клеймо', u'рычаг пробы', u'поле формы', u'причина'))
    for name, stamped, key, field, reason, _ in MATRIX:
        print(u'  %-27s %-7s %-13s %-14s %s'
              % (name, u'да' if stamped else u'нет', key or u'-', field or u'-', reason))
    print(u'')
    print(u'=== путь КРИВОЙ (EfficiencyCalculationOptions) ===')
    for name, field, default, reason, _ in CURVE:
        print(u'  %-27s поле %-18s умолчание %-9s %s'
              % (name, field or u'-', default or u'-', reason))
    print(u'')
    print(u'=== кто что ставит EfficiencySimulator ===')
    for name, m, c, reason, _ in SIM:
        print(u'  %-27s матрица %-10s кривая %-10s %s'
              % (name, u'ставит' if m else u'умолчание',
                 u'ставит' if c else u'умолчание', reason))


def main(argv):
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    src = {}
    for rel in SOURCES:
        text = read(root, rel)
        if text is None:
            print(u'СУДИТЬ НЕЧЕГО: не найден %s' % rel.replace(os.sep, u'/'))
            return 3
        src[rel] = text

    if u'--table' in argv:
        table()
        return 0

    print(u'сторож состава ключей обоих путей (T242): реестр -- %d настроек матрицы, '
          u'%d кривой, %d полей симулятора' % (len(MATRIX), len(CURVE), len(SIM)))

    bad = judge(src)
    if bad:
        print(u'ОСТАНОВ: состав ключей разошёлся, %d нарушений:' % len(bad))
        for line in bad:
            print(u'   ' + line)
        print(u'  (самопроверка не гонялась: её опора -- те же исходники, что разошлись)')
        return 1

    missed = selftest(src)
    if missed:
        print(u'ОСТАНОВ: САМОПРОВЕРКА НЕ ПРОШЛА -- сторож слеп:')
        for m in missed:
            print(u'   ' + m)
        return 2
    print(u'  самопроверка: шесть подставленных порч пойманы, целое дерево чисто')

    if u'--selftest' in argv:
        return 0

    stamped = len([r for r in MATRIX if r[1]])
    levers = len([r for r in MATRIX if r[2]])
    fields = len([r for r in MATRIX if r[3]])
    print(u'  путь матрицы: %d настроек, в клейме %d, рычаг пробы у %d, поле формы у %d'
          % (len(MATRIX), stamped, levers, fields))
    print(u'  путь кривой: %d настроек; симулятору он ставит РОВНО ТРИ поля '
          u'(Histories, Seed, PeakHalfWidthKev), остальное -- умолчания симулятора'
          % len(CURVE))
    print(u'  односторонних ключей: намеренно %d, неприменимо %d, забыто %d'
          % (len([r for r in MATRIX + [(c[0], 0, 0, 0, c[3], 0) for c in CURVE]
                  + [(s[0], 0, 0, 0, s[3], 0) for s in SIM] if r[4] == u'намеренно']),
             len([r for r in MATRIX + [(c[0], 0, 0, 0, c[3], 0) for c in CURVE]
                  + [(s[0], 0, 0, 0, s[3], 0) for s in SIM] if r[4] == u'неприменимо']),
             0))
    print(u'СОСТАВ КЛЮЧЕЙ СОШЁЛСЯ: у каждой настройки есть потребитель, всё, что в '
          u'клейме, лежит в файле, и у каждого одностороннего ключа названа причина.')
    return 0


if __name__ == u'__main__':
    sys.exit(main(sys.argv[1:]))
