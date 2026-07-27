# -*- coding: utf-8 -*-
"""Собрать корпус: рабочие копии спектров в tools/LibraryFitLab/corpus.

Что делается с каждым исходным файлом (оригинал не трогается):

1. Вынимается ОДИН ResultData (в сборных «калибровках» их несколько) и
   выбрасывается PulseCollection — у поверочных источников «КИ от Жени» это
   base64-список импульсов на 20-50 МБ, для поиска пиков бесполезный.
2. Если фона в файле нет, а рядом (та же папка или выше, но не выше папки
   первого уровня библиотеки) лежит подходящий — он вкладывается в копию как
   BackgroundEnergySpectrum.
3. Энергетическая калибровка проверяется и при нужде перефитывается
   (corpus_calib). Список опорных линий строится по СОДЕРЖИМОМУ образца, а не
   только по цепочкам: у спектра Cs-137 или Co-60 нет ни одной ториевой линии.
   Хранившаяся калибровка остаётся, если поправка её заметно не улучшила.
4. FWHM-калибровка считается по модели разрешения детектора, одной на группу
   det, — разрешение принадлежит кристаллу, а не образцу.
5. Форма пика приводится к гауссиане (PeakType = 0), как у исходной девятки:
   иначе сравнение детекторов смешается с разными хвостами ExpGaussExp.

Девять спектров исходного исследования копируются БАЙТ-В-БАЙТ из
scripts/spectra: их калибровки — те, на которых посчитаны все числа отчёта,
и пересчитывать их заново значит потерять воспроизводимость.

Запуск:  python build_corpus.py [--only=KEY,KEY]
"""
import csv
import json
import os
import re
import shutil
import sys
import xml.etree.ElementTree as ET

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)
CORPUS = os.path.join(LAB, 'corpus')
OUT_SPECTRA = os.path.join(CORPUS, 'spectra')
OUT_DEVICES = os.path.join(CORPUS, 'devices')
RAW = os.path.join(HERE, '_corpus_raw')
LEGACY_DIR = os.path.join(HERE, 'spectra')

sys.path.insert(0, HERE)

import calibrate                                     # noqa: E402
import corpus_calib                                  # noqa: E402
import corpus_def                                    # noqa: E402
from corpus_paths import resolve                     # noqa: E402
from spectrum import Spectrum                        # noqa: E402
from chains import chain_lines, CHAINS               # noqa: E402
import sqlite3                                       # noqa: E402

# ---------------------------------------------------------------------------
# Конфигурации устройств: одна на группу det.
#
# Для трёх детекторов исходной девятки берётся ТА ЖЕ конфигурация, с которой
# считался отчёт (побайтная копия), иначе Min_Range/Max_Range/Tolerance/
# Max_Items поедут и числа перестанут совпадать. Остальные группы получают
# конфигурацию, написанную здесь: у части исходных файлов ссылки на устройство
# нет вовсе, у части GUID указывает на конфиг, которого в дереве нет — именно
# из-за этого прогон «по всем спектрам трёх каталогов» терял 30 файлов из 121
# на «Device config not found».
# ---------------------------------------------------------------------------
APPDATA_DEV = os.path.join(os.environ.get('APPDATA', ''), 'BecqMoni', 'config', 'device')
ASN8_DEV = r'C:\Users\moroz\YandexDisk\Спектры\ASN8_Configs'

COPY_DEVICE = {
    'ASN16': (APPDATA_DEV, '1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml'),
    'AS80x80': (APPDATA_DEV, 'Atom Spectra 80x80.xml'),
    'RC103': (APPDATA_DEV, 'RC-103.xml'),
    'RC101': (APPDATA_DEV, 'RC-101.xml'),
    'OBS': (APPDATA_DEV, 'Obsidian.xml'),
    'ASN8_1024': (ASN8_DEV, 'ASN8 1024 ch.xml'),
    'ASN8_2048': (ASN8_DEV, 'ASN8 2048 ch.xml'),
    'ASN8_3000': (ASN8_DEV, 'ASN8 3000 ch.xml'),
    'ASN8_4096': (ASN8_DEV, 'ASN8 4096 ch.xml'),
    'ASN8_8192': (ASN8_DEV, 'ASN8 8192 ch.xml'),
}

