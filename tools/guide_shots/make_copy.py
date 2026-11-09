# -*- coding: utf-8 -*-
u"""Копия программы и КОНФИГ ГИДА для снимков `docs/` (`AMBER319`, 08–09.11.2026).

Зовёт `shoot.ps1`; руками — только для разбора:

  python -I make_copy.py base   --work=<каталог> --bin=<сборка> [--store=<склад корпуса>] [--disk-curve=corpus|file]
  python -I make_copy.py stage  --work=<каталог> --scene=<сцена> --lang=<ru|en>
  python -I make_copy.py tokens                 — личные метки настоящего конфига (для `check.py`)

⛔ Снимки строятся ТОЛЬКО из дерева. Конфиг гида — нейтральный, придуманный, закреплённый:
от правок конфига человека (`%AppData%\\BecqMoni`) снимок не меняется. `base` и `stage` его
не читают; читает только `tokens` — список того, чего на снимке быть не должно, и этот список
в дерево не пишется.

Из чего собрана копия (`base`, каталог <work>\\app — сборка с конфигом рядом, портативная
раскладка):

  * главный конфиг — `config/BecquerelMonitor.xml` рядом: цвета, единицы, умолчания графика
    и окон, на которых сняты снимки гида;
  * библиотека нуклидов — поставочная `config/NuclideDefinition.xml` (сеты «Cs-137 + K-40»,
    «Th-232 + K-40»); вещества и шаблоны конструктора — встроенный засев программы (файлов нет);
  * приборы (`GUIDE_DEVICES`) — заглушки корпуса `tools/CORPUS/corpus/devices` под именами
    моделей, с придуманными датами «Обновлён»; настройки связи — несуществующие: порт COM99,
    серийный закрыт точками, адрес BLE 0. ⛔ Н1 (журнал `handover/handover-2026-11-07-guide.md`):
    окно прибора AtomSpectra при загрузке шлёт `-cal` в порт — с COM99 обращения к прибору нет.
    Учебные приборы AmplitudaSerial, AmplitudaUSB, AtomSpectra Wi-Fi — с умолчаниями;
  * кривые прибора диска: узел корпуса `AS80_th_disk` (из `AS80_Th232Medal`) и придуманная
    кривая конструктора «Цилиндр 40×15» (сосуд PE 1 мм, проба Soil 1.5 г/см³, 14 мм);
  * склад матриц — матрицы кривых спектров сцен из склада корпуса (`--store`; склад в git не
    входит, без него — отказ);
  * спектры сцен — из дерева: `ASN16_Cs137` корпуса и `AS80_Th232_disk` витрины FSA; кривая диска
    — корпусная (`--disk-curve=corpus`, решение Amber 08.11.2026 «Свежая кривая, числа обновить»),
    `file` — своя кривая файла витрины `phys=18` (сверка со снимками 07.11.2026); имя прибора —
    модель, имя файла фона — нейтральное на языке снимка (`stage`).

`stage` готовит копию к сцене и языку: язык, набор приборов сцены, документ раскладки,
у `main-fsa-nomatrix` — склад без матрицы кривой диска.
"""
import argparse
import io
import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GUIDE_CONFIG = os.path.join(HERE, u'config')

# Спектры сцен: путь в дереве и имя файла фона на языке снимка.
SPECTRA = {
    u'ASN16_Cs137': (u'tools/CORPUS/corpus/spectra/ASN16_Cs137.xml',
                     {u'ru': u'Фон в домике.xml', u'en': u'Background in lead house.xml'}),
    u'AS80_Th232_disk': (u'tools/fsa_showcase/spectra/AS80_Th232_disk.xml',
                         {u'ru': u'Фон.xml', u'en': u'Background.xml'}),
}
DISK_CURVE_FROM = u'tools/CORPUS/corpus/spectra/AS80_Th232Medal.xml'
STORE = u'tools/CORPUS/corpus/geometries/response'
CORPUS_DEVICES = u'tools/CORPUS/corpus/devices'
NUCLIDES = u'config/NuclideDefinition.xml'

ATOMSPECTRA = (u'<AtomSpectraDeviceConfig><ComPortName>COM99</ComPortName><BaudRate>600000</BaudRate>'
               u'<DeadTimeValue>0</DeadTimeValue></AtomSpectraDeviceConfig>')


