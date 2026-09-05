# -*- coding: utf-8 -*-
u"""Конвертер спектров ЛСРМ/АСПЕКТ (`.spe`) в XML BecqMoni (задача B5).

ЗАЧЕМ. Поверочные эталоны Гамма-1С лежат в
`YandexDisk\\Спектры\\Спектры источники эталоны\\Spe - поверки` — это ~60
измерительных спектров с ПАСПОРТНЫМИ активностями прямо в файле, и корпусу
они нужны понятной частью. Корпус же собирается из XML BecqMoni: двенадцать
нынешних G1S попали в него уже сконвертированными сторонним набором
(spectravibe-toolkit, см. `import_vibe.py`), а на остальные конвертера не
было. Этот — свой.

ФОРМАТ, разобран по файлам 14.08.2026:

  * заголовок — строки `КЛЮЧ=значение` в cp1251 до строки `SPECTR=`;
  * дальше СРАЗУ двоичный блок: `uint32` little-endian на канал, число
    каналов = длина блока / 4 (у поверок 1024). Поверка разбора встроена:
    сумма отсчётов обязана сойтись с `CPS * TLIVE` (сходится до единицы);
  * `ENERGY=N,c0,c1,…` — полином энергокалибровки СТЕПЕНИ N (N+1
    коэффициентов), ровно в том виде, что нужен `PolynomialEnergyCalibration`;
  * `FWHM=N,a0,…` — полином ПШПВ по √E (границы видны в `FWHM_ORT`:
    LeftBound 1.414 = √2, RightBound 54.9 = √3014). НЕ переносится: корпус
    меряет разрешение сам по спектрам (`build_corpus.py`), и чужая модель
    здесь спорила бы с измеренной;
  * `TLIVE` / `TREAL` — живое и полное время; `MEASBEGIN` — дата съёмки в
    ДД-ММ-ГГ; `COMMENT` — ПАСПОРТ источника («A=106000 Бк dA=3% 19-05-2017»);
  * `SHIFR` — имя источника, `GEOMETRY`/`DISTANCE`/`DETECTOR` — обстановка,
    `RAWMASS`/`PROBEVOLUME` — масса и объём пробы (в граммах и мл; в XML
    BecqMoni кладутся килограммами и литрами, как у сторонней конверсии).

ПОВЕРКА КОНВЕРТЕРА — не на глаз: ключ `--verify` берёт файл, у которого
СТОРОННЯЯ конверсия уже есть, и сверяет с ней канал в канал плюс времена и
коэффициенты. Две независимые реализации, сошедшиеся до знака, — это и есть
доказательство разбора.

    python tools/CORPUS/scripts/spe_import.py --src=<папка .spe> [--out=<папка>]
                                              [--apply] [--verify]

⛔ `--apply` ВЫКЛЮЧЕН по умолчанию: без него печатается план и ничего не
пишется. Пишет он в БИБЛИОТЕКУ на диске пользователя (как `import_vibe.py`),
а туда кладут только по решению.

ВТОРОЙ ВХОД — СПЕКТР НОВОЙ TCCFCALC (задача `T64`, обвязка 05.09.2026)

Та же ЛСРМ пишет `.spe` и из своей монте-карловской библиотеки: сцена
`tools/tccfcalc2/README.md` §13.10 даёт `test_spectr.spe` (БЕЗ каскадного
суммирования) и `test_spectr_coi.spe` (С ним). Формат тот же, разбор тот же,
а вот ОБВЯЗКА другая, и в трёх местах:

  * шапка DLL несёт всего семь ключей — `DATE`, `TIME`, `TLIVE`, `TREAL`,
    `ENERGY`, `FWHM`, `COMMENT= A= N Bq`. Ни `SHIFR`, ни `MEASBEGIN`, ни
    массы с объёмом в ней нет, поэтому имя, время и проба берутся из СЦЕНЫ
    прогона (`--geometry`, `--nuclide`, `--decays`), а не из файла.
    ⛔ `DATE`/`TIME` из шапки НЕ БЕРУТСЯ: это зашитая в DLL константа
    `20-05-2010 12:15:11` — измерено двумя прогонами 05.09.2026, она не
    меняется ни от даты, ни от активности. Временем ставится запись файла;
  * `DeviceConfigReference` у ЛСРМ-ветки зашит на Гамма-1С, а спектр посчитан
    на НАШЕЙ геометрии — ссылка берётся по геометрии из
    `corpus/devices/` (`--device` перекрывает). Чужая ссылка тем и вредна,
    что корпусный прогон возьмёт по ней не тот прибор;
  * ключа `CPS` в шапке DLL нет, и встроенная поверка суммы пропускала сама
    себя — молча. Её место занимает тождество `A · TLIVE = число распадов`
    плюс граница `0 ≤ Σ отсчётов ≤ число распадов`; число распадов знает
    только сцена, поэтому его надо ПОДАТЬ, а не подать — ОТКАЗ.

    python tools/CORPUS/scripts/spe_import.py --tccf=<test_spectr.spe>
              --geometry=tools/effmaker/models/Nano16Pro_tube.in
              --nuclide=Cs-137 --decays=200000 [--seed=N] [--device=<имя|xml>]
              [--xml=<куда писать>] [--apply]

    python tools/CORPUS/scripts/spe_import.py --selftest

`--selftest` — положительный контроль поверки на семи плечах: два нетронутых
файла сцены обязаны пройти, пять испорченных и обеднённых — ОТКАЗАТЬ.
"""
import argparse
import datetime
import io
import math
import os
import re
import struct
import sys
import unicodedata

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from corpus_paths import resolve                     # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

LIB = r'C:\Users\moroz\YandexDisk\Спектры'
SRC_DEFAULT = os.path.join(LIB, 'Спектры источники эталоны', 'Spe - поверки')
OUT_DEFAULT = os.path.join(LIB, 'LSRM поверки')