# Группы, для которых конфигурация пишется здесь.
# guid — тот, что уже стоит в файлах группы (если он один и осмысленный),
# иначе выдуманный детерминированный: рабочие копии всё равно переклеиваются
# на него, а исходные файлы не меняются.
NEW_DEVICE = {
    'HPGE':    dict(guid='9e5a1c00-0001-4a00-9000-11c0de000001', name='HPGe 16384 (corpus)',
                    channels=16384, dtype='AudioInput', lo=20.0, hi=2700.0),
    'LaBr3':   dict(guid='9e5a1c00-0002-4a00-9000-11c0de000002', name='LaBr3 8192 (corpus)',
                    channels=8192, dtype='AtomSpectraVCP', lo=20.0, hi=2800.0),
    'CZT':     dict(guid='9e5a1c00-0003-4a00-9000-11c0de000003', name='CZT 5x5x5 4096 (corpus)',
                    channels=4096, dtype='AudioInput', lo=30.0, hi=2800.0),
    'SrI2':    dict(guid='379b5443-f07f-4ddb-afcc-59d95222d295', name='SrI2 8192 (corpus)',
                    channels=8192, dtype='AtomSpectraVCP', lo=20.0, hi=2800.0),
    'GS4000':  dict(guid='27cc6fd0-ec1d-457e-baf2-33fe07ff3064', name='Gamma-Spectra 4000 (corpus)',
                    channels=4000, dtype='AudioInput', lo=20.0, hi=2800.0),
    'ASN3':    dict(guid='9e5a1c00-0004-4a00-9000-11c0de000004', name='Atom Spectra Nano 3 2500 (corpus)',
                    channels=2500, dtype='AtomSpectraVCP', lo=20.0, hi=2800.0),
    'AS1PRO':  dict(guid='9e5a1c00-0006-4a00-9000-11c0de000006', name='Atom Spectra 1 Pro 8192 (corpus)',
                    channels=8192, dtype='AtomSpectraVCP', lo=20.0, hi=2800.0),
    'RC103g':  dict(guid='17280748-6c91-49e6-a001-3c074db3750e', name='RC-103g 1024 (corpus)',
                    channels=1024, dtype='RadiaCode', lo=15.0, hi=2800.0),
    # --- из spectravibe-toolkit, аттестованные источники ---
    'G1S':     dict(guid='9e5a1c00-0007-4a00-9000-11c0de000007',
                    name='Gamma-1S UDS-GC 63x63 1024 (corpus)',
                    channels=1024, dtype='AudioInput', lo=30.0, hi=2800.0),
    'HPGE_GMX': dict(guid='9e5a1c00-0008-4a00-9000-11c0de000008',
                     name='HPGe GMX 8192 (corpus)',
                     channels=8192, dtype='AudioInput', lo=20.0, hi=2800.0),
    'HPGE_GEM': dict(guid='9e5a1c00-0009-4a00-9000-11c0de000009',
                     name='HPGe GEM20P4 8192 (corpus)',
                     channels=8192, dtype='AudioInput', lo=20.0, hi=2800.0),
    'LABR_BRIL': dict(guid='9e5a1c00-000a-4a00-9000-11c0de00000a',
                      name='LaBr3 BrilLanCe380 1024 (corpus)',
                      channels=1024, dtype='AudioInput', lo=30.0, hi=2800.0),
    'CZT_TECD': dict(guid='9e5a1c00-000b-4a00-9000-11c0de00000b',
                     name='CZT Te(Cd) 4095 (corpus)',
                     channels=4095, dtype='AudioInput', lo=30.0, hi=2800.0),
}

def device_guid(det):
    if det in NEW_DEVICE:
        return NEW_DEVICE[det]['guid']
    src = os.path.join(*COPY_DEVICE[det])
    return ET.parse(src).getroot().findtext('Guid')


# ---------------------------------------------------------------------------
# Линии: что образец в принципе может излучать
# ---------------------------------------------------------------------------
AMBIENT = [(1460.82, 3.0, 'K-40'), (511.0, 2.0, 'annih')]
W_XRAY = [(57.98, 30.0, 'W Ka2'), (59.32, 52.0, 'W Ka1'), (67.24, 18.0, 'W Kb')]
_NUC_CACHE = {}
_ROOM_CACHE = []

# Доля, с которой природные ряды комнаты подмешиваются к образцу. Нужна не для
# физики, а для калибровки: у спектра Cs-137 или Am-241 своих линий одна-две, и
# полином по ним не построить, зато 1461 (K-40), 2614 (Tl-208) и 609 (Bi-214)
# видны в любом достаточно длинном измерении в помещении. measure() всё равно
# берёт только те линии, которые реально нашлись с sig >= 4, так что для
# спектра, где комнаты не видно, добавка ничего не меняет.
ROOM_SCALE = 0.05


def nuclide_lines(nucid):
    """Гамма-линии одиночного нуклида из nucdb — тем же запросом, что chains.py."""
    if nucid in _NUC_CACHE:
        return _NUC_CACHE[nucid]
    c = sqlite3.connect(chains_db())
    rows = c.execute(
        "select energy_num, intensity_num from decay_radiations "
        "where parent_nucid = ? and type_a = 'G' and energy_num not null "
        "and intensity_num not null and intensity_num > 0.5 "
        "and parent_l_seqno = (select min(parent_l_seqno) from decay_radiations y "
        "                      where y.parent_nucid = ?)", (nucid, nucid)).fetchall()
    c.close()
    from chains import pretty
    out = [(float(e), float(i), pretty(nucid)) for e, i in rows]
    # 44Ti в равновесии со своим 44Sc: линия 1157 приходит от дочернего
    if nucid == '44TI':
        out.extend((float(e), float(i), 'Sc-44') for e, i in
                   [(1157.02, 99.9)])
    _NUC_CACHE[nucid] = out
    return out


def chains_db():
    import chains
    return chains.DB


def room_lines():
    """Сильные линии природных рядов — то, что даёт помещение любому спектру."""
    if not _ROOM_CACHE:
        for chain in ('232TH', '226RA'):
            for r in chain_lines(chain):
                if r['i_chain'] >= 3.0:
                    _ROOM_CACHE.append((r['energy'], r['i_chain'] * ROOM_SCALE,
                                        r['name'] + ' room'))
    return _ROOM_CACHE


def sample_lines(entry):
    """Всё, что образец может излучить, с интенсивностью на распад родителя.

    Заменяет одноимённую функцию calibrate.py: там источником могли быть только
    цепочки, а в корпусе половина спектров — одиночные нуклиды (Cs-137, Am-241,
    Co-60, Lu-176, I-131), у которых ни одной цепочечной линии нет.
    """
    rows = []
    for ch in entry.get('chains') or []:
        if ch == 'U-238u':
            for r in chain_lines('238U'):
                if r['nucid'] in ('238U', '234TH', '234PAm1', '234PA', '234U'):
                    rows.append((r['energy'], r['i_chain'], r['name']))
            continue
        for r in chain_lines(CHAINS[ch]):
            rows.append((r['energy'], r['i_chain'], r['name']))
    for nucid in entry.get('nuclides') or []:
        rows.extend(nuclide_lines(nucid))
    for e, i, name in room_lines():
        if not any(abs(e - x) < 0.6 for x, _, _ in rows):
            rows.append((e, i, name))
    rows.extend(AMBIENT)
    if entry.get('extra') == 'WT':
        rows.extend(W_XRAY)
    rows.sort()
    return rows