def radiacode(model):
    return (u'<RadiaCodeDeviceConfig><DeviceSerial>%s-••••••</DeviceSerial>'
            u'<AddressBLE>0</AddressBLE></RadiaCodeDeviceConfig>' % model)


OBSIDIAN = (u'<ObsidianDeviceConfig><DeviceSerial>••••</DeviceSerial>'
            u'<AddressBLE>0</AddressBLE></ObsidianDeviceConfig>')

# Приборы конфига гида: имя, откуда, «Обновлён» (придумано, задаёт порядок списка), связь.
# Откуда: файл заглушки корпуса, `spectrum:<ключ SPECTRA>` — прибор этого спектра (по Guid),
# `synthetic:<тип>` — учебный прибор с умолчаниями (тип, число каналов, блок связи).
GUIDE_DEVICES = [
    (u'Atom Spectra 80x80', u'spectrum:AS80_Th232_disk', u'2026-11-01T12:00:00', ATOMSPECTRA),
    (u'AmplitudaSerial', u'synthetic:AmplitudaSerial', u'2026-11-01T11:00:00',
     (u'1024', u'<AmplitudaSerialDeviceConfig><PortName>COM99</PortName></AmplitudaSerialDeviceConfig>')),
    (u'AmplitudaUSB', u'synthetic:AmplitudaUSB', u'2026-11-01T10:00:00', (u'4096', u'<AmplitudaUsbDeviceConfig />')),
    (u'RC-103', u'RC-103.xml', u'2026-10-15T12:00:00', radiacode(u'RC-103')),
    (u'Atom Spectra Nano 16 Pro', u'spectrum:ASN16_Cs137', u'2026-10-15T11:00:00', ATOMSPECTRA),
    (u'Obsidian', u'Obsidian.xml', u'2026-07-01T12:00:00', OBSIDIAN),
    (u'RC-101', u'RC-101.xml', u'2024-10-01T12:00:00', radiacode(u'RC-101')),
    # звуковой вход: карта — несуществующая (номер 99): форма не находит её среди карт машины
    # и поле оставляет пустым, как на снимке гида. Без неё форма брала ПЕРВУЮ карту машины —
    # её имя (железо компьютера) попадало на снимок, и снимок зависел от машины.
    # «Изменить в Windows» выключено — копия громкость системы не трогает.
    (u'Atom Spectra Nano 8 3000 ch', u'ASN8 3000 ch.xml', u'2024-05-01T12:00:00',
     u'<AudioInputDeviceConfig><AudioInputDevice><DeviceId>99</DeviceId><Manufacturer>0</Manufacturer>'
     u'<ProductId>0</ProductId><DriverVersion>0</DriverVersion><Name>Guide audio input</Name>'
     u'<Channels>1</Channels></AudioInputDevice>'
     u'<SamplesPerSecond>44100</SamplesPerSecond><BitsPerSample>16</BitsPerSample>'
     u'<Volume>59</Volume><AutoVolumeSetting>false</AutoVolumeSetting><NegativePolarity>false</NegativePolarity>'
     u'<PulseDetectionMethod>PRA Homage Method</PulseDetectionMethod></AudioInputDeviceConfig>'),
    (u'AtomSpectra Wi-Fi', u'synthetic:AtomSpectraTCP', u'2024-05-01T12:00:00',
     (u'8192', u'<AtomSpectraTcpDeviceConfig><Host>atomspectra.local</Host></AtomSpectraTcpDeviceConfig>')),
]
SYNTHETIC_GUID = u'5e1f0c3a-0000-4000-8000-0000000000%02d'
CRYSTAL = {u'RC-103': u'Cesium iodide', u'RC-101': u'Cesium iodide', u'Atom Spectra 80x80': u'Sodium iodide',
           u'Atom Spectra Nano 16 Pro': u'Cesium iodide'}

# Настройки поиска пиков приборов спектров сцен — те, на которых сняты снимки гида главного окна
# (панель «Обнаружение пиков»: Мин SNR, Допуск; от них зависят метки пиков и состав FSA).
# Заглушки корпуса несут настройки харнесса корпуса (Мин SNR 4), с ними окно было бы другим.
PEAKS = {
    u'spectrum:ASN16_Cs137': (u'10', u'11', u'5', u'2048', u'24', u'3587', u'146', u'1', u'1.5', u'5'),
    u'spectrum:AS80_Th232_disk': (u'10', u'20', u'15', u'1024', u'10', u'1877', u'101', u'1', u'1.2', u'2.6'),
}


