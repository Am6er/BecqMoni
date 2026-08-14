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
"""
import argparse
import io
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


def read_spe(path):
    u"""Заголовок и отсчёты одного `.spe`. Возвращает (dict, list[int])."""
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
    counts = list(struct.unpack('<%dI' % (len(body) // 4), body))

    # Поверка разбора: заявленная скорость счёта против суммы отсчётов.
    # Расходится — значит блок прочитан не тем типом или не с того места, и
    # молча брать такой спектр нельзя.
    live = float(head.get('TLIVE', 0) or 0)
    cps = head.get('CPS')
    if cps and live > 0:
        expected = float(cps.replace(',', '.')) * live
        if expected > 0 and abs(sum(counts) - expected) > max(2.0, 1e-4 * expected):
            raise ValueError('сумма отсчётов %d против CPS*TLIVE %.1f'
                             % (sum(counts), expected))
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


def to_xml(head, counts, note_extra=''):
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

    out = [u"<?xml version='1.0' encoding='utf-8'?>",
           u'<ResultDataFile>',
           u'  <FormatVersion>120920</FormatVersion>',
           u'  <ResultDataList>',
           u'    <ResultData>',
           u'      <SampleInfo>',
           u'        <Name>%s</Name>' % escape(head.get('SHIFR', '')),
           u'        <Location />',
           u'        <Time>%s</Time>' % start_time(head),
           u'        <Weight>%s</Weight>' % trim(first_number(head.get('PROBEMASS')) / 1000.0),
           u'        <Volume>%s</Volume>' % trim(first_number(head.get('PROBEVOLUME')) / 1000.0),
           u'        <Note>%s</Note>' % escape(note),
           u'      </SampleInfo>',
           u'      <DeviceConfigReference>',
           u'        <Name>%s</Name>' % DEVICE_NAME,
           u'        <Guid>%s</Guid>' % DEVICE_GUID,
           u'      </DeviceConfigReference>',
           u'      <StartTime>%s</StartTime>' % start_time(head),
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
            u'      </EnergySpectrum>',
            u'    </ResultData>',
            u'  </ResultDataList>',
            u'</ResultDataFile>']
    return u'\n'.join(out) + u'\n'


def trim(value):
    return ('%.6f' % value).rstrip('0').rstrip('.') or '0'


def escape(text):
    return (text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;'))


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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default=SRC_DEFAULT)
    ap.add_argument('--out', default=OUT_DEFAULT)
    ap.add_argument('--apply', action='store_true')
    ap.add_argument('--verify', action='store_true')
    ap.add_argument('--all', action='store_true',
                    help='включая фоны и серии временной нестабильности')
    args = ap.parse_args()

    src = resolve(args.src)
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