def wanted_lines(entry):
    """Кандидаты в опорные линии: сильные линии содержимого плюс классический
    список calibrate.WANTED (он нужен там, где сильные линии сливаются)."""
    rows = sample_lines(entry)
    want = set(e for e, i, _ in rows if i >= 3.0)
    want.update(e for e in calibrate.WANTED
                if any(abs(e - x) < 0.6 for x, _, _ in rows))
    want.update(e for e, _, _ in AMBIENT
                if any(abs(e - x) < 0.6 for x, _, _ in rows))
    return sorted(want)


# Фон помещения: цепочки и калий есть всегда, этого хватает на калибровку
BG_ENTRY = dict(chains=['Th-232', 'Ra-226', 'U-238'], nuclides=['40K'])


# ---------------------------------------------------------------------------
# Стадия 1: вынуть ResultData, выбросить импульсы, подшить фон
# ---------------------------------------------------------------------------
def strip_pulses(rd):
    for pc in rd.findall('PulseCollection'):
        pulses = pc.find('Pulses')
        if pulses is not None and (pulses.text or ''):
            pulses.text = None


def ensure_livetime(es):
    lt = es.find('LiveTime')
    mt = es.findtext('MeasurementTime')
    if (lt is None or not (lt.text or '').strip()) and mt:
        if lt is None:
            lt = ET.SubElement(es, 'LiveTime')
        lt.text = mt


def spectrum_sum(es):
    return sum(int(d.text or 0) for d in es.findall('Spectrum/DataPoint'))


def extract(entry):
    """Единственный ResultData в отдельном файле, без импульсов, с фоном."""
    src = resolve(entry['path'])
    tree = ET.parse(src)
    root = tree.getroot()
    rds = root.findall('ResultDataList/ResultData')
    rd = rds[entry.get('idx', 0)]
    lst = root.find('ResultDataList')
    for other in list(lst):
        if other is not rd:
            lst.remove(other)

    strip_pulses(rd)
    es = rd.find('EnergySpectrum')
    ensure_livetime(es)

    bg_source = None
    bg = rd.find('BackgroundEnergySpectrum')
    if bg is not None and spectrum_sum(bg) > 0:
        bg_source = 'встроен'
        ensure_livetime(bg)
    else:
        if bg is not None:
            rd.remove(bg)
            bg = None
        if entry.get('bg'):
            bgsrc = resolve(entry['bg'])
            brd = ET.parse(bgsrc).getroot().find('ResultDataList/ResultData')
            bes = brd.find('EnergySpectrum')
            ensure_livetime(bes)
            bes.tag = 'BackgroundEnergySpectrum'
            # порядок элементов внутри ResultData не важен для XmlSerializer
            # только пока каждый встречается один раз; вставляем сразу за
            # передним планом, как в файлах, где фон писало приложение
            idx = list(rd).index(es) + 1
            rd.insert(idx, bes)
            bg = bes
            bg_source = os.path.basename(bgsrc)
            ref = rd.find('BackgroundSpectrumFile')
            if ref is None:
                ref = ET.SubElement(rd, 'BackgroundSpectrumFile')
            ref.text = os.path.basename(bgsrc)

    # У части файлов ссылки на устройство нет вовсе (<DeviceConfigReference />
    # у HPGe и CZT — их привёз импорт из N42), у части GUID указывает на конфиг,
    # которого в дереве нет. Рабочая копия всегда ссылается на конфигурацию
    # своей группы из corpus/devices.
    ref = rd.find('DeviceConfigReference')
    if ref is None:
        ref = ET.SubElement(rd, 'DeviceConfigReference')
    name_el = ref.find('Name')
    if name_el is None:
        name_el = ET.SubElement(ref, 'Name')
    guid_el = ref.find('Guid')
    if guid_el is None:
        guid_el = ET.SubElement(ref, 'Guid')
    old_guid = (guid_el.text or '').strip()
    guid_el.text = device_guid(entry['det'])
    if entry['det'] in NEW_DEVICE:
        name_el.text = NEW_DEVICE[entry['det']]['name']

    os.makedirs(RAW, exist_ok=True)
    dest = os.path.join(RAW, entry['key'] + '.xml')
    tree.write(dest, encoding='utf-8', xml_declaration=True)

    # фон отдельным файлом — калибровать его нужно тем же кодом, а Spectrum
    # умеет читать только передний план
    bg_path = None
    if bg is not None:
        btree = ET.ElementTree(ET.fromstring(ET.tostring(root, encoding='unicode')))
        brd = btree.getroot().find('ResultDataList/ResultData')
        bfg = brd.find('EnergySpectrum')
        bbg = brd.find('BackgroundEnergySpectrum')
        brd.remove(bfg)
        bbg.tag = 'EnergySpectrum'
        bg_path = os.path.join(RAW, entry['key'] + '_bg.xml')
        btree.write(bg_path, encoding='utf-8', xml_declaration=True)

    return dest, bg_path, bg_source, old_guid