def peak_block(snr, tol, lo, concat, fwhm0, chf, wf, kind, left, right):
    return (u'<PeakDetectionMethodConfig>'
            u'<Min_SNR>%s</Min_SNR><FWHM_AT_0>%s</FWHM_AT_0><Ch_Fwhm>%s</Ch_Fwhm><Width_Fwhm>%s</Width_Fwhm>'
            u'<Max_Items>40</Max_Items><Tolerance>%s</Tolerance><Min_Range>%s</Min_Range><Max_Range>2800</Max_Range>'
            u'<Min_FWHM_Tol>10</Min_FWHM_Tol><Max_FWHM_Tol>190</Max_FWHM_Tol><Enabled>true</Enabled>'
            u'<Ch_Concat>%s</Ch_Concat><PeakWidthWidenFactor>1.2</PeakWidthWidenFactor>'
            u'<UseCenterOfMassCentroid>true</UseCenterOfMassCentroid>'
            u'<PeakType>%s</PeakType><ExpGaussExpLeftTail>%s</ExpGaussExpLeftTail>'
            u'<ExpGaussExpRightTail>%s</ExpGaussExpRightTail></PeakDetectionMethodConfig>'
            % (snr, fwhm0, chf, wf, tol, lo, concat, kind, left, right))

# Наборы приборов сцен (имена из GUIDE_DEVICES).
ALL = [d[0] for d in GUIDE_DEVICES if d[0] != u'AtomSpectra Wi-Fi']
SETS = {
    u'all': ALL,
    u'guide': [n for n in ALL if n != u'Atom Spectra Nano 16 Pro'],
    u'guide+tcp': [n for n in ALL if n not in (u'Atom Spectra Nano 16 Pro', u'Atom Spectra Nano 8 3000 ch')]
                  + [u'AtomSpectra Wi-Fi'],
    u'guide-ecal': [u'Atom Spectra 80x80', u'RC-103', u'Obsidian', u'RC-101'],
}

# Сцена -> (спектр раскладки, набор приборов).
SCENES = {
    u'main': (u'ASN16_Cs137', u'all'),
    u'main-fsa': (u'AS80_Th232_disk', u'all'),
    u'main-fsa-nomatrix': (u'AS80_Th232_disk', u'all'),
    u'devcfg': (u'ASN16_Cs137', u'guide'),
    u'devcfg-ecal': (u'ASN16_Cs137', u'guide-ecal'),
    u'devcfg-eff': (u'ASN16_Cs137', u'all'),
    u'devtcp': (u'ASN16_Cs137', u'guide+tcp'),
    u'maker': (u'ASN16_Cs137', u'all'),
    u'gdml': (u'ASN16_Cs137', u'all'),
    u'nucdef': (u'ASN16_Cs137', u'all'),
    u'dialogs': (u'ASN16_Cs137', u'all'),
}

# Придуманная кривая конструктора: геометрия диска с сосудом PE и пробой Soil.
CYLINDER = {
    u'name': u'Цилиндр 40×15',
    u'guid': u'5e1f0c3a-0000-4000-8000-000000000101',
    u'set': ((u'BeakerHeight', u'15'), (u'BeakerSideWallThickness', u'1'), (u'BeakerEndWallThickness', u'1'),
             (u'SourceHeight', u'14')),
    u'wall': (u'Polyethylene', u'0.94', ((1, u'0.1437'), (6, u'0.8563'))),
    u'source': (u'Soil', u'1.5', ((1, u'0.009'), (6, u'0.006'), (8, u'0.5393'), (13, u'0.0635'), (14, u'0.3272'),
                                  (20, u'0.02'), (26, u'0.035'))),
}

ABS_PATH = re.compile(r'(?<![A-Za-z])[A-Za-z]:\\[^<"]*')


def read_text(path):
    with io.open(path, u'r', encoding=u'utf-8-sig', newline=u'') as f:
        return f.read()


def write_text(path, text, bom=False):
    with io.open(path, u'w', encoding=u'utf-8-sig' if bom else u'utf-8', newline=u'') as f:
        f.write(text)


def guid_of(text):
    m = re.search(r'<Guid>([^<]+)</Guid>', text)
    return m.group(1) if m else u''


def first_name(text):
    m = re.search(r'<Name>([^<]*)</Name>', text)
    return m.group(1) if m else u''


