# -*- coding: utf-8 -*-
"""Приёмка корпуса: насколько записанные копии попадают в табличные энергии.

Независимо от того, как строилась калибровка, вопрос один: садится ли пик на
свою табличную энергию и насколько промахивается — в долях полуширины, потому
что библиотечный фит опознаёт линию по якорю с допуском 0.5 FWHM
(LibraryPeakFitter.AnchorMatchToleranceFwhm), а «заявляет» её в 0.25 FWHM.
Отсюда и критерий приёмки: медиана невязки заметно меньше 0.25 FWHM.

Разбор идёт по ЗАПИСАННОМУ файлу: берётся его EnergyCalibration и его же
SqrtFwhmCalibration, ничего не подгоняется заново.

Запуск: python check_corpus.py [--key=...] [--verbose]
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

import numpy as np

# ⛔ ПРИЁМКА ОБЯЗАНА ДОЙТИ ДО ВЕРДИКТА В ЛЮБОЙ КОНСОЛИ (`A71`, 02.09.2026).
#
# В отчёте пять знаков, которых нет в cp1251: ⚠ ⛔ ² χ σ. Пока `sys.stdout`
# отдаёт utf-8, они печатаются; но стоит выключить режим UTF-8
# (`PYTHONUTF8=0` — а это ещё и умолчание питонов до 3.15), как кодировкой
# потока становится cp1251, и ПЕРВЫЙ же такой знак роняет приёмку
# `UnicodeEncodeError`. Проверено прямым вызовом: `sys.stdout.write(chr(0x26A0))`
# при `enc=cp1251` даёт трассу, а не строку.
#
# Цена — не косметика: отказ приходит ПОСЕРЕДИНЕ отчёта, вердикта и кода
# возврата не видно вовсе, и «приёмка упала» неотличимо от «корпус не сошёлся».
#
# ⚠ Меняется ТОЛЬКО политика ошибок, а НЕ кодировка. Соседние скрипты пишут
# `reconfigure(encoding="utf-8")`, и здесь это было бы хуже: в cp1251-консоли
# utf-8 превращает в кашу ВЕСЬ русский текст, а не пять знаков. С `replace`
# кириллица остаётся читаемой (она в cp1251 есть), а пять знаков становятся
# знаком вопроса.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(errors='replace')
    except (AttributeError, ValueError):        # поток не текстовый или подменён
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)
SPECTRA = os.path.join(LAB, 'corpus', 'spectra')

sys.path.insert(0, HERE)
import calibrate                                      # noqa: E402
import corpus_calib                                   # noqa: E402
import corpus_def                                     # noqa: E402
import build_corpus                                   # noqa: E402
import spectrum                                       # noqa: E402
import gaussfit                                      # noqa: E402
from gaussfit import fit_peak, fit_peak_ex, FWHM_SIGMA  # noqa: E402

calibrate.sample_lines = build_corpus.sample_lines

# Порог приёмки в долях FWHM: за ним якорь перестаёт совпадать с пиком.
TOL_OK = 0.25
TOL_WARN = 0.40


#: Узел привязки кривой — `<Efficiency>` с `<Guid>` внутри. Проверять НАЛИЧИЕ
#: узла подстрокой `'<Efficiency>' in text` нельзя, и это ловушка: в файле с
#: кривой тег встречается 35 раз — каждая точка кривой записана им же
#: (`<Efficiency>0.00636…</Efficiency>` внутри `ROIEfficiencyData`). Ищем ровно
#: открывающий тег узла: за ним сразу идёт `<Guid>`, по которому и находится
#: матрица.
EFF_NODE = re.compile(r'<Efficiency>\s*<Guid>')


def has_efficiency_node(text):
    return EFF_NODE.search(text) is not None


#: Имя кривой в узле — по нему видно, ЧЬЯ она.
EFF_NODE_NAME = re.compile(r'<Efficiency>\s*<Guid>[^<]*</Guid>\s*<Name>([^<]*)</Name>')


def efficiency_node_name(text):
    m = EFF_NODE_NAME.search(text)
    return m.group(1).strip() if m else None


def load(path):
    root = ET.parse(path).getroot()
    rd = root.find('ResultDataList/ResultData')
    es = rd.find('EnergySpectrum')
    counts = np.array([int(d.text) for d in es.findall('Spectrum/DataPoint')], dtype=float)
    ecal = [float(x.text) for x in es.findall('EnergyCalibration/Coefficients/Coefficient')]
    # Оба вида кривой ПШПВ (`V2`): корневая тройкой, степенная парой.
    fw = rd.find('SqrtFwhmCalibration')
    if fw is None:
        fw = rd.find('PowerFwhmCalibration')
    fwhm = [float(x.text) for x in fw.findall('Coefficients/Coefficient')] if fw is not None else None
    return counts, ecal, fwhm, rd


def fwhm_ch_at(coef, ch):
    return float(max(spectrum.fwhm_from_coef(coef, ch), 1e-3))


def check(entry, verbose=False):
    path = os.path.join(SPECTRA, entry['key'] + '.xml')
    if not os.path.isfile(path):
        return dict(key=entry['key'], err='нет файла')
    counts, ecal_coef, fwhm_coef, rd = load(path)
    cal = corpus_calib.Ecal(ecal_coef, len(counts))
    if fwhm_coef is None:
        return dict(key=entry['key'], err='нет кривой ПШПВ (ни корневой, ни степенной)')

    ent = dict(entry)
    ent['wanted'] = build_corpus.wanted_lines(entry)
    # разрешение берём из САМОГО файла — проверяем то, что записано
    def res_fn(e):
        ch = cal.channel(e)
        return fwhm_ch_at(fwhm_coef, ch) * abs(cal.dEdch(ch))

    # Порог чистоты ослабляется, пока не наберётся три линии: на 1024-канальном
    # приборе с полушириной 12 % чистых линий нет вообще, и фиксированный порог
    # оставлял фон и Obsidian вовсе без проверки.
    rows = []
    # `V13`: фит, который РАЗБЕЖАЛСЯ, и линия, которой НЕТ, — разные вещи, и до
    # 25.08.2026 приёмка не различала их вовсе (`gaussfit` отдавал `None` в
    # обоих случаях). Разбег считаем отдельно и печатаем: спектр, у которого
    # приёмка молчит из-за расходимости фита, выглядел «чистым».
    # ⚠ Упор в предел σ (`gaussfit.BOUND`) сюда НЕ входит: у спектра, где линии
    # из списка просто нет, он срабатывает по восемь раз, и сторож утонул бы.
    noconv = []
    for purity_bar in (0.85, 0.75, 0.60, 0.45):
        rows = []
        noconv = []
        for e_ref, label, purity, e_table in calibrate.curate(
                ent, res_fn, min_purity=purity_bar):
            ch0 = cal.channel(e_ref)
            if ch0 < 5 or ch0 > len(counts) - 6:
                continue
            fw_ch = fwhm_ch_at(fwhm_coef, ch0)
            r, status = fit_peak_ex(counts, ch0, fw_ch / FWHM_SIGMA, window=2.4)
            if status in (gaussfit.NOCONV, gaussfit.SINGULAR):
                noconv.append((e_ref, label, status))
            if r is None or r['sig'] < 8.0:
                continue
            if abs(r['mu'] - ch0) > 1.5 * fw_ch:
                continue
            width_ratio = r['fwhm'] / fw_ch
            if not 0.4 <= width_ratio <= 2.5:
                continue
            rows.append(dict(e=e_ref, label=label, purity=purity,
                             d_kev=cal.energy(r['mu']) - e_ref,
                             d_fwhm=(r['mu'] - ch0) / fw_ch,
                             width_ratio=width_ratio, sig=r['sig']))
        if len(rows) >= 3:
            break
    if not rows:
        err = 'ни одной линии'
        if noconv:
            err += ' (фит НЕ СОШЁЛСЯ на %d: %s)' % (
                len(noconv), ', '.join('%.1f' % e for e, _l, _s in noconv[:5]))
        return dict(key=entry['key'], det=entry['det'], n=0, err=err,
                    noconv=len(noconv))
    d = np.array([abs(r['d_fwhm']) for r in rows])
    wr = np.array([r['width_ratio'] for r in rows])
    out = dict(key=entry['key'], det=entry['det'], n=len(rows),
               med=float(np.median(d)), p90=float(np.percentile(d, 90)),
               worst=float(d.max()),
               med_kev=float(np.median([abs(r['d_kev']) for r in rows])),
               width_med=float(np.median(wr)), rows=rows,
               noconv=len(noconv))
    if verbose and noconv:
        print('  фит НЕ СОШЁЛСЯ на %d линиях: %s' % (
            len(noconv), ', '.join('%.1f (%s)' % (e, st) for e, _l, st in noconv)))
    if verbose:
        print('  %-24s %8s %9s %8s %7s' % ('линия', 'E, кэВ', 'd, кэВ', 'd/FWHM', 'ширина'))
        for r in sorted(rows, key=lambda x: x['e']):
            print('  %-24s %8.2f %+9.2f %+8.2f %7.2f' % (
                r['label'][:24], r['e'], r['d_kev'], r['d_fwhm'], r['width_ratio']))
    return out


#: Состав набивки в сцене: `SC_`/`SM_` — цилиндр и маринелли. Точечные сцены
#: вещества не имеют вовсе, и это не пропуск: у ОСГИ в паспорте стоит
#: `Material=not essential`, `Mass=0`.
SRC_PREFIX = {'CYLINDER': 'SC', 'MARINELLI': 'SM'}


def scene_composition(path):
    """Массовые доли по Z из файла сцены `.in` (он в cp1251)."""
    with open(path, encoding='cp1251', errors='replace') as fh:
        text = fh.read()
    values = dict(re.findall(r'^\s*([A-Za-z_0-9.\[\]]+)\s*=\s*(.+?)\s*$',
                             text, re.M))
    prefix = SRC_PREFIX.get(values.get('SourceType', '').strip())
    if prefix is None:
        return None
    try:
        count = int(values.get('%s_nSourceElements' % prefix, '0'))
    except ValueError:
        return None
    out = {}
    for i in range(count):
        z = values.get('%s_ZSource[%d]' % (prefix, i))
        fraction = values.get('%s_FractionsSource[%d]' % (prefix, i))
        if z is None or fraction is None:
            continue
        try:
            out[int(z)] = float(fraction.split()[0])
        except ValueError:
            continue
    return out


def check_composition():
    """`B13`: набивка сцены обязана быть той, что названа В ЗАГОЛОВКЕ спектра.

    Сторож заведён 16.08.2026, и вот почему его не было: приёмка проверяла, что
    геометрия НАЗВАНА и что файл на месте, но в файл не заглядывала ни разу.
    Поставка ЛСРМ называет РАЗНЫЙ состав под ОДНИМ именем вещества в разные
    поверки (`ОИСН-06`: без железа в 2016, Fe 0.151 в 2024; `ОИСН-16`: 0.655412
    против 0.714), а сцена строилась по ИМЕНИ вещества — и все 24 съёмки 2024
    года молча считались с веществом 2016-го.

    Это НАПОМИНАНИЕ, а не отказ, ровно как у `B11`: привести сцены в порядок
    значит пересчитать матрицы и СДВИНУТЬ базу, а решение о смене базы — за
    Amber, не за приёмкой. Молчать при этом нельзя.
    """
    import csv
    table = os.path.join(LAB, 'data', 'lsrm_spectrum_geometry.csv')
    geom_dir = os.path.join(LAB, 'corpus', 'geometries')
    index = os.path.join(geom_dir, 'index.csv')
    print('\n== набивка сцены против заголовка спектра (B13) ==')
    if not (os.path.isfile(table) and os.path.isfile(index)):
        print('  нет таблицы или описи — проверка не делалась')
        return True

    wanted = {}
    with open(table, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            raw = (row.get(u'состав_Z_доля') or '').strip()
            if not raw:
                continue
            comp = {}
            for part in raw.split():
                z, _, value = part.partition(':')
                try:
                    comp[int(z)] = float(value)
                except ValueError:
                    pass
            if comp:
                wanted[row[u'спектр']] = comp

    bad, checked = [], 0
    with open(index, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            spectrum, geometry = row['spectrum'], row['geometry']
            if spectrum not in wanted:
                continue
            path = os.path.join(geom_dir, geometry + '.in')
            if not os.path.isfile(path):
                continue
            got = scene_composition(path)
            if got is None:          # точечная сцена — вещества нет
                continue
            checked += 1
            want = wanted[spectrum]
            same = (set(want) == set(got)
                    and all(abs(want[z] - got[z]) < 5e-4 for z in want))
            if not same:
                bad.append((spectrum, geometry, want, got))

    print('  сверено сосудных спектров: %d' % checked)
    if not bad:
        print('  СОШЛОСЬ')
        return True
    print('  РАСХОДЯТСЯ: %d' % len(bad))
    for spectrum, geometry, want, got in bad:
        fmt = lambda c: ' '.join('%d:%.4f' % (z, c[z]) for z in sorted(c))
        print('    %-24s -> %s' % (spectrum, geometry))
        print('       заголовок: %s' % fmt(want))
        print('       сцена    : %s' % fmt(got))
    print('     сцена построена по ИМЕНИ вещества, а имя у двух поверок общее;')
    print('     привести в порядок — пересчитать матрицы и СДВИНУТЬ базу (B13)')
    return True


#: Guid узла привязки — по нему разбор ИЩЕТ ФАЙЛ МАТРИЦЫ.
EFF_NODE_GUID = re.compile(r'<Efficiency>\s*<Guid>([^<]*)</Guid>')


def check_response_store():
    """`B14`: матрица лежит там, откуда её берёт РАЗБОР, а не там, где удобно.

    Сторож заведён 16.08.2026, и он про два разных файла. `check_parts` выше
    проверяет `geometries/<геометрия>.rmx` — тот, что пишет `CorpusMatrixProbe`.
    А разбор берёт матрицу совсем иначе: `ResponseMatrixStore.Load(Guid)` →
    `geometries/response/<guid>.rmx`, по guid из узла `<Efficiency>` самого
    спектра. Пути разошлись, и 16.08.2026 это стоило дорого: у всех 37 сосудных
    спектров первый файл лежал на месте, второго не было ни одного, и понятная
    часть почти наполовину считалась БЕЗ матрицы — молча, потому что смотрели
    не туда. Узлы при этом были: их проставил `restore_eff_nodes.py`, который
    матрицу не трогает по построению.

    Это ОТКАЗ, а не напоминание: спектр понятной части без матрицы называет
    себя понятным и смешивает две модели внутри одной части — то, ради чего
    раздел и заводился.
    """
    import csv
    geom_dir = os.path.join(LAB, 'corpus', 'geometries')
    store = os.path.join(geom_dir, 'response')
    parts_path = os.path.join(LAB, 'corpus', 'parts.csv')
    print('\n== матрица там, откуда её берёт разбор (B14) ==')
    if not os.path.isfile(parts_path):
        print('  нет parts.csv — проверка не делалась')
        return True

    with open(parts_path, encoding='utf-8-sig', newline='') as fh:
        rows = [r for r in csv.DictReader(fh) if r['part'] == 'known']

    missing, noguid, checked = [], [], 0
    for row in rows:
        path = os.path.join(SPECTRA, row['spectrum'] + '.xml')
        if not os.path.isfile(path):
            continue
        with open(path, encoding='utf-8-sig') as fh:
            text = fh.read()
        m = EFF_NODE_GUID.search(text)
        if m is None or not m.group(1).strip():
            noguid.append(row['spectrum'])
            continue
        checked += 1
        guid = m.group(1).strip()
        if not os.path.isfile(os.path.join(store, guid + '.rmx')):
            missing.append('%s (guid %s, геометрия %s)'
                           % (row['spectrum'], guid[:8], row['geometry']))

    print('  сверено спектров понятной части: %d' % checked)
    if noguid:
        print('  БЕЗ GUID в узле: %d — %s' % (len(noguid), ', '.join(noguid)))
    if missing:
        print('  НЕТ ФАЙЛА В response/: %d' % len(missing))
        for line in missing:
            print('    %s' % line)
        print('     эти спектры разберутся БЕЗ матрицы, назвавшись понятными;')
        print('     положить туда матрицы: CorpusEffProbe.exe (ПОСЛЕ CorpusMatrixProbe)')
        return False
    if not noguid:
        print('  СОШЛОСЬ')
    return not noguid


#: Насколько коэффициентам записанного узла позволено разойтись с планом.
#: План считается ТЕМ ЖЕ кодом от ТЕХ ЖЕ входов, поэтому расхождение здесь —
#: не численный шум, а другой вход: шкала, модель группы или порядок шагов.
#: Порог оставлен на уровне шума `lstsq`, а не «инженерного допуска».
NODE_COEF_TOL = 1e-6


def check_fwhm_node():
    u"""`T61`: третий шаг пересборки не пропущен — форма узла ПШПВ та, что положена.

    Сторож заведён 24.08.2026, и вот почему его не было: приёмка спрашивала у
    файла ЛЮБУЮ кривую ПШПВ (`load` берёт `SqrtFwhmCalibration`, а если его
    нет — `PowerFwhmCalibration`) и на этом успокаивалась. Между тем `build_corpus.py
    --from-library` пишет КОРНЕВУЮ форму всем 129 спектрам, а степенную (`V2`,
    100 спектров семи групп) кладёт ОТДЕЛЬНАЯ команда
    `res_apply.py --mode=power-node --apply`. Пропустив её, корпус получает
    правдоподобную, но чужую модель разрешения — и приёмка говорила «СОШЛОСЬ».
    Цена измерена: Σχ² понятной части **477.8 → 628.9 (+32 %)**, медиана
    χ²/ndf 2.90 → 3.61, recall 99 → 92 %.

    ⛔ Ожидание берётся из `res_apply.plan()` — ТОГО ЖЕ кода, которым узел и
    пишется, — а не из отдельного списка «кому положено». Список разошёлся бы с
    записью на первом же особом случае, а их три: вырожденная шкала, нефизичная
    степень в каналах и приёмка «вне предела, но прежняя хуже». Поверка, которая
    сверяет не с тем, что пишется, проходит всегда (`D27`).

    Это ОТКАЗ: молча посчитанная чужой моделью база выглядит как настоящая.
    """
    import res_apply
    print(u'\n== форма узла кривой ПШПВ (T61) ==')
    try:
        rows, switched, _verdict = res_apply.plan('power-node')
    except Exception as exc:                      # noqa: BLE001
        print(u'  план не построился (%s) — проверка не делалась' % exc)
        return True

    lost, wrong, stray, drift, absent = [], [], [], [], []
    for rec in rows:
        nodes = res_apply.stored_nodes(rec['src'])
        if not nodes:
            absent.append(rec['key'])
            continue
        if len(nodes) > 1:
            # `B18`: видов кривой три, а поле в модели одно — разбор возьмёт
            # первый попавшийся, и это может быть не тот, что писал корпус.
            stray.append('%s (%s)' % (rec['key'],
                                      ', '.join(n for n, _ in nodes)))
            continue
        tag, coef = nodes[0]
        kind = res_apply.NODE_KIND.get(tag)
        if rec['kind'] is None:                   # спектр оставлен как был
            continue
        if kind != rec['kind']:
            (lost if rec['kind'] == 'power' else wrong).append(
                '%s [%s] узел %s, а положен %s'
                % (rec['key'], rec['det'], tag,
                   'PowerFwhmCalibration' if rec['kind'] == 'power'
                   else 'SqrtFwhmCalibration'))
            continue
        want = list(rec['coef'])
        if len(coef) != len(want) or any(
                abs(a - b) > NODE_COEF_TOL * max(abs(b), 1e-12)
                for a, b in zip(coef, want)):
            drift.append('%s [%s] %s против %s'
                         % (rec['key'], rec['det'],
                            ' '.join('%.6g' % v for v in coef),
                            ' '.join('%.6g' % v for v in want)))

    print(u'  сверено спектров: %d, групп со степенной формой: %d (%s)'
          % (len(rows), len(switched), ', '.join(sorted(switched))))
    for title, sink, hint in (
            (u'ПОТЕРЯНА СТЕПЕННАЯ ФОРМА', lost,
             u'пропущен третий шаг пересборки; без него Σχ² хуже на 32 %:\n'
             u'     python tools/CORPUS/scripts/res_apply.py --mode=power-node --apply'),
            (u'ЛИШНЯЯ СТЕПЕННАЯ ФОРМА', wrong,
             u'узел степенной там, где план его не даёт — шаг шёл по ДРУГИМ входам'),
            (u'ДВА УЗЛА КРИВОЙ В ОДНОМ ФАЙЛЕ', stray,
             u'разбор возьмёт первый попавшийся, а не свой (B18)'),
            (u'КОЭФФИЦИЕНТЫ РАЗОШЛИСЬ С ПЛАНОМ', drift,
             u'форма та, а числа чужие — узел лёг ДО правки шкалы или модели группы.\n'
             u'     Чаще всего это НЕ дрейф, а свежая правка ИСТИНЫ корпуса: план\n'
             u'     берётся у decide(measured_points()), а measured_points строит\n'
             u'     список линий из nuclides/chains манифеста — то есть добавленный\n'
             u'     спектру нуклид меняет модель группы, а узлы остаются прежними.\n'
             u'     Лечится пересборкой узлов:\n'
             u'     python tools/CORPUS/scripts/res_apply.py --mode=power-node --apply'),
            (u'НЕТ УЗЛА КРИВОЙ ВОВСЕ', absent,
             u'разбор откатится на калибровку прибора')):
        if sink:
            print(u'  %s: %d' % (title, len(sink)))
            for line in sink[:12]:
                print(u'    %s' % line)
            if len(sink) > 12:
                print(u'    … и ещё %d' % (len(sink) - 12))
            print(u'     %s' % hint)

    ok = not (lost or wrong or stray or drift or absent)
    if ok:
        print(u'  СОШЛОСЬ')
    return ok


def check_parts():
    """Раздел корпуса (B1): у каждого спектра назван part, у понятного —
    существующая геометрия и посчитанная под неё матрица.

    Проверяется не «файл есть», а покрытие: спектр без строки в `parts.csv`
    попадёт в сводку неизвестно какой половиной, и заметить это будет нечем.
    """
    import csv
    parts_path = os.path.join(LAB, 'corpus', 'parts.csv')
    geom_dir = os.path.join(LAB, 'corpus', 'geometries')
    print('\n== раздел корпуса ==')
    if not os.path.isfile(parts_path):
        print('НЕТ %s — прогоните scripts/split_corpus.py' % parts_path)
        return False

    with open(parts_path, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))

    keys = {e['key'] for e in corpus_def.ALL}
    named = {r['spectrum'] for r in rows}
    missing = sorted(keys - named)
    extra = sorted(named - keys)

    counts, bad, need_matrix = {}, [], set()
    no_node = []
    stray_node = []
    for r in rows:
        counts[r['part']] = counts.get(r['part'], 0) + 1
        if r['part'] != 'known':
            if r['geometry']:
                bad.append('%s: не known, а геометрия названа' % r['spectrum'])

            # Зеркальная ошибка к `T30`, и её не искал никто: у спектра
            # НЕПОНЯТНОЙ части узел кривой ЕСТЬ. Определение части — «ни
            # кривой, ни матрицы»: у остальных образ компонента строится из
            # одних пиков, а с кривой линии перевзвешиваются по энергии, то
            # есть модель другая. Сводка по такой части — среднее двух моделей,
            # ровно то, ради чего раздел и заводился. Узел приезжает из
            # исходного файла библиотеки: спектр сохранён с прикреплённой
            # кривой, а пересборка копирует `ResultData` целиком.
            spectrum_path = os.path.join(SPECTRA, r['spectrum'] + '.xml')
            if r['part'] == 'unknown' and os.path.isfile(spectrum_path):
                with open(spectrum_path, encoding='utf-8-sig') as fh:
                    text = fh.read()
                if has_efficiency_node(text):
                    stray_node.append('%s (кривая «%s»)'
                                      % (r['spectrum'], efficiency_node_name(text)))
            continue
        if not r['geometry']:
            bad.append('%s: known без геометрии' % r['spectrum'])
            continue

        # T30: геометрия на диске ЕСТЬ, а узел `<Efficiency>` в самом спектре
        # мог пропасть — полная пересборка строит копии заново и о вставленных
        # узлах не знает. Проверять надо ИМЕННО файл спектра: 14.08.2026 узлы
        # исчезли у всех тринадцати понятных, геометрии при этом остались на
        # месте, и приёмка говорила «СОШЛОСЬ», пока разбор шёл БЕЗ кривой и без
        # матрицы.
        spectrum_path = os.path.join(SPECTRA, r['spectrum'] + '.xml')
        if os.path.isfile(spectrum_path):
            with open(spectrum_path, encoding='utf-8-sig') as fh:
                text = fh.read()
            if not has_efficiency_node(text):
                no_node.append(r['spectrum'])
            else:
                # ⚠ «Узел есть» не значит «узел ТОТ». Пересборка тянет копию из
                # исходного файла библиотеки, а у него бывает СВОЙ узел: у
                # `!ASN16\Lu176.xml` это кривая «Цилиндр». Матрицы под чужой
                # guid не находится, и спектр понятной части ТИХО считается без
                # матрицы — 16.08.2026 это заметили только по колонке «с матр.
                # 16 из 17» в сводке прогона, а приёмка молчала.
                name = efficiency_node_name(text)
                if name != r['geometry']:
                    bad.append('%s: узел кривой «%s», а геометрия назначена %s'
                               % (r['spectrum'], name, r['geometry']))

        if not os.path.isfile(os.path.join(geom_dir, r['geometry'] + '.in')):
            bad.append('%s: нет геометрии %s.in' % (r['spectrum'], r['geometry']))
        # Матрица в репозиторий НЕ кладётся (решение Amber 09.08.2026): она
        # двоичная, весит полмегабайта на геометрию, пересчитывается заново при
        # каждой смене версии физики и воспроизводится побитово — зерно узла не
        # зависит от порядка счёта. Поэтому её отсутствие не отказ приёмки, а
        # напоминание прогнать `corpusmatrixprobe`: у свежего клона её и не
        # может быть.
        elif not os.path.isfile(os.path.join(geom_dir, r['geometry'] + '.rmx')):
            need_matrix.add(r['geometry'])

    for part in ('known', 'unknown', 'excluded'):
        print('  %-9s %3d' % (part, counts.get(part, 0)))
    if missing:
        print('  БЕЗ СТРОКИ В parts.csv: %s' % ', '.join(missing))
    if extra:
        print('  ЛИШНИЕ В parts.csv: %s' % ', '.join(extra))
    for line in bad:
        print('  %s' % line)
    if need_matrix:
        print('  матрицы нет у %d геометрий (%s) — прогоните corpusmatrixprobe;'
              ' в репозитории их нет нарочно'
              % (len(need_matrix), ', '.join(sorted(need_matrix))))
    if no_node:
        # Это ОТКАЗ, а не напоминание, в отличие от матрицы: без узла разбор
        # понятной части идёт без кривой и без матрицы, а называет себя
        # понятным. Тихо хуже — худший из исходов.
        print('  БЕЗ УЗЛА <Efficiency> в самом спектре: %d — %s'
              % (len(no_node), ', '.join(no_node)))
        print('     кривая и матрица из прогона ПРОПАЛИ; вернуть те же узлы:')
        print('     python tools/CORPUS/scripts/restore_eff_nodes.py --apply')
    if stray_node:
        # НАПОМИНАНИЕ, а не отказ: снять узел — значит изменить числа непонятной
        # части, а это решение Amber, не приёмки. Молчать при этом нельзя.
        #
        # ⚠ Уточнено 24.08.2026 при `W29`: раздача узлов
        # (`restore_eff_nodes.py`) к этим спектрам НЕ ПРИЧАСТНА — клетка
        # `geometry` у всех троих в `parts.csv` пуста, и с 24.08.2026
        # инструмент им отказывает прямо. Узел приезжает С КОПИЕЙ: пересборка
        # копирует `ResultData` целиком, а спектр в библиотеке был сохранён с
        # прикреплённой кривой. Правило «кривая положена всем с геометрией»
        # (решение Amber 24.08.2026) их не покрывает — геометрии у них нет.
        print('  У НЕПОНЯТНОЙ ЧАСТИ ЕСТЬ УЗЕЛ КРИВОЙ: %d — %s'
              % (len(stray_node), ', '.join(stray_node)))
        print('     эти спектры разбираются С кривой, остальные без неё:')
        print('     часть перестаёт быть однородной по модели (см. B11, W29)')
        print('     ⚠ и это ТРЕТЬЯ модель, а не одна из двух: геометрии этих')
        print('       кривых в корпусе нет, матрицы по guid тоже — то есть')
        print('       кривая ЕСТЬ, а матрицы НЕТ; проверить поимённо:')
        print('       python tools/CORPUS/scripts/restore_eff_nodes.py')

    ok = not (missing or extra or bad or no_node)
    print('  %s' % ('СОШЛОСЬ' if ok else 'РАЗОШЛОСЬ'))
    return ok


def main():
    only = None
    verbose = '--verbose' in sys.argv
    for a in sys.argv[1:]:
        if a.startswith('--key='):
            only = set(a.split('=', 1)[1].split(','))

    print('%-20s %-9s %4s %8s %8s %8s %8s  %s' % (
        'спектр', 'детектор', 'лин', 'медиана', 'p90', 'макс', 'ширина', 'вердикт'))
    bad, warn, nc = [], [], []
    for e in corpus_def.ALL:
        if only and e['key'] not in only:
            continue
        if verbose:
            print('== %s' % e['key'])
        r = check(e, verbose)
        if r.get('noconv'):
            nc.append('%s:%d' % (r['key'], r['noconv']))
        if r.get('err'):
            print('%-20s %-9s  --  %s' % (r['key'], e['det'], r['err']))
            warn.append(r['key'])
            continue
        if r['med'] > TOL_WARN:
            verdict, sink = 'ПЛОХО', bad
        elif r['med'] > TOL_OK:
            verdict, sink = 'на грани', warn
        else:
            verdict, sink = 'ок', None
        if sink is not None:
            sink.append(r['key'])
        print('%-20s %-9s %4d %8.3f %8.3f %8.3f %8.2f  %s' % (
            r['key'], r['det'], r['n'], r['med'], r['p90'], r['worst'],
            r['width_med'], verdict))
    print('\nплохих: %d %s' % (len(bad), ', '.join(bad)))
    print('спорных/непроверенных: %d %s' % (len(warn), ', '.join(warn)))
    # `V13`: отдельная строка, потому что отказ фита раньше выглядел как
    # «линии нет» и не показывался нигде.
    print('спектров, где фит РАЗБЕЖАЛСЯ хотя бы на одной линии: %d %s'
          % (len(nc), ', '.join(nc)))
    if nc:
        print('     ⛔ это регресс демпфера `gaussfit` — по корпусу должно быть 0')
    if only:
        return 0

    # Код возврата, а не только печать (T30). Приёмка, которая ВСЕГДА выходит
    # нулём, не читается ничем, кроме глаз: скрипт конвейера после неё
    # продолжится как ни в чём не бывало. «Плохие» по невязке сюда не входят —
    # это свойство спектра, а не поломка корпуса; отказом считается только
    # нарушение целостности: пропавшая строка parts.csv, лишняя строка,
    # отсутствующая геометрия, потерянный узел `<Efficiency>`.
    ok = check_parts()
    # `B14` — ОТКАЗ и входит в код возврата: спектр понятной части без матрицы
    # называет себя понятным, а это порча самой сводки, не «числа сдвинутся».
    ok &= check_response_store()
    # `T61` — ОТКАЗ по той же причине: корпус с потерянной формой узла ПШПВ
    # считается ЧУЖОЙ моделью разрешения и выглядит при этом настоящим.
    # Стоит ~18 с (меряет ширины линий заново), поэтому есть ключ пропуска —
    # но по умолчанию сторож ВКЛЮЧЁН: выключенный по умолчанию сторож не сторож.
    if '--no-fwhm-node' not in sys.argv:
        ok &= check_fwhm_node()
    # Порядок намеренный: состав печатается ПОСЛЕ раздела, чтобы напоминание не
    # тонуло выше вердикта, и в код возврата не входит (см. `check_composition`).
    check_composition()
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