# ---------------------------------------------------------------------------
# Стадия 2: энергетическая калибровка
# ---------------------------------------------------------------------------
def calibrate_one(sp, entry, res_a_hint=None):
    """Калибровка одного спектра: см. corpus_calib.calibrate."""
    ent = dict(entry)
    ent['wanted'] = wanted_lines(entry)
    calibrate.sample_lines = sample_lines          # curate() берёт её из модуля

    # Курирование линий (какие вообще годятся в опорные при данном разрешении)
    # оставлено за calibrate.curate: там уже учтено, что близкие линии
    # сливаются в одну и калиброваться надо на центроид группы.
    stored = corpus_calib.Ecal(sp.ecal, sp.n)

    def res_of(a):
        return lambda e: a * np.sqrt(max(float(e), 5.0))

    # Разрешение: либо померенное по самому спектру (первый проход), либо
    # взятое из модели своей группы (второй проход). Модель группы надёжнее —
    # разрешение принадлежит кристаллу, а не образцу, и строится она по всем
    # спектрам детектора сразу.
    if res_a_hint:
        res_a, r662 = res_a_hint, res_a_hint / np.sqrt(662.0)
    else:
        res_a, r662 = corpus_calib.measure_resolution(sp.counts, stored, ent['wanted'])

    # Порог чистоты низкий намеренно. curate возвращает не табличную энергию, а
    # интенсивностно-взвешенный центроид группы — то самое, куда детектор
    # действительно ставит пик, — так что бленд калибровке не мешает. А вот
    # порог 0.6 на 1024-канальных приборах и на фонах оставлял два-три
    # кандидата, из которых не находилось ни одного, и калибровка оставалась
    # непроверенной (у фона Obsidian — с промахом 41 кэВ на 1120 кэВ).
    lines = calibrate.curate(ent, res_of(res_a), min_purity=0.45)
    if not lines:
        return stored, [], r662, 'stored/нет линий'

    cal, pairs, res_a, tag = corpus_calib.calibrate(sp.counts, sp.ecal, lines, res_a)
    return cal, pairs, res_a / np.sqrt(662.0), tag


def collect_points(state, det, min_purity):
    pts = []
    for st in state.values():
        if st['det'] != det:
            continue
        for a in st['accepted']:
            if a.get('purity', 1.0) < min_purity:
                continue
            pts.append((a['e_ref'], a['fwhm'] * abs(st['ecal'].dEdch(a['ch'])),
                        min(a['sig'], 100.0) * a.get('purity', 1.0)))
    return pts


def resolution_points(state, det):
    """Точки (E, FWHM, вес) группы, очищенные от выбросов.

    Одна плохо севшая линия портит модель на всю группу: у AS80x80 первый прогон
    дал 46 % на 60 кэВ — квадратичная по E модель с большим c0, вытянутая парой
    завышенных ширин. Отбрасываем то, что уходит от медианы приведённой ширины
    FWHM/sqrt(E) больше чем в полтора раза.
    """
    pts = collect_points(state, det, 0.85)
    if len(pts) < 3:
        pts = collect_points(state, det, 0.0)
    if len(pts) < 3:
        return pts
    red = np.array([f / np.sqrt(max(e, 1.0)) for e, f, _ in pts])
    med = float(np.median(red))
    keep = [p for p, r in zip(pts, red) if 0.6 * med <= r <= 1.7 * med]
    return keep if len(keep) >= 3 else pts


# ---------------------------------------------------------------------------
# Стадия 3: запись
# ---------------------------------------------------------------------------
def apply_ecal(es, coef):
    cal = es.find('EnergyCalibration')
    if cal is None:
        return
    order = cal.find('PolynomialOrder')
    if order is not None:
        order.text = str(len(coef) - 1)
    coefs = cal.find('Coefficients')
    for child in list(coefs):
        coefs.remove(child)
    for c in coef:
        ET.SubElement(coefs, 'Coefficient').text = repr(float(c))


# Умолчания FWHMPeakDetectionMethodConfig — ровно те, при которых считался
# отчёт. Спектр своей конфигурации нести не может: ResultData.
# PeakDetectionMethodConfig помечен [XmlIgnore], в файл не пишется и из файла не
# читается, а инициализирован новым объектом. Из-за этого проверка
# `peakConfig != null` в LibraryFitLab.LoadResultData всегда была истинной, и
# конфигурация устройства до поиска пиков не доходила (исправлено в Program.cs).
# Поэтому трём детекторам девятки конфигурация пишется С ЭТИМИ ЖЕ значениями:
# после исправления их поведение обязано остаться прежним.
PEAK_CONFIG_DEFAULTS = dict(
    Min_SNR=4, FWHM_AT_0=15, Ch_Fwhm=3756, Width_Fwhm=103, Max_Items=40,
    Tolerance=10, Min_Range=30, Max_Range=2800, Min_FWHM_Tol=1,
    Max_FWHM_Tol=199, Enabled='true', Ch_Concat=1024, PeakType=0,
    ExpGaussExpLeftTail=1, ExpGaussExpRightTail=1)

# Три детектора исходного исследования: им ничего не пересчитываем, чтобы после
# исправления LoadResultData числа отчёта остались прежними.
LEGACY_DETS = {'ASN16', 'AS80x80', 'RC103'}

PEAK_CONFIG_ORDER = ['Min_SNR', 'FWHM_AT_0', 'Ch_Fwhm', 'Width_Fwhm', 'Max_Items',
                     'Tolerance', 'Min_Range', 'Max_Range', 'Min_FWHM_Tol',
                     'Max_FWHM_Tol', 'Enabled', 'Ch_Concat', 'PeakType',
                     'ExpGaussExpLeftTail', 'ExpGaussExpRightTail']