def curve_node(text):
    u"""Границы узла кривой спектра `<Efficiency><Guid>…</Efficiency>`. Внутри него точки
    кривой тоже зовутся `<Efficiency>`, поэтому конец ищется счётом вложенности."""
    m = re.search(r'<Efficiency>\s*<Guid>', text)
    if not m:
        sys.exit(u'в спектре нет кривой')
    depth = 0
    for t in re.finditer(r'<(/?)Efficiency(\s*/)?>', text[m.start():]):
        if t.group(2):
            continue
        depth += -1 if t.group(1) else 1
        if depth == 0:
            return m.start(), m.start() + t.end()
    sys.exit(u'узел кривой не закрыт')


def spectrum_device_guid(repo, key):
    return re.search(r'<DeviceConfigReference>.*?<Guid>([^<]+)</Guid>',
                     read_text(os.path.join(repo, SPECTRA[key][0])), re.S).group(1)


def material(tag, name, density, fractions):
    els = u''.join(u'<Element Z="%d" Fraction="%s" />' % (z, f) for z, f in fractions)
    return u'<%s><Name>%s</Name><Density>%s</Density><Fractions>%s</Fractions></%s>' % (tag, name, density, els, tag)


def disk_curve_nodes(repo):
    u"""Кривые прибора диска: узел корпуса `AS80_th_disk` и придуманная кривая конструктора."""
    donor = read_text(os.path.join(repo, DISK_CURVE_FROM))
    d0, d1 = curve_node(donor)
    node = donor[d0:d1]
    disk = u'<EfficiencyConfigData>' + node[len(u'<Efficiency>'):-len(u'</Efficiency>')] + u'</EfficiencyConfigData>'
    geometry = re.search(r'<Geometry>.*?</Geometry>', node, re.S).group(0)
    geometry = re.sub(r'<Name>[^<]*</Name>', u'<Name>' + CYLINDER[u'name'] + u'</Name>', geometry, count=1)
    for tag, value in CYLINDER[u'set']:
        geometry, n = re.subn(r'<%s>[^<]*</%s>' % (tag, tag), u'<%s>%s</%s>' % (tag, value, tag), geometry)
        assert n == 1, tag
    geometry = re.sub(r'<BeakerWall>.*?</BeakerWall>', material(u'BeakerWall', *CYLINDER[u'wall']), geometry, flags=re.S)
    geometry = re.sub(r'<Source>.*?</Source>', material(u'Source', *CYLINDER[u'source']), geometry, flags=re.S)
    cylinder = (u'<EfficiencyConfigData><Guid>%s</Guid><Name>%s</Name><LastUpdated>2026-11-01T12:00:00</LastUpdated>'
                u'<Origin>Simulation</Origin><Curve />%s<Note /></EfficiencyConfigData>'
                % (CYLINDER[u'guid'], CYLINDER[u'name'], geometry))
    return u'<EfficiencyConfigs>\r\n    ' + disk + u'\r\n    ' + cylinder + u'\r\n  </EfficiencyConfigs>'


def synthetic_device(name, kind, updated, index, channels, body):
    lines = [u'<?xml version="1.0"?>',
             u'<DeviceConfigInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema" '
             u'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">',
             u'  <FormatVersion>120920</FormatVersion>',
             u'  <Guid>%s</Guid>' % (SYNTHETIC_GUID % index),
             u'  <Name>%s</Name>' % name,
             u'  <LastUpdated>%s</LastUpdated>' % updated,
             u'  <DefaultMeasurementTime>360000000</DefaultMeasurementTime>',
             u'  <ChannelPitch>1</ChannelPitch>',
             u'  <NumberOfChannels>%s</NumberOfChannels>' % channels,
             u'  <DeviceType>%s</DeviceType>' % kind,
             u'  <ThermometerType>None</ThermometerType>',
             u'  <EnergyCalibrationType>Polynomial</EnergyCalibrationType>',
             u'  <Note />',
             u'  ' + body,
             u'  <BackgroundSpectrumPathname />',
             u'</DeviceConfigInfo>', u'']
    return u'\r\n'.join(lines)