# Имя прибора в ссылке спектра. Оно НЕ обязано существовать в конфигурации:
# `build_corpus.py` пишет корпусу свои конфиги устройств сам, а ссылка нужна
# лишь как след происхождения. Взято такое же, как у сторонней конверсии
# двенадцати нынешних G1S, чтобы обе половины пачки назывались одинаково.
DEVICE_NAME = 'УДС-ГЦ-63х63-USB №SN-01'
DEVICE_GUID = '41c6e7b2-5a01-5a6b-8491-6590a2603784'

# Каталог конфигураций приборов корпуса — оттуда берётся ссылка для спектра,
# посчитанного на нашей геометрии (`T64`, пункт 2).
DEVICES_DIR = os.path.join(HERE, os.pardir, 'corpus', 'devices')

# Геометрия -> конфигурация прибора корпуса. Пара обоснована размером
# кристалла (`tools/effmaker/models/README.md`, таблица приведения):
# ASN16 = Nano 16 Pro (1.5×1.8×6.0 -> цилиндр 1.854×5.9), Обсидиан
# 0.7×0.7×3.0 -> 0.7899, AS80 — 8×8 как есть.
# ⛔ `RadiaCode_*` СЮДА НЕ ВНЕСЕНА НАМЕРЕННО: в `corpus/devices/` лежат ДВЕ
# конфигурации RC-103 (`RC-103.xml` и `RC-103g 1024 (corpus).xml`), выбрать
# между ними размером кристалла нечем. Для неё обязателен `--device`.
GEOMETRY_DEVICE = (
    ('Nano16Pro', '1.Atom Spectra Nano 16 Pro RadiaScan 701A'),
    ('ASN16', '1.Atom Spectra Nano 16 Pro RadiaScan 701A'),
    ('AS80', 'Atom Spectra 80x80'),
    ('Obsidian', 'Obsidian'),
)

# Допуск тождества `A · TLIVE = число распадов`. Относительный: `TLIVE`
# печатается DLL с ограниченным числом знаков, и на неделящемся нацело
# отношении (200000 / 3000 = 66.6667) обрезка даёт 5·10⁻⁷ — 10⁻³ покрывает её
# с запасом в три порядка и при этом ловит подмену времени или активности.
DECAY_TOL = 1e-3


def activity_bq(head):
    u"""Активность из `COMMENT= A= 1000 Bq`. Нет строки — None.

    Это единственное место шапки DLL, куда уходит аргумент
    `CalculateSpectrum` (`tools/tccfcalc2/README.md`, §13.10).
    """
    m = re.search(r'A\s*=\s*([0-9]+(?:[.,][0-9]*)?(?:[eE][-+]?[0-9]+)?)\s*Bq',
                  head.get('COMMENT', '') or '')
    if not m:
        return None
    try:
        return float(m.group(1).replace(',', '.'))
    except ValueError:
        return None


def check_counts(head, counts, decays=None):
    u"""Поверка прочитанного. Возвращает строку — ЧЕМ именно поверено.

    ⛔ Нечем поверить — это ОТКАЗ, а не пропуск. Прежняя поверка была одна
    («сумма = `CPS`·`TLIVE`») и на файле без ключа `CPS` не исполнялась
    вовсе: условие `if cps and live > 0` пропускало само себя молча, и
    спектр брался непроверенным, а выглядело это как «проверка прошла».
    Измерено 05.09.2026 (`T64`): старый код принимал и файл с задранной на
    500 000 отсчётов суммой, и файл со сбитым временем.

    Поверок две, по диалекту шапки:

      * `CPS` есть — сумма отсчётов против `CPS`·`TLIVE` (поверочный диалект
        Гамма-1С, у всех 218 файлов пачки);
      * `CPS` нет, а сцена назвала число распадов — тождество
        `A · TLIVE = число распадов` (шапка) И граница
        `0 ≤ Σ отсчётов ≤ число распадов` (тело): один распад даёт в спектр
        не больше одного события. Первая половина ловит подмену времени или
        активности, вторая — разбор двоичного блока не тем типом или не с
        того места. Мерить их надо порознь, потому и написаны порознь.
    """
    total = sum(counts)
    live = float(head.get('TLIVE', 0) or 0)
    cps = head.get('CPS')

    # Отрицательный отсчёт — бессмыслица при любом диалекте, и она же признак
    # разбора не тем типом. Раньше такое значение прочиталось бы как
    # `uint32` и стало четырьмя миллиардами, то есть спряталось бы в сумме.
    # На 218 файлах пачки поверок отрицательных нет (измерено 05.09.2026),
    # так что проверка ничего не отнимает и стоит до выбора тождества.
    if counts and min(counts) < 0:
        low = min(range(len(counts)), key=lambda i: counts[i])
        raise ValueError('отрицательный отсчёт %d в канале %d — блок прочитан '
                         'не тем типом или не с того места'
                         % (counts[low], low))

    if cps and live > 0:
        expected = float(cps.replace(',', '.')) * live
        if expected > 0:
            if abs(total - expected) > max(2.0, 1e-4 * expected):
                raise ValueError('сумма отсчётов %d против CPS*TLIVE %.1f'
                                 % (total, expected))
            return u'CPS·TLIVE: сумма %d против ожидаемых %.1f' % (total, expected)

    activity = activity_bq(head)
    if decays and activity and live > 0:
        expected = activity * live
        tol = max(1.0, DECAY_TOL * decays)
        if abs(expected - decays) > tol:
            raise ValueError(
                'A·TLIVE = %.6g·%.6g = %.1f против заявленных сценой %d распадов '
                '(расхождение %.1f, допуск %.1f)'
                % (activity, live, expected, decays, expected - decays, tol))
        if total < 0 or total > decays:
            raise ValueError(
                'сумма отсчётов %d вне границы 0…%d: один распад не может дать '
                'больше одного события' % (total, decays))
        return (u'A·TLIVE: %.6g·%.6g = %.1f против %d распадов; сумма %d в границе 0…%d'
                % (activity, live, expected, decays, total, decays))

    missing = []
    if not cps:
        missing.append(u'нет ключа CPS')
    if live <= 0:
        missing.append(u'нет TLIVE > 0')
    if activity is None:
        missing.append(u'нет активности в COMMENT= A= N Bq')
    if not decays:
        missing.append(u'сцена не назвала число распадов (--decays)')
    raise ValueError(u'ПОВЕРИТЬ СУММУ НЕЧЕМ: %s. Непроверенный спектр не берём'
                     % u'; '.join(missing))