def concat_for(fwhm_coef, ecal, nmax):
    """Ch_Concat — до скольки каналов финдер пересыпает спектр перед поиском
    (PeakDetector.PeakFinder: mul = N / Ch_Concat, потом combine_bins(mul)).
    Держим 14-20 ячеек на полуширину — столько же, сколько получается у девятки
    при умолчании 1024."""
    ch = float(np.clip(ecal.channel(662.0), 1.0, nmax - 2))
    fw = float(np.sqrt(max(fwhm_coef[0] + fwhm_coef[1] * ch + fwhm_coef[2] * ch * ch, 1e-6)))
    mul = max(1, int(round(fw / 16.0)))
    return max(64, nmax // mul)


def write_copy(raw_path, dest, fg_coef, bg_coef, fwhm_coef, peak_type=0):
    tree = ET.parse(raw_path)
    rd = tree.getroot().find('ResultDataList/ResultData')
    apply_ecal(rd.find('EnergySpectrum'), fg_coef)
    bg = rd.find('BackgroundEnergySpectrum')
    if bg is not None and bg_coef is not None:
        apply_ecal(bg, bg_coef)
    old = rd.find('SqrtFwhmCalibration')
    if old is not None:
        rd.remove(old)
    fw = ET.SubElement(rd, 'SqrtFwhmCalibration')
    ET.SubElement(fw, 'CalibrationPeaks')
    cs = ET.SubElement(fw, 'Coefficients')
    for c in fwhm_coef:
        ET.SubElement(cs, 'Coefficient').text = repr(float(c))
    ET.SubElement(fw, 'PeakType').text = str(peak_type)
    ET.SubElement(fw, 'ExpGaussExpLeftTail').text = '1.0'
    ET.SubElement(fw, 'ExpGaussExpRightTail').text = '1.0'
    ET.SubElement(fw, 'Chi2pNdp').text = '-1'
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    tree.write(dest, encoding='utf-8', xml_declaration=True)


DEVICE_TEMPLATE = """<?xml version="1.0"?>
<DeviceConfigInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <FormatVersion>120920</FormatVersion>
  <Guid>{guid}</Guid>
  <Name>{name}</Name>
  <LastUpdated>2026-07-26T00:00:00.0000000+03:00</LastUpdated>
  <DefaultMeasurementTime>360000000</DefaultMeasurementTime>
  <ChannelPitch>1</ChannelPitch>
  <NumberOfChannels>{channels}</NumberOfChannels>
  <DeviceType>{dtype}</DeviceType>
  <ThermometerType>None</ThermometerType>
  <EnergyCalibrationType>Polynomial</EnergyCalibrationType>
  <!-- Конфигурация корпуса LibraryFitLab. Реального прибора за ней нет: она
       нужна, чтобы харнесс нашёл устройство спектра и взял отсюда
       Min_Range/Max_Range/Ch_Concat/Tolerance/Max_Items. Калибровки энергии и
       FWHM лежат в самих спектрах, здесь они только заглушка. -->
  <Note />
  <PolynomialEnergyCalibration>
    <PolynomialOrder>2</PolynomialOrder>
    <Coefficients>
      <Coefficient>{c0!r}</Coefficient>
      <Coefficient>{c1!r}</Coefficient>
      <Coefficient>{c2!r}</Coefficient>
    </Coefficients>
  </PolynomialEnergyCalibration>
  <StabilizerConfig>
    <TargetPeaks />
  </StabilizerConfig>
  <DoseRateConfig>
    <DoseRateCalibrationPoints />
  </DoseRateConfig>
  <PeakDetectionMethodConfig>
    <Min_SNR>4</Min_SNR>
    <FWHM_AT_0>{fwhm0}</FWHM_AT_0>
    <Ch_Fwhm>{ch_fwhm}</Ch_Fwhm>
    <Width_Fwhm>{width_fwhm}</Width_Fwhm>
    <Max_Items>{max_items}</Max_Items>
    <Tolerance>{tolerance}</Tolerance>
    <Min_Range>{lo!r}</Min_Range>
    <Max_Range>{hi!r}</Max_Range>
    <Min_FWHM_Tol>{fwhm_tol_min}</Min_FWHM_Tol>
    <Max_FWHM_Tol>{fwhm_tol_max}</Max_FWHM_Tol>
    <Enabled>true</Enabled>
    <Ch_Concat>{concat}</Ch_Concat>
    <PeakType>0</PeakType>
    <ExpGaussExpLeftTail>1</ExpGaussExpLeftTail>
    <ExpGaussExpRightTail>1</ExpGaussExpRightTail>
  </PeakDetectionMethodConfig>
  <BackgroundSpectrumPathname />
</DeviceConfigInfo>
"""


def write_devices(state):
    """Конфигурация устройства на каждую группу — единственное место, откуда
    поиск пиков берёт Min_Range/Max_Range/Ch_Concat/Tolerance/Max_Items.

    Трём детекторам девятки пишутся ровно те значения, при которых считался
    отчёт (умолчания класса, см. PEAK_CONFIG_DEFAULTS): их поведение после
    исправления LoadResultData обязано остаться прежним. Новым группам —
    посчитанные: Ch_Concat под их разрешение, диапазон под их шкалу.
    """
    os.makedirs(OUT_DEVICES, exist_ok=True)
    written = []
    for det in sorted({st['det'] for st in state.values()}):
        ref = max((st for st in state.values() if st['det'] == det),
                  key=lambda st: len(st['accepted']), default=None)
        if ref is None or 'peak_config' not in ref:
            continue
        cfg = ref['peak_config']
        ecal = ref['ecal']
        n = ref['sp'].n
        if det in NEW_DEVICE:
            guid, name = NEW_DEVICE[det]['guid'], NEW_DEVICE[det]['name']
            dtype = NEW_DEVICE[det]['dtype']
        else:
            src = os.path.join(*COPY_DEVICE[det])
            root = ET.parse(src).getroot()
            guid, name = root.findtext('Guid'), root.findtext('Name')
            dtype = root.findtext('DeviceType') or 'AudioInput'
        fname = re.sub(r'[\/:*?"<>|]', '-', name) + '.xml'
        coef = list(ecal.coef[:3]) + [0.0, 0.0]
        text = DEVICE_TEMPLATE.format(
            guid=guid, name=name, channels=n, dtype=dtype,
            c0=float(coef[0]), c1=float(coef[1]), c2=float(coef[2]),
            fwhm0=cfg['FWHM_AT_0'], ch_fwhm=cfg['Ch_Fwhm'],
            width_fwhm=cfg['Width_Fwhm'], lo=float(cfg['Min_Range']),
            hi=float(cfg['Max_Range']), concat=cfg['Ch_Concat'],
            tolerance=cfg['Tolerance'], max_items=cfg['Max_Items'],
            fwhm_tol_min=cfg['Min_FWHM_Tol'], fwhm_tol_max=cfg['Max_FWHM_Tol'])
        with open(os.path.join(OUT_DEVICES, fname), 'w', encoding='utf-8') as fh:
            fh.write(text)
        written.append((det, fname, 'Ch_Concat=%d  %g..%g кэВ' % (
            cfg['Ch_Concat'], cfg['Min_Range'], cfg['Max_Range'])))
    return written


MANIFEST_COLUMNS = [
    'key', 'det', 'channels', 'live_s', 'counts', 'chains', 'nuclides',
    'background', 'ecal_mode', 'ecal_lines', 'ecal_rms_kev', 'ecal_rms_fwhm',
    'bg_ecal_mode',
    'fwhm_662_pct', 'result_data', 'source', 'why',
]


def legacy_manifest(entries):
    """Строки манифеста для девятки — из data/calibration.json, где лежат те же
    коэффициенты, которыми сделаны её рабочие копии."""
    cal = {r['key']: r for r in json.load(
        open(os.path.join(LAB, 'data', 'calibration.json'), encoding='utf-8'))}
    rows = []
    for e in entries:
        c = cal.get(e['key'], {})
        path = os.path.join(OUT_SPECTRA, e['key'] + '.xml')
        sp = Spectrum(path)
        res = c.get('res_kev')
        fw = (100 * float(np.sqrt(max(res[0] + res[1] * 662.0 + res[2] * 662.0 ** 2, 1e-9))) / 662.0
              if res else None)
        rd = ET.parse(path).getroot().find('ResultDataList/ResultData')
        has_bg = rd.find('BackgroundEnergySpectrum') is not None
        rows.append(dict(
            key=e['key'], det=e['det'], channels=sp.n, live=round(sp.live, 1),
            counts=int(sp.counts.sum()),
            chains=';'.join(e.get('chains') or []), nuclides='',
            background='встроен' if has_bg else 'нет',
            ecal_mode=c.get('mode', '?'), ecal_lines=c.get('n_lines', ''),
            ecal_rms_kev=round(c['rms'], 2) if 'rms' in c else '',
            ecal_rms_fwhm='',
            bg_ecal_mode='как передний план' if has_bg else '-',
            fwhm_662_pct=round(fw, 2) if fw else '',
            source=os.path.relpath(resolve(e['path']), corpus_def.LIB),
            result_data=0, why=e['why']))
    return rows


def write_manifest(rows):
    order = {e['key']: i for i, e in enumerate(corpus_def.ALL)}
    rows.sort(key=lambda r: order.get(r['key'], 999))
    path = os.path.join(CORPUS, 'manifest.csv')
    with open(path, 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(MANIFEST_COLUMNS)
        for r in rows:
            w.writerow([r.get('live' if c == 'live_s' else c, '') for c in MANIFEST_COLUMNS])
    print('манифест: %s (%d строк)' % (path, len(rows)))


def write_detectors(state, res_coef, rows):
    """corpus/detectors.csv — модель разрешения и рабочий диапазон на группу.

    Нужен всему, что строит сеты: ширина линии на энергии определяет и фильтр
    разноса k*FWHM, и куда можно сдвигать линии обманки. Раньше эти модели
    существовали только для трёх детекторов девятки, внутри
    data/calibration.json, и mkconfig.py знал ровно их.
    """
    channels, ranges = {}, {}
    for st in state.values():
        channels.setdefault(st['det'], st['sp'].n)
        cfg = st.get('peak_config') or PEAK_CONFIG_DEFAULTS
        ranges.setdefault(st['det'], (cfg['Min_Range'], cfg['Max_Range']))
    for row in rows:
        channels.setdefault(row['det'], int(row['channels']))
        ranges.setdefault(row['det'], (PEAK_CONFIG_DEFAULTS['Min_Range'],
                                       PEAK_CONFIG_DEFAULTS['Max_Range']))

    path = os.path.join(CORPUS, 'detectors.csv')
    with open(path, 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(['det', 'channels', 'spectra', 'e_lo', 'e_hi',
                    'res_c0', 'res_c1', 'res_c2', 'fwhm_662_pct'])
        for det in sorted(res_coef):
            coef = res_coef[det]
            rf = corpus_calib.resolution_fn(coef)
            lo, hi = ranges.get(det, (30.0, 2800.0))
            w.writerow([det, channels.get(det, ''),
                        sum(1 for r in rows if r['det'] == det), lo, hi,
                        repr(float(coef[0])), repr(float(coef[1])), repr(float(coef[2])),
                        round(100 * rf(662.0) / 662.0, 2)])
    print('детекторы: %s (%d групп)' % (path, len(res_coef)))


# ---------------------------------------------------------------------------
def main():
    only = None
    for a in sys.argv[1:]:
        if a.startswith('--only='):
            only = set(a.split('=', 1)[1].split(','))

    entries = [e for e in corpus_def.NEW + corpus_def.VIBE
               if only is None or e['key'] in only]
    os.makedirs(OUT_SPECTRA, exist_ok=True)

    # --- девятка: побайтная копия ---
    legacy_rows = []
    for e in corpus_def.LEGACY:
        src = os.path.join(LEGACY_DIR, e['key'] + '.xml')
        if not os.path.isfile(src):
            print('НЕТ рабочей копии девятки: %s (запусти apply_calibration.py)' % src)
            continue
        shutil.copyfile(src, os.path.join(OUT_SPECTRA, e['key'] + '.xml'))
        legacy_rows.append(e)
    print('девятка: скопировано %d' % len(legacy_rows))

    # --- стадия 1+2 ---
    state = {}
    for e in entries:
        try:
            raw, bg_raw, bg_source, old_guid = extract(e)
        except Exception as ex:
            print('%-20s ОШИБКА извлечения: %s' % (e['key'], ex))
            continue
        sp = Spectrum(raw)
        ecal, acc, r662, mode = calibrate_one(sp, e)
        st = dict(entry=e, det=e['det'], raw=raw, bg_raw=bg_raw, bg_source=bg_source,
                  old_guid=old_guid, sp=sp, ecal=ecal, accepted=acc, r662=r662, mode=mode)
        # фон
        st['bg_ecal'] = None
        st['bg_mode'] = '-'
        if bg_raw:
            bsp = Spectrum(bg_raw)
            if len(bsp.ecal) == len(sp.ecal) and np.allclose(bsp.ecal, sp.ecal, rtol=0, atol=1e-12):
                st['bg_ecal'] = ecal
                st['bg_mode'] = 'как передний план'
            else:
                becal, bacc, br662, bmode = calibrate_one(bsp, BG_ENTRY)
                st['bg_ecal'] = becal
                st['bg_mode'] = bmode
                st['bg_accepted'] = bacc
                st['bg_sp'] = bsp
        state[e['key']] = st
        print('%-20s %-9s ch=%-6d live=%-9.0f R662=%5.2f%% lines=%2d '
              'rms=%6.2f кэВ = %5.2f FWHM  %s' % (
                  e['key'], e['det'], sp.n, sp.live, 100 * r662, len(acc),
                  calibrate.rms(acc, ecal),
                  corpus_calib.residual_fwhm(ecal, acc, r662 * np.sqrt(662.0)),
                  mode))

    # --- стадия 2а: второй проход с разрешением, взятым из модели группы ---
    # Разрешение принадлежит кристаллу, а не образцу, и модель, построенная по
    # всем спектрам детектора сразу, надёжнее оценки по одному спектру: у
    # негативов (Am-241, K-40, фон) своих линий одна-две, и мерить ширину не на
    # чем. Первый проход нужен только чтобы эту модель было из чего построить.
    for round_no in (1, 2):
        res_a = {}
        for det in sorted({st['det'] for st in state.values()}):
            pts = resolution_points(state, det)
            if len(pts) >= 2:
                res_a[det] = float(np.median([w / np.sqrt(e) for e, w, _ in pts]))
        moved = 0
        for key, st in state.items():
            hint = res_a.get(st['det'])
            if hint is None or abs(np.log(hint / (st['r662'] * np.sqrt(662.0)))) < 0.02:
                continue
            ecal, acc, r662, mode = calibrate_one(st['sp'], st['entry'], res_a_hint=hint)
            if len(acc) < len(st['accepted']):
                continue
            st.update(ecal=ecal, accepted=acc, r662=r662, mode=mode + '/grp')
            if st['bg_mode'] == 'как передний план':
                st['bg_ecal'] = ecal
            moved += 1
        print('  проход %d: пересчитано по модели группы %d' % (round_no, moved))
        if not moved:
            break

    # --- стадия 2б: слабые спектры наследуют калибровку сильного соседа ---
    # Спектр вроде «Домик сутки 8192» (142 k отсчётов за 210 ks на 8192 каналах)
    # своих опорных линий не набирает вовсе. Но если хранившаяся калибровка у
    # него побайтно та же, что у сильного спектра той же группы, значит снимали
    # на одной настройке усиления, и поправка, найденная по сильному, — лучшая
    # оценка, какая есть. Тот же приём, что 'ref-cal' в calibrate.py.
    for key, st in state.items():
        if len(st['accepted']) >= 2:
            continue
        donors = [d for d in state.values()
                  if d['det'] == st['det'] and len(d['accepted']) >= 4
                  and len(d['sp'].ecal) == len(st['sp'].ecal)
                  and np.allclose(d['sp'].ecal, st['sp'].ecal, rtol=0, atol=1e-12)]
        if not donors:
            continue
        donor = max(donors, key=lambda d: len(d['accepted']))
        st['ecal'] = donor['ecal']
        st['r662'] = donor['r662']
        st['mode'] = 'ref-cal(%s)' % donor['entry']['key']
        if st['bg_mode'] == 'как передний план':
            st['bg_ecal'] = donor['ecal']
        print('%-20s наследует калибровку %s' % (key, donor['entry']['key']))

    # --- стадия 3: модель разрешения на группу ---
    # У трёх детекторов исходной девятки модель НЕ пересчитывается: она уже
    # посчитана по их спектрам и записана в data/calibration.json, и числа
    # отчёта держатся на ней. Пересчёт по одним лишь новым спектрам дал бы
    # AS80x80 9.0 % на 662 кэВ против 7.7 % у девятки — два разных разрешения
    # у одного и того же кристалла внутри одного корпуса.
    res_coef = {}
    for row in json.load(open(os.path.join(LAB, 'data', 'calibration.json'),
                              encoding='utf-8')):
        if row.get('res_kev'):
            res_coef[row['det']] = np.array(row['res_kev'], dtype=float)
    for det in sorted({st['det'] for st in state.values()}):
        if det in res_coef:
            continue
        pts = resolution_points(state, det)
        if not pts:
            print('  НЕТ ТОЧЕК для модели разрешения %s' % det)
            continue
        if len(pts) >= 3:
            coef = corpus_calib.fit_resolution_kev(pts)
        else:
            # одна-две точки: только однопараметрическая sqrt(k*E), как у
            # исходной девятки (ASN16 = sqrt(2.940*E)); двух точек не хватит,
            # чтобы отличить настоящий c0 от шума
            w = np.array([p[2] for p in pts])
            e = np.array([p[0] for p in pts])
            f = np.array([p[1] for p in pts])
            coef = np.array([0.0, float((f ** 2 * e * w).sum() /
                                        max((e * e * w).sum(), 1e-9)), 0.0])
        res_coef[det] = coef
        rf = corpus_calib.resolution_fn(coef)
        if not (0.001 <= rf(662.0) / 662.0 <= 0.30):
            print('  МОДЕЛЬ РАЗРЕШЕНИЯ %s неправдоподобна: %.2f%% на 662 кэВ'
                  % (det, 100 * rf(662.0) / 662.0))
    for det in sorted(res_coef):
        rf = corpus_calib.resolution_fn(res_coef[det])
        print('  %-10s FWHM: ' % det + '  '.join(
            '%d:%.1f%%' % (e, 100 * rf(e) / e) for e in (60, 300, 662, 1461, 2615)))

    # --- запись ---
    rows = []
    for key, st in state.items():
        det = st['det']
        if det not in res_coef:
            print('%-20s пропущен: нет модели разрешения' % key)
            continue
        rf = corpus_calib.resolution_fn(res_coef[det])
        fwhm_ch = corpus_calib.fwhm_channel_coef(st['ecal'], rf, st['sp'].n)
        st['fwhm_ch'] = fwhm_ch
        pcfg = dict(PEAK_CONFIG_DEFAULTS)
        if det not in LEGACY_DETS:
            pcfg['Ch_Concat'] = concat_for(fwhm_ch, st['ecal'], st['sp'].n)
        pcfg['FWHM_AT_0'] = round(float(np.sqrt(max(fwhm_ch[0], 1e-6))), 4)
        pcfg['Ch_Fwhm'] = st['sp'].n // 2
        pcfg['Width_Fwhm'] = int(round(float(np.sqrt(max(
            fwhm_ch[0] + fwhm_ch[1] * (st['sp'].n // 2) +
            fwhm_ch[2] * (st['sp'].n // 2) ** 2, 1e-6)))))
        if det in NEW_DEVICE and det not in LEGACY_DETS:
            pcfg['Min_Range'] = NEW_DEVICE[det]['lo']
            pcfg['Max_Range'] = NEW_DEVICE[det]['hi']
        st['peak_config'] = pcfg
        dest = os.path.join(OUT_SPECTRA, key + '.xml')
        write_copy(st['raw'], dest,
                   st['ecal'].coef,
                   st['bg_ecal'].coef if st['bg_ecal'] is not None else None,
                   fwhm_ch)
        e = st['entry']
        rows.append(dict(
            key=key, det=det, channels=st['sp'].n,
            live=round(st['sp'].live, 1),
            counts=int(st['sp'].counts.sum()),
            chains=';'.join(e.get('chains') or []),
            nuclides=';'.join(e.get('nuclides') or []),
            background=st['bg_source'] or 'нет',
            ecal_mode=st['mode'], ecal_lines=len(st['accepted']),
            ecal_rms_kev=round(calibrate.rms(st['accepted'], st['ecal']), 2),
            ecal_rms_fwhm=round(corpus_calib.residual_fwhm(
                st['ecal'], st['accepted'], st['r662'] * np.sqrt(662.0)), 3),
            bg_ecal_mode=st['bg_mode'],
            fwhm_662_pct=round(100 * rf(662.0) / 662.0, 2),
            source=os.path.relpath(resolve(e['path']), corpus_def.LIB),
            result_data=e.get('idx', 0),
            why=e['why'],
        ))

    devices = write_devices(state)
    print('устройства: %d' % len(devices))
    for det, fname, how in devices:
        print('   %-10s %-45s %s' % (det, fname, how))

    if only is None:
        rows = legacy_manifest(legacy_rows) + rows
        write_manifest(rows)
        write_detectors(state, res_coef, rows)

    with open(os.path.join(HERE, 'corpus_state.json'), 'w', encoding='utf-8') as fh:
        json.dump({k: dict(det=v['det'], ecal=[float(c) for c in v['ecal'].coef],
                           fwhm_ch=[float(c) for c in v.get('fwhm_ch', [])],
                           res_kev=[float(c) for c in res_coef.get(v['det'], [])],
                           mode=v['mode'], bg_mode=v['bg_mode'])
                   for k, v in state.items()}, fh, ensure_ascii=False, indent=1)
    return rows


if __name__ == '__main__':
    result = main()
    print('\nзаписано новых копий: %d' % len(result))