def corpus_device(repo, source):
    root = os.path.join(repo, CORPUS_DEVICES)
    if source.startswith(u'spectrum:'):
        guid = spectrum_device_guid(repo, source[9:])
        for name in sorted(os.listdir(root)):
            if name.lower().endswith(u'.xml') and guid_of(read_text(os.path.join(root, name))) == guid:
                return read_text(os.path.join(root, name))
        sys.exit(u'в заглушках корпуса нет прибора спектра ' + source[9:])
    return read_text(os.path.join(root, source))


def build_devices(repo, dev):
    u"""Все приборы конфига гида в `dev`; имя прибора по Guid — для ссылок спектров."""
    names = {}
    disk_guid = spectrum_device_guid(repo, u'AS80_Th232_disk')
    for index, (name, source, updated, link) in enumerate(GUIDE_DEVICES, 1):
        if source.startswith(u'synthetic:'):
            channels, body = link
            text = synthetic_device(name, source[10:], updated, index, channels, body)
        else:
            text = corpus_device(repo, source)
            text = re.sub(r'<Name>[^<]*</Name>', u'<Name>' + name + u'</Name>', text, count=1)
            text = re.sub(r'<LastUpdated>[^<]*</LastUpdated>', u'<LastUpdated>' + updated + u'</LastUpdated>', text, count=1)
            if link:
                text = text.replace(u'  <PolynomialEnergyCalibration>', u'  ' + link + u'\r\n  <PolynomialEnergyCalibration>', 1)
                if link not in text:
                    sys.exit(u'заглушка корпуса без калибровки — некуда вставить связь: ' + name)
            # вещество кристалла модели (вкладка «Основное»); у заглушек корпуса его нет
            crystal = CRYSTAL.get(name)
            if crystal and u'<CrystalMaterialName>' not in text:
                text = text.replace(u'</DeviceConfigInfo>', u'  <CrystalMaterialName>%s</CrystalMaterialName>\r\n</DeviceConfigInfo>' % crystal, 1)
            if source in PEAKS:
                text, n = re.subn(r'<PeakDetectionMethodConfig>.*?</PeakDetectionMethodConfig>',
                                  peak_block(*PEAKS[source]), text, count=1, flags=re.S)
                if n != 1:
                    sys.exit(u'у заглушки корпуса нет настроек поиска пиков: ' + name)
            if guid_of(text) == disk_guid:
                text = text.replace(u'</DeviceConfigInfo>', u'  ' + disk_curve_nodes(repo) + u'\r\n</DeviceConfigInfo>', 1)
        if ABS_PATH.search(text):
            sys.exit(u'в приборе гида абсолютный путь: ' + name)
        write_text(os.path.join(dev, name + u'.xml'), text, bom=True)
        names[guid_of(text)] = name
    return names


def copy_tree(src, dst, skip=()):
    if os.path.isdir(dst):
        shutil.rmtree(dst)
    shutil.copytree(src, dst, ignore=shutil.ignore_patterns(*skip) if skip else None)