def read_spe(path, decays=None, require_check=True):
    u"""Заголовок и отсчёты одного `.spe`. Возвращает (dict, list[int]).

    `decays` — число распадов сцены; нужен файлам без `CPS` (спектр из DLL).
    `require_check=False` берёт спектр НЕПРОВЕРЕННЫМ и оставлен только для
    случая, когда вызывающий письменно объяснил, почему поверить нечем;
    ключа командной строки у него нет намеренно.
    """
    raw = open(path, 'rb').read()
    marker = raw.find(b'SPECTR=')
    if marker < 0:
        raise ValueError('нет блока SPECTR=')

    head = {}
    for line in raw[:marker].decode('cp1251', 'replace').splitlines():
        m = re.match(r'^([A-Za-z_][A-Za-z0-9_ ]*)=(.*)$', line)
        if m:
            head[m.group(1).strip()] = m.group(2).strip()

    body = raw[marker + len('SPECTR='):]
    if len(body) % 4:
        raise ValueError('блок отсчётов %d байт — не делится на 4' % len(body))
    # DLL пишет `int32`, поверочные файлы — `uint32`; на неотрицательных
    # отсчётах байты те же (измерено 25.08.2026), а отрицательные и должны
    # выпасть в отказ поверки ниже, а не превратиться в четыре миллиарда.
    counts = list(struct.unpack('<%di' % (len(body) // 4), body))

    if require_check:
        check_counts(head, counts, decays)
    return head, counts


def numbers(value):
    return [float(x) for x in value.replace(',', ' ').split() if x]


def start_time(head):
    u"""`MEASBEGIN=22-10-24 14:16:05.80` -> ISO. День-месяц-год, век 20xx."""
    value = head.get('MEASBEGIN', '').strip()
    m = re.match(r'^(\d{2})-(\d{2})-(\d{2})\s+(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?', value)
    if not m:
        return ''
    day, month, year, hh, mm, ss, frac = m.groups()
    micro = int(round(float('0.' + (frac or '0')) * 1e6))
    return '%04d-%s-%sT%s:%s:%s.%06d' % (2000 + int(year), month, day, hh, mm, ss, micro)


def first_number(value):
    u"""`1000.0;0.0` -> 1000.0 (второе число — погрешность)."""
    if not value:
        return 0.0
    head = value.split(';')[0].replace(',', '.').strip()
    try:
        return float(head)
    except ValueError:
        return 0.0


def to_xml(head, counts, note_extra='', scene=None):
    u"""XML BecqMoni из шапки и отсчётов.

    `scene` перекрывает поля, которых в шапке НЕТ (спектр из DLL, `T64`):
    `name`, `time`, `weight_kg`, `volume_l`, `device` = (имя, guid).
    """
    scene = scene or {}
    ecal = numbers(head.get('ENERGY', ''))
    if len(ecal) < 2:
        raise ValueError('нет ENERGY= с коэффициентами')
    order = int(ecal[0])
    coefficients = ecal[1:order + 2]
    if len(coefficients) != order + 1:
        raise ValueError('ENERGY= обещает степень %d, а коэффициентов %d'
                         % (order, len(coefficients)))

    live = float(head.get('TLIVE', 0) or 0)
    real = float(head.get('TREAL', 0) or live)
    total = sum(counts)
    note = head.get('COMMENT', '')
    if note_extra:
        note = (note + ' | ' + note_extra) if note else note_extra

    name = scene.get('name', head.get('SHIFR', ''))
    when = scene.get('time', start_time(head))
    weight = scene.get('weight_kg',
                       first_number(head.get('PROBEMASS')) / 1000.0)
    volume = scene.get('volume_l',
                       first_number(head.get('PROBEVOLUME')) / 1000.0)
    dev_name, dev_guid = scene.get('device', (DEVICE_NAME, DEVICE_GUID))

    out = [u"<?xml version='1.0' encoding='utf-8'?>",
           u'<ResultDataFile>',
           u'  <FormatVersion>120920</FormatVersion>',
           u'  <ResultDataList>',
           u'    <ResultData>',
           u'      <SampleInfo>',
           u'        <Name>%s</Name>' % escape(name),
           u'        <Location />',
           u'        <Time>%s</Time>' % when,
           u'        <Weight>%s</Weight>' % trim(weight),
           u'        <Volume>%s</Volume>' % trim(volume),
           u'        <Note>%s</Note>' % escape(note),
           u'      </SampleInfo>',
           u'      <DeviceConfigReference>',
           u'        <Name>%s</Name>' % escape(dev_name),
           u'        <Guid>%s</Guid>' % dev_guid,
           u'      </DeviceConfigReference>',
           u'      <StartTime>%s</StartTime>' % when,
           u'      <EnergySpectrum>',
           u'        <NumberOfChannels>%d</NumberOfChannels>' % len(counts),
           u'        <ChannelPitch>1</ChannelPitch>',
           u'        <EnergyCalibration>',
           u'          <PolynomialOrder>%d</PolynomialOrder>' % order,
           u'          <Coefficients>']
    for c in coefficients:
        out.append(u'            <Coefficient>%s</Coefficient>' % repr(c))
    out += [u'          </Coefficients>',
            u'        </EnergyCalibration>',
            u'        <ValidPulseCount>%d</ValidPulseCount>' % total,
            u'        <TotalPulseCount>%d</TotalPulseCount>' % total,
            u'        <MeasurementTime>%s</MeasurementTime>' % trim(real),
            u'        <LiveTime>%s</LiveTime>' % trim(live),
            u'        <NumberOfSamples>0</NumberOfSamples>',
            u'        <Spectrum>']
    out += [u'          <DataPoint>%d</DataPoint>' % c for c in counts]
    out += [u'        </Spectrum>',
            u'      </EnergySpectrum>']
    # Разрешение переносится ТОЛЬКО когда оно вход сцены, а не измеряемая
    # величина (спектр из DLL). У поверочных файлов ЛСРМ модель ПШПВ
    # по-прежнему не переносится: там её меряет `build_corpus.py` по самому
    # спектру, и чужая спорила бы с измеренной.
    if scene.get('fwhm'):
        out += [u'      <SqrtFwhmCalibration>',
                u'        <CalibrationPeaks />',
                u'        <Coefficients>']
        out += [u'          <Coefficient>%s</Coefficient>' % repr(c)
                for c in scene['fwhm']]
        out += [u'        </Coefficients>',
                u'        <PeakType>0</PeakType>',
                u'        <ExpGaussExpLeftTail>1.0</ExpGaussExpLeftTail>',
                u'        <ExpGaussExpRightTail>1.0</ExpGaussExpRightTail>',
                u'        <Chi2pNdp>-1</Chi2pNdp>',
                u'      </SqrtFwhmCalibration>']
    out += [u'    </ResultData>',
            u'  </ResultDataList>',
            u'</ResultDataFile>']
    return u'\n'.join(out) + u'\n'


def trim(value):
    return ('%.6f' % value).rstrip('0').rstrip('.') or '0'


def escape(text):
    return (text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;'))


# ---------------------------------------------------------------------------
# Обвязка под спектр новой TCCFCALC (`T64`)
# ---------------------------------------------------------------------------
# Сцена §13.10 журнала `tools/tccfcalc2/README.md`: она же — постановка
# положительного контроля `--selftest`.
TCCF_DIR = os.path.join(HERE, os.pardir, os.pardir, 'tccfcalc2', 'out', 'spectr')
TCCF_GEOMETRY = os.path.join(HERE, os.pardir, os.pardir, 'effmaker', 'models',
                             'Nano16Pro_tube.in')
TCCF_SCENE = {'nuclide': 'Cs-137', 'decays': 200000, 'seed': 20260824}

# Дата и время в шапке DLL — ЗАШИТАЯ КОНСТАНТА, а не время прогона. Измерено
# 05.09.2026 двумя прогонами подряд (A = 1000 и A = 2000 Бк, 05.09.2026):
# обе шапки несут `DATE=20-05-2010`, `TIME=12:15:11`. Верить ей нельзя, и
# читать её отдельным диалектом тоже нельзя — временем ставится запись файла.
DLL_FAKE_DATE = ('20-05-2010', '12:15:11')


def geometry_keys(path):
    u"""Ключи `ИМЯ = значение [ед.]` файла геометрии `.in`."""
    keys = {}
    with io.open(resolve(path), encoding='latin-1') as fh:
        for line in fh:
            m = re.match(r'^\s*([A-Za-z_][A-Za-z0-9_\[\].]*)\s*=\s*(\S+)', line)
            if m:
                keys[m.group(1)] = m.group(2)
    return keys


def source_probe(geometry_path):
    u"""Масса (кг), объём (л) и словесное обоснование пробы из геометрии.

    Считается из САМОЙ геометрии, а не назначается: шапка DLL массы с объёмом
    не несёт вовсе, и подставить сюда что-нибудь «правдоподобное» значило бы
    выдумать пробу.
    """
    keys = geometry_keys(geometry_path)
    kind = keys.get('SourceType', '').upper()
    if kind == 'POINT':
        return 0.0, 0.0, (u'SourceType = POINT: у точечного источника массы и '
                          u'объёма НЕТ, нули здесь верны, а не пропущены')
    if kind == 'CYLINDER':
        outer = float(keys['SC_BeakerDiameter'])
        wall = float(keys.get('SC_BeakerSideWallThickness', 0) or 0)
        height = float(keys['SC_SourceHeight'])
        rho = float(keys.get('SC_RoSource', 0) or 0)
        inner = outer - 2.0 * wall
        volume = math.pi * (inner / 2.0) ** 2 * height          # см³ = мл
        return (rho * volume / 1000.0, volume / 1000.0,
                u'SourceType = CYLINDER: D %g − 2·%g = %g см, H %g см, '
                u'ρ %g г/см³ -> V = %.4f мл, m = %.4f г'
                % (outer, wall, inner, height, rho, volume, rho * volume))
    raise ValueError(
        u'источник «%s» этой обвязкой не считается: у Маринелли проба кольцевая, '
        u'и объём её надо выводить отдельно — угадывать нельзя' % (kind or '?'))


def device_reference(geometry_path=None, explicit=None):
    u"""(имя, guid, файл) конфигурации прибора корпуса.

    Имя и guid читаются из САМОГО файла конфигурации, а не переписываются в
    исходник: переписанная пара разъезжается с деревом молча. Пары для
    геометрии нет — ОТКАЗ, а не подстановка чего-нибудь похожего.
    """
    if explicit:
        base = explicit[:-4] if explicit.lower().endswith('.xml') else explicit
        path = (explicit if os.path.isfile(explicit)
                else os.path.join(DEVICES_DIR, base + '.xml'))
    else:
        name = os.path.splitext(os.path.basename(geometry_path))[0]
        for prefix, device in GEOMETRY_DEVICE:
            if name.upper().startswith(prefix.upper()):
                path = os.path.join(DEVICES_DIR, device + '.xml')
                break
        else:
            raise ValueError(
                u'геометрии «%s» в таблице GEOMETRY_DEVICE пары нет — назвать '
                u'прибор ключом --device' % name)
    if not os.path.isfile(path):
        raise ValueError(u'конфигурации прибора нет на диске: %s' % path)
    text = io.open(path, encoding='utf-8-sig').read()
    guid = re.search(r'<Guid>([^<]+)</Guid>', text)
    name = re.search(r'<Name>([^<]+)</Name>', text)
    if not guid or not name:
        raise ValueError(u'в конфигурации %s нет <Guid> или <Name>' % path)
    return name.group(1), guid.group(1), os.path.normpath(path)


def fwhm_calibration(head, n_channels):
    u"""ПШПВ шапки -> коэффициенты `SqrtFwhmCalibration`. Возвращает (коэф., худшее %).

    ⚠ Семейства РАЗНЫЕ: DLL пишет `FWHM=2, c0, c1, c2` и читает его как
    ПШПВ(E) = c0 + c1·√E + c2·E в кэВ (README §13.10), а приложение хранит
    ПШПВ(канал) = √(a + b·канал + c·канал²) в каналах. Точного перевода нет,
    но приближение выходит хорошим по существу задачи: у DLL главный член
    ровно c1·√E, значит ПШПВ² линейна по каналу — и три параметра ложатся
    почти без остатка. Худшее расхождение возвращается вместе с
    коэффициентами и уходит в `<Note>`: молча приближать чужую модель нельзя.

    ⛔ Здесь разрешение — НЕ измеряемая величина, а ВХОД сцены: его задал
    блок `AN_*`. Довод ЛСРМ-ветки («корпус меряет разрешение сам по
    спектрам») тут не работает, и переносить ПШПВ надо.
    """
    parts = numbers(head.get('FWHM', ''))
    if len(parts) < 4 or int(parts[0]) != 2:
        return None, None
    c0, c1, c2 = parts[1], parts[2], parts[3]
    ecal = numbers(head.get('ENERGY', ''))
    if len(ecal) < 3:
        return None, None
    coef = ecal[1:int(ecal[0]) + 2]

    def energy(ch):
        return sum(c * ch ** i for i, c in enumerate(coef))

    def dedch(ch):
        return sum(i * c * ch ** (i - 1) for i, c in enumerate(coef) if i)

    xs, ys = [], []
    for ch in range(1, n_channels):
        e = energy(ch)
        slope = dedch(ch)
        if e <= 0 or slope <= 0:
            continue
        xs.append(float(ch))
        ys.append(((c0 + c1 * math.sqrt(e) + c2 * e) / slope) ** 2)
    if len(xs) < 4:
        return None, None

    rows = [(1.0, x, x * x) for x in xs]
    ata = [[sum(r[i] * r[j] for r in rows) for j in range(3)] for i in range(3)]
    atb = [sum(r[i] * y for r, y in zip(rows, ys)) for i in range(3)]
    m = [ata[i] + [atb[i]] for i in range(3)]
    for i in range(3):
        p = max(range(i, 3), key=lambda k: abs(m[k][i]))
        m[i], m[p] = m[p], m[i]
        if m[i][i] == 0:
            return None, None
        for k in range(3):
            if k == i:
                continue
            f = m[k][i] / m[i][i]
            for j in range(i, 4):
                m[k][j] -= f * m[i][j]
    a, b, c = [m[i][3] / m[i][i] for i in range(3)]

    worst, worst_at = 0.0, 0.0
    for x, y in zip(xs, ys):
        got = a + b * x + c * x * x
        got = math.sqrt(got) if got > 0 else 0.0
        want = math.sqrt(y)
        if want > 0 and abs(got - want) / want * 100.0 > worst:
            worst, worst_at = abs(got - want) / want * 100.0, energy(x)
    return (a, b, c), (worst, worst_at)


def file_time(path):
    u"""Время ЗАПИСИ файла в ISO — им ставится время съёмки.

    Шапка DLL несёт `DATE`/`TIME`, но это константа (см. `DLL_FAKE_DATE`), а
    запись файла — настоящий момент, когда DLL его положила.
    """
    when = datetime.datetime.fromtimestamp(os.path.getmtime(path))
    return when.strftime('%Y-%m-%dT%H:%M:%S.') + '%06d' % when.microsecond


def tccf_convert(spe_path, geometry, nuclide, decays, seed=None, device=None,
                 when=None):
    u"""Спектр новой TCCFCALC -> XML BecqMoni. Возвращает (head, counts, scene, xml).

    `when` — время съёмки в ISO. Не задано — берётся запись файла, и это
    ⚠ верно ТОЛЬКО в каталоге самого прогона: копия и выгрузка из git несут
    своё время, а не время DLL. Для файла, положенного в дерево, время надо
    ЗАКРЕПИТЬ ключом, иначе XML перестаёт быть воспроизводимым.
    """
    head, counts = read_spe(spe_path, decays=decays)
    verified = check_counts(head, counts, decays)
    coi = os.path.splitext(os.path.basename(spe_path))[0].lower().endswith('_coi')
    dev_name, dev_guid, dev_path = device_reference(geometry, device)
    weight, volume, probe_words = source_probe(geometry)
    geom = os.path.splitext(os.path.basename(geometry))[0]
    activity = activity_bq(head)

    fwhm, fwhm_worst = fwhm_calibration(head, len(counts))

    name = u'TCCFCALC %s %s %s' % (
        nuclide, geom,
        u'каскады ВКЛ' if coi else u'каскады ВЫКЛ')
    note = (u'TCCFCALC 2.10.1844, сцена README §13.10: геометрия %s, нуклид %s, '
            u'распадов %d, зерно %s, A = %s Бк, TLIVE = %s с; каскадное '
            u'суммирование %s (%s). Проба: %s. Дата в шапке DLL — константа '
            u'%s %s, временем взято %s.'
            % (geom, nuclide, decays, seed if seed is not None else '?',
               ('%g' % activity) if activity else '?', head.get('TLIVE', '?'),
               u'ВКЛЮЧЕНО' if coi else u'ВЫКЛЮЧЕНО',
               os.path.basename(spe_path), probe_words,
               DLL_FAKE_DATE[0], DLL_FAKE_DATE[1],
               u'закреплённое ключом --when' if when else u'время записи файла'))
    if fwhm:
        note += (u' ПШПВ сцены (FWHM=%s) переложена в модель приложения '
                 u'√(a+b·ch+c·ch²), худшее расхождение по всей шкале %.3f %% '
                 u'на %.0f кэВ.'
                 % (head.get('FWHM', ''), fwhm_worst[0], fwhm_worst[1]))

    scene = {'name': name, 'time': when or file_time(spe_path),
             'time_from': u'ключ --when' if when else u'запись файла',
             'weight_kg': weight, 'volume_l': volume,
             'device': (dev_name, dev_guid), 'device_path': dev_path,
             'probe_words': probe_words, 'coincidences': coi,
             'verified': verified, 'note': note,
             'fwhm': fwhm, 'fwhm_worst': fwhm_worst}
    return head, counts, scene, to_xml(head, counts, note, scene)


def _mutate(raw, drop=None, tlive=None, bump=None):
    u"""Испортить копию `.spe` ровно одним способом — для плеч контроля."""
    marker = raw.find(b'SPECTR=')
    head, body = raw[:marker], raw[marker + len('SPECTR='):]
    if drop:
        head = b''.join(l + b'\r\n' for l in head.split(b'\r\n')
                        if l and not l.startswith(drop))
    if tlive is not None:
        head = re.sub(br'TLIVE=[^\r\n]*', b'TLIVE=' + tlive, head, count=1)
    counts = list(struct.unpack('<%di' % (len(body) // 4), body))
    if bump:
        counts[bump[0]] += bump[1]
    return head + b'SPECTR=' + struct.pack('<%di' % len(counts), *counts)


def selftest():
    u"""Положительный контроль поверки: семь плеч, пять из них обязаны ОТКАЗАТЬ.

    Без этого поверку принимать нельзя: прежняя ведь тоже «работала» — она
    молча пропускала сама себя, и выглядело это как успех.
    """
    import tempfile

    plain = os.path.join(TCCF_DIR, 'test_spectr.spe')
    coinc = os.path.join(TCCF_DIR, 'test_spectr_coi.spe')
    for path in (plain, coinc):
        if not os.path.isfile(path):
            print(u'НЕТ ФАЙЛА СЦЕНЫ: %s' % path)
            return False
    n = TCCF_SCENE['decays']
    raw = open(plain, 'rb').read()

    tmp = tempfile.mkdtemp(prefix='t64_')
    arms = []

    def arm(label, path, decays, must_pass):
        arms.append((label, path, decays, must_pass))

    def write(name, data):
        dest = os.path.join(tmp, name)
        open(dest, 'wb').write(data)
        return dest

    arm(u'1 нетронутый test_spectr.spe (каскады ВЫКЛ)', plain, n, True)
    arm(u'2 нетронутый test_spectr_coi.spe (каскады ВКЛ)', coinc, n, True)
    arm(u'3 шапка: TLIVE 200 -> 190',
        write('tlive.spe', _mutate(raw, tlive=b'190')), n, False)
    arm(u'4 тело: +500000 отсчётов в канал 500',
        write('body.spe', _mutate(raw, bump=(500, 500000))), n, False)
    arm(u'5 шапка: строка активности убрана',
        write('noa.spe', _mutate(raw, drop=b'COMMENT=')), n, False)
    arm(u'6 шапка: TLIVE убран',
        write('nolive.spe', _mutate(raw, drop=b'TLIVE=')), n, False)
    arm(u'7 сцена не назвала число распадов', plain, None, False)

    ok = True
    sums = {}
    for label, path, decays, must_pass in arms:
        try:
            head, counts = read_spe(path, decays=decays)
            got, words = True, check_counts(head, counts, decays)
            sums[label[0]] = sum(counts)
        except ValueError as ex:                             # noqa: BLE001
            got, words = False, u'%s' % ex
        verdict = u'СОШЛОСЬ' if got == must_pass else u'⛔ НЕ СОШЛОСЬ'
        if got != must_pass:
            ok = False
        print(u'%-46s ждали %-6s получили %-6s %s\n%s%s'
              % (label, u'приём' if must_pass else u'ОТКАЗ',
                 u'приём' if got else u'ОТКАЗ', verdict, u' ' * 4, words))

    # Каскадное суммирование обязано ОБЕДНЯТЬ одиночные линии: сумма `_coi`
    # меньше. Это и есть признак, которым два выходных файла разведены
    # (README §13.10, измерение на Co-60).
    if '1' in sums and '2' in sums:
        print(u'\nсумма без каскадов %d, с каскадами %d, разница %+d (%.2f %%)'
              % (sums['1'], sums['2'], sums['2'] - sums['1'],
                 100.0 * (sums['2'] - sums['1']) / sums['1']))
        if sums['2'] >= sums['1']:
            print(u'⛔ файл `_coi` не обеднён — либо файлы перепутаны, либо '
                  u'каскады не считались')
            ok = False
    print(u'\nСАМОПОВЕРКА:', u'СОШЛОСЬ' if ok else u'РАСХОЖДЕНИЕ')
    return ok


# ---------------------------------------------------------------------------
# Поверка против сторонней конверсии
# ---------------------------------------------------------------------------
VERIFY_PAIRS = [
    (os.path.join('Поверка 2024', 'Дента120мл', 'Th232_420-7-17_Дента-120мл_0cm.spe'),
     os.path.join(LIB, 'SpectraVibe', 'Gamma-1S', 'Th232 Дента-120мл.xml')),
    (os.path.join('Поверка 2024', 'Маринелли', 'Th232_420-7-17_Маринелли_0cm.spe'),
     os.path.join(LIB, 'SpectraVibe', 'Gamma-1S', 'Th232 Маринелли-1л.xml')),
    (os.path.join('Поверка 2024', 'Петри-60мл', 'Ra226_420-7-18_Петри-60мл_0cm.spe'),
     os.path.join(LIB, 'SpectraVibe', 'Gamma-1S', 'Ra226 Петри-60мл.xml')),
    (os.path.join('Поверка 2024', 'Точечная-25см', 'Th-228 №309_Точечная-25см_25cm.spe'),
     os.path.join(LIB, 'SpectraVibe', 'Gamma-1S', 'Th228 точечный 25см.xml')),
]


def verify(src):
    u"""Сверка с независимой конверсией: канал в канал, времена, полином."""
    ok = True
    for rel, other in VERIFY_PAIRS:
        path = resolve(os.path.join(src, rel))
        head, counts = read_spe(path)
        text = io.open(resolve(other), encoding='utf-8-sig').read()
        # Только ПЕРВЫЙ <EnergySpectrum>: у сторонней конверсии следом идёт
        # <BackgroundEnergySpectrum> со своими каналами и своей калибровкой,
        # и сплошной поиск по файлу склеивал бы два спектра в один.
        head_block = text[:text.find('<BackgroundEnergySpectrum>')
                          if '<BackgroundEnergySpectrum>' in text else len(text)]
        theirs = [int(x) for x in re.findall(r'<DataPoint>(-?\d+)</DataPoint>', head_block)]
        live = float(re.search(r'<LiveTime>([^<]+)', head_block).group(1))
        real = float(re.search(r'<MeasurementTime>([^<]+)', head_block).group(1))
        their_coef = [float(x) for x in
                      re.findall(r'<Coefficient>([^<]+)</Coefficient>', head_block)]
        ours_coef = numbers(head.get('ENERGY', ''))[1:]

        same = counts == theirs
        dlive = abs(live - float(head.get('TLIVE', 0)))
        dreal = abs(real - float(head.get('TREAL', 0)))
        ncoef = min(len(their_coef), len(ours_coef))
        dcoef = max([abs(a - b) / max(abs(b), 1e-12)
                     for a, b in zip(their_coef[:ncoef], ours_coef[:ncoef])] or [0.0])
        print('%-46s каналов %d/%d %s; Δживое %.3f с, Δполное %.3f с; '
              'коэф. %d/%d, макс. рассогл. %.2g'
              % (os.path.basename(rel), len(counts), len(theirs),
                 'СОШЛИСЬ' if same else 'РАЗОШЛИСЬ', dlive, dreal,
                 len(ours_coef), len(their_coef), dcoef))
        if not same or dlive > 0.01 or dreal > 0.01 or dcoef > 1e-6:
            ok = False
    print('ПОВЕРКА:', 'СОШЛОСЬ' if ok else 'РАСХОЖДЕНИЕ')
    return ok


SKIP_DIRS = ('Временная нестабильность', 'Фон вода', 'фон пустая защита',
             'Фон с открытыми крышками', 'Фон закр кр', 'Фон откр кр')


def tccf_main(args):
    u"""Один спектр TCCFCALC -> XML. План печатается всегда, пишет `--apply`."""
    spe = resolve(args.tccf)
    try:
        head, counts, scene, xml = tccf_convert(
            spe, args.geometry, args.nuclide, args.decays,
            seed=args.seed, device=args.device, when=args.when)
    except (ValueError, IOError) as ex:                      # noqa: BLE001
        print(u'ОТКАЗ на %s: %s' % (os.path.basename(spe), ex))
        return False

    # Сцена печатается ЦЕЛИКОМ и первой: у ключей есть умолчания (сцена
    # §13.10), и умолчание, взятое молча на чужом файле, — это ровно та
    # подстановка, от которой предостерегает соглашение дерева.
    print(u'сцена: геометрия %s, нуклид %s, распадов %d, зерно %s'
          % (os.path.basename(args.geometry), args.nuclide, args.decays,
             args.seed))
    print(u'%s: %d каналов, %d отсчётов, TLIVE %s с, каскады %s'
          % (os.path.basename(spe), len(counts), sum(counts),
             head.get('TLIVE', '?'),
             u'ВКЛ' if scene['coincidences'] else u'ВЫКЛ'))
    print(u'  поверено: %s' % scene['verified'])
    print(u'  <Name>      %s' % scene['name'])
    print(u'  <Time>      %s  (%s; в шапке DLL стоит константа %s %s)'
          % (scene['time'], scene['time_from'],
             DLL_FAKE_DATE[0], DLL_FAKE_DATE[1]))
    print(u'  <Weight>    %s кг' % trim(scene['weight_kg']))
    print(u'  <Volume>    %s л   — %s' % (trim(scene['volume_l']),
                                          scene['probe_words']))
    if scene.get('fwhm'):
        print(u'  ПШПВ        %s -> a=%.6g b=%.6g c=%.6g, худшее %.3f %% на %.0f кэВ'
              % (head.get('FWHM', ''), scene['fwhm'][0], scene['fwhm'][1],
                 scene['fwhm'][2], scene['fwhm_worst'][0], scene['fwhm_worst'][1]))
    else:
        print(u'  ПШПВ        в шапке нет — модель разрешения НЕ переложена')
    print(u'  прибор      %s' % scene['device'][0])
    print(u'              guid %s' % scene['device'][1])
    print(u'              %s' % scene['device_path'])

    dest = args.xml or os.path.splitext(spe)[0] + '.xml'
    print(u'  -> %s' % dest)
    if not args.apply:
        print(u'\n--apply не задан: файл не записан.')
        return True
    folder = os.path.dirname(os.path.abspath(dest))
    if folder and not os.path.isdir(folder):
        os.makedirs(folder)
    with io.open(dest, 'w', encoding='utf-8', newline='') as fh:
        fh.write(xml)
    print(u'\nЗАПИСАНО.')
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default=SRC_DEFAULT)
    ap.add_argument('--out', default=OUT_DEFAULT)
    ap.add_argument('--apply', action='store_true')
    ap.add_argument('--verify', action='store_true')
    ap.add_argument('--all', action='store_true',
                    help='включая фоны и серии временной нестабильности')
    ap.add_argument('--file', action='append', default=[],
                    help='конкретный .spe (путь от --src); можно повторять. '
                         'Нужен фонам: их серии НАКОПИТЕЛЬНЫЕ, и берётся '
                         'последний файл, а не все подряд')
    # Обвязка под спектр новой TCCFCALC (`T64`).
    ap.add_argument('--tccf', help='спектр новой TCCFCALC (.spe из DLL)')
    ap.add_argument('--geometry', default=TCCF_GEOMETRY,
                    help='геометрия сцены (.in) — из неё проба и прибор')
    ap.add_argument('--nuclide', default=TCCF_SCENE['nuclide'])
    ap.add_argument('--decays', type=int, default=TCCF_SCENE['decays'],
                    help='число распадов сцены; им поверяется шапка и сумма')
    ap.add_argument('--seed', default=str(TCCF_SCENE['seed']))
    ap.add_argument('--device', help='конфигурация прибора: имя или .xml; '
                                     'без него — по геометрии')
    ap.add_argument('--xml', help='куда писать XML (умолчание — рядом с .spe)')
    ap.add_argument('--when', help='время съёмки ISO; без него — запись файла, '
                                   'а она верна только в каталоге прогона')
    ap.add_argument('--selftest', action='store_true',
                    help='положительный контроль поверки на сцене §13.10')
    args = ap.parse_args()

    if args.selftest:
        sys.exit(0 if selftest() else 1)

    if args.tccf:
        sys.exit(0 if tccf_main(args) else 1)

    src = resolve(args.src)
    if args.file:
        for rel in args.file:
            path = resolve(os.path.join(src, rel))
            head, counts = read_spe(path)
            dest_dir = os.path.join(args.out, 'Фоны поверок')
            dest = os.path.join(dest_dir, os.path.splitext(os.path.basename(rel))[0] + '.xml')
            print('%-46s %5d кан, %9.1f с, %9d отсч. -> %s'
                  % (os.path.basename(rel), len(counts),
                     float(head.get('TLIVE', 0) or 0), sum(counts), dest))
            if args.apply:
                if not os.path.isdir(dest_dir):
                    os.makedirs(dest_dir)
                with io.open(dest, 'w', encoding='utf-8', newline='') as fh:
                    fh.write(to_xml(head, counts, 'ЛСРМ фон поверки, последний файл накопительной серии'))
        if not args.apply:
            print('\n--apply не задан: файлы не записаны.')
        return
    if args.verify:
        sys.exit(0 if verify(src) else 1)

    plan = []
    for dirpath, _dirs, files in os.walk(src):
        folder = os.path.basename(dirpath)
        if not args.all and folder in SKIP_DIRS:
            continue
        for name in sorted(files):
            if not name.lower().endswith('.spe'):
                continue
            path = os.path.join(dirpath, name)
            year = '2016' if '2016' in dirpath else ('2024' if '2024' in dirpath else '?')
            try:
                head, counts = read_spe(path)
            except Exception as ex:                       # noqa: BLE001
                print('%-52s ОШИБКА: %s' % (name, ex))
                continue
            plan.append((year, folder, name, path, head, counts))

    print('к переносу: %d спектров' % len(plan))
    for year, folder, name, _path, head, counts in plan:
        print('  %s %-22s %-34s %5d кан, %9.1f с, %10d отсч.  %s'
              % (year, folder, head.get('SHIFR', name)[:34], len(counts),
                 float(head.get('TLIVE', 0) or 0), sum(counts),
                 head.get('COMMENT', '')[:46]))

    if not args.apply:
        print('\n--apply не задан: файлы не записаны.')
        return

    written = 0
    for year, folder, name, path, head, counts in plan:
        dest_dir = os.path.join(args.out, 'Поверка ' + year, folder)
        if not os.path.isdir(dest_dir):
            os.makedirs(dest_dir)
        dest = os.path.join(dest_dir, os.path.splitext(name)[0] + '.xml')
        note = 'ЛСРМ %s, поверка %s, %s' % (head.get('GEOMETRY', ''), year,
                                            head.get('DETECTOR', ''))
        with io.open(dest, 'w', encoding='utf-8', newline='') as fh:
            fh.write(to_xml(head, counts, note))
        written += 1
    print('\nЗАПИСАНО: %d файлов в %s' % (written, args.out))


if __name__ == '__main__':
    main()