def cmd_base(a):
    work, repo = a.work, a.repo
    # Сборка — без её поставочного config и без страниц справки: конфиг копии свой.
    # Проба съёмки (GuideShot.exe) кладётся сюда же сценарием после этого шага.
    copy_tree(a.bin, os.path.join(work, u'app'), skip=(u'config', u'docs', u'*.pdb', u'*.log'))

    cfg = os.path.join(work, u'base_config')
    if os.path.isdir(cfg):
        shutil.rmtree(cfg)
    os.makedirs(os.path.join(cfg, u'device', u'response'))
    os.makedirs(os.path.join(cfg, u'layout'))
    shutil.copy2(os.path.join(GUIDE_CONFIG, u'BecquerelMonitor.xml'), os.path.join(cfg, u'BecquerelMonitor.xml'))
    shutil.copy2(os.path.join(repo, NUCLIDES), os.path.join(cfg, u'NuclideDefinition.xml'))
    names = build_devices(repo, os.path.join(cfg, u'device'))
    for n in sorted(names.values()):
        print(u'прибор: ' + n)

    spectra = os.path.join(work, u'spectra')
    if os.path.isdir(spectra):
        shutil.rmtree(spectra)
    os.makedirs(spectra)
    for key, (rel, _) in sorted(SPECTRA.items()):
        text = read_text(os.path.join(repo, rel))

        def device_ref(m):
            guid = guid_of(m.group(0))
            if guid not in names:
                sys.exit(u'спектр %s ссылается на прибор, которого нет в конфиге гида' % key)
            return re.sub(r'<Name>[^<]*</Name>', u'<Name>' + names[guid] + u'</Name>', m.group(0))

        text = re.sub(r'<DeviceConfigReference>.*?</DeviceConfigReference>', device_ref, text, flags=re.S)
        if key == u'AS80_Th232_disk' and a.disk_curve == u'corpus':
            donor_text = read_text(os.path.join(repo, DISK_CURVE_FROM))
            d0, d1 = curve_node(donor_text)
            m0, m1 = curve_node(text)
            if guid_of(donor_text[d0:d1]) != guid_of(text[m0:m1]):
                sys.exit(u'узел кривой корпуса не той геометрии')
            text = text[:m0] + donor_text[d0:d1] + text[m1:]
        text = ABS_PATH.sub(u'', text)
        write_text(os.path.join(spectra, key + u'.xml'), text, bom=True)
        m = re.search(r'<Efficiency>\s*<Guid>([^<]+)</Guid>', text)
        if m:
            # ⛔ Склад корпуса в git не входит (данные вне дерева): в чистом worktree его нет,
            # и снимок FSA молча вышел бы «без матрицы». Поэтому нет матрицы — отказ.
            store = a.store or os.path.join(repo, STORE)
            stored = os.path.join(store, m.group(1) + u'.rmx')
            if not os.path.isfile(stored):
                sys.exit(u'нет матрицы кривой %s на складе корпуса %s — укажите --store=<склад основного дерева>'
                         % (key, store))
            shutil.copy2(stored, os.path.join(cfg, u'device', u'response', m.group(1) + u'.rmx'))
            print(u'матрица кривой %s: из склада корпуса' % key)
        print(u'спектр: %s' % key)
    return 0


def set_tag(text, tag, value):
    new, n = re.subn(r'<%s>[^<]*</%s>|<%s\s*/>' % (tag, tag, tag), u'<%s>%s</%s>' % (tag, value, tag), text)
    if n == 0:
        new = text.replace(u'</GlobalConfigInfo>', u'  <%s>%s</%s>\r\n</GlobalConfigInfo>' % (tag, value, tag))
    return new


def cmd_stage(a):
    work = a.work
    if a.scene not in SCENES:
        sys.exit(u'неизвестная сцена: ' + a.scene)
    cfg = os.path.join(work, u'app', u'config')
    copy_tree(os.path.join(work, u'base_config'), cfg)

    main = read_text(os.path.join(cfg, u'BecquerelMonitor.xml'))
    main = set_tag(main, u'Language', u'ru-RU' if a.lang == u'ru' else u'')
    # Окно конфигурации прибора — размера снимков гида (рамка 884x750; у TCP/REST — 884x1025).
    main = set_tag(main, u'DeviceConfigFormHeight', u'1032' if a.scene == u'devtcp' else u'757')
    write_text(os.path.join(cfg, u'BecquerelMonitor.xml'), main)

    key, devices = SCENES[a.scene]
    wanted = set(SETS[devices])
    dev = os.path.join(cfg, u'device')
    for name in os.listdir(dev):
        if name.lower().endswith(u'.xml') and name[:-4] not in wanted:
            os.remove(os.path.join(dev, name))

    # Имя файла фона — на языке снимка.
    spectrum = os.path.join(work, u'spectra', key + u'.xml')
    staged = os.path.join(work, u'spectra', u'%s.%s.xml' % (key, a.lang))
    text = read_text(spectrum)
    text = re.sub(r'<BackgroundSpectrumFile>[^<]*</BackgroundSpectrumFile>',
                  u'<BackgroundSpectrumFile>' + SPECTRA[key][1][a.lang] + u'</BackgroundSpectrumFile>', text)
    write_text(staged, text, bom=True)
    # Документ раскладки — имя файла как у спектра сцены (заголовок окна, «Сохранить в»).
    doc_dir = os.path.join(work, u'spectra', a.lang)
    if not os.path.isdir(doc_dir):
        os.makedirs(doc_dir)
    doc = os.path.join(doc_dir, key + u'.xml')
    shutil.move(staged, doc)
    layout = read_text(os.path.join(HERE, u'layout.xml')).replace(u'{DOC}', doc + u',' + key)
    layout = layout.replace(u'\r\n', u'\n').replace(u'\n', u'\r\n')
    with io.open(os.path.join(cfg, u'layout', u'ExpertMode.xml'), u'w', encoding=u'utf-16', newline=u'') as f:
        f.write(layout)

    if a.scene == u'main-fsa-nomatrix':
        p = os.path.join(dev, u'response', guid_of(text[text.find(u'<Efficiency>'):]) + u'.rmx')
        if os.path.isfile(p):
            os.remove(p)
        if u'<EmbeddedResponseMatrix>' in text:
            sys.exit(u'спектр сцены без матрицы несёт свою матрицу — сцена сняла бы не то')
        print(u'матрица кривой убрана со склада копии')
    print(u'сцена %s, язык %s, спектр %s, приборов %d' % (a.scene, a.lang, key, len(wanted)))
    return 0


def cmd_tokens(a):
    u"""Личные метки НАСТОЯЩЕГО конфига (`%AppData%\\BecqMoni`, только чтение), которых на снимках
    быть не должно: «вид<TAB>метка». В дерево не пишется. Виды: name — имя прибора, serial —
    серийный номер и адрес BLE, port — порт, file — имя файла фона, user — имя профиля,
    curve — название кривой, material — своё вещество (с кириллицей), template — шаблон."""
    root = os.path.join(os.environ[u'APPDATA'], u'BecqMoni', u'config')
    out = set()
    dev = os.path.join(root, u'device')
    models = {d[0] for d in GUIDE_DEVICES}
    if os.path.isdir(dev):
        for name in os.listdir(dev):
            if not name.lower().endswith(u'.xml'):
                continue
            text = read_text(os.path.join(dev, name))
            nm = first_name(text)
            if nm not in models:
                out.add((u'name', nm))
            for tag, kind in ((u'DeviceSerial', u'serial'), (u'AddressBLE', u'serial'), (u'ComPortName', u'port')):
                for v in re.findall(r'<%s>([^<]+)</%s>' % (tag, tag), text):
                    out.add((kind, v.strip()))
            for v in re.findall(r'<BackgroundSpectrumPathname>([^<]+)</BackgroundSpectrumPathname>', text):
                out.add((u'file', os.path.splitext(re.split(r'[\\/]', v)[-1])[0]))
            for v in re.findall(r'<EfficiencyConfigData>\s*<Guid>[^<]*</Guid>\s*<Name>([^<]+)</Name>', text):
                out.add((u'curve', v.strip()))
    for fname, kind in ((u'GeometryMaterials.xml', u'material'), (u'GeometryTemplates.xml', u'template'),
                        (u'SampleTemplates.xml', u'template')):
        p = os.path.join(root, fname)
        if os.path.isfile(p):
            for v in re.findall(r'<Name>([^<]+)</Name>', read_text(p)):
                if kind == u'template' or re.search(u'[Ѐ-ӿ]', v):
                    out.add((kind, v.strip()))
    for key, (rel, _) in SPECTRA.items():
        for v in re.findall(r'<BackgroundSpectrumFile>([^<]+)</BackgroundSpectrumFile>', read_text(os.path.join(a.repo, rel))):
            out.add((u'file', os.path.splitext(v)[0]))
    out.add((u'user', os.path.basename(os.environ.get(u'USERPROFILE', u''))))
    for kind, t in sorted(x for x in out if len(x[1]) >= 3):
        print(u'%s\t%s' % (kind, t))
    return 0


def main():
    try:
        sys.stdout.reconfigure(encoding=u'utf-8')
        sys.stderr.reconfigure(encoding=u'utf-8')  # отказ `sys.exit(<текст>)` идёт сюда
    except AttributeError:
        pass
    p = argparse.ArgumentParser()
    p.add_argument(u'cmd', choices=[u'base', u'stage', u'tokens'])
    p.add_argument(u'--work', default=u'')
    p.add_argument(u'--bin', default=u'')
    p.add_argument(u'--repo', default=os.path.normpath(os.path.join(HERE, u'..', u'..')))
    p.add_argument(u'--scene', default=u'main')
    p.add_argument(u'--lang', default=u'ru', choices=[u'ru', u'en'])
    p.add_argument(u'--disk-curve', default=u'corpus', choices=[u'file', u'corpus'])
    p.add_argument(u'--store', default=u'')
    a = p.parse_args()
    return {u'base': cmd_base, u'stage': cmd_stage, u'tokens': cmd_tokens}[a.cmd](a)


if __name__ == u'__main__':
    sys.exit(main())
