# -*- coding: utf-8 -*-
u"""Слепой перемер гейта отбора опорных линий: вершина, ложь, цена порога.

Мерка ничего не меняет и ничего не пересобирает: читает ПОСТАВЛЕННЫЕ спектры
корпуса (`corpus/spectra`, все 129), их собственную энергокалибровку и модель
разрешения ГРУППЫ из `corpus/detectors.csv`, и прогоняет по ним тот же
`corpus_calib.match_lines`, каким корпус набирает опорные линии.

Отвечает на три вопроса, и все три — ПО ЧАСТЯМ корпуса (`corpus/parts.csv`),
без сложения частей:

1. **Есть ли у линии штатного набора вершина.** Проверка (`vertex`) идёт по
   СЫРЫМ отсчётам и про гаусс не знает вовсе: сглаженные тройкой отсчёты
   обязаны иметь ВНУТРЕННИЙ максимум, поднятый на 3·sqrt(N) над прямой по краям
   окна и спадающий на столько же по ОБЕ стороны. Центр фита в проверку не
   входит — иначе она подтверждала бы сама себя (ср. `T95` в
   `gaussfit_check.bump_ok`, где он входил при обратном обещании).

   ⛔ **«Доля с вершиной» — величина ПРИЗНАКА, а не корпуса** (`V17`). Поэтому
   признаков тут ДВА, оба считаются всегда и печатаются рядом, а имя
   поставочного стоит в каждой строке с числом:

     * `пик-3√N` — прежний признак: внутренний максимум, подъём и спад 3·sqrt(N);
     * `пик-3√N+симм` — он же плюс СИММЕТРИЯ СПАДА: меньший спад не мельче
       `SYM_MIN` от большего. **Поставочный.**

   Симметрия введена 05.09.2026 отрицательным контролем, а не из красоты:
   канонический изгиб `G1S24_Y88_P5` 198.1 кэВ (рост через всё окно, вершины
   нет) `пик-3√N` объявляет вершиной — спад влево 10090, вправо 2295,
   отношение 0.227. Цена признака на штатном наборе мала: понятная
   73.6 % → 72.4 % (362 → 356 линий из 492), непонятная 74.3 % → 73.4 %,
   германий 90.7 % → 90.7 % (ни одной потери). Отвергнутый кандидат
   «кривизна-σ» и канонический изгиб принимает, и чувствительность рушит
   (на подсаженных z≈12 — 0 % против 84 % у `пик-3√N`).

2. **Отрицательный контроль.** Те же линии, отодвинутые на ±5 ПШПВ. ⛔ **Сами
   по себе такие позиции ложными НЕ ЯВЛЯЮТСЯ** (`V16`): сдвиг меряется в ПШПВ
   ПРИБОРА, и на сцинтилляторе 5 ПШПВ — это 107 кэВ при 200 кэВ (ASN16) и
   168 кэВ (AS80x80), то есть прыжок не из спектра, а на СОСЕДНЮЮ настоящую
   линию. Замер 05.09.2026 по ПОЛНОМУ списку излучений пробы
   (`build_corpus.sample_lines`, I >= 0.1 %): в пределах одной ПШПВ от
   настоящей линии стояли понятная 2113/2477 = **85.3 %**, непонятная
   1007/1200 = **83.9 %**, германий 48/414 = 11.6 %.
   Поэтому ложный набор ЧИСТИТСЯ по полному списку излучений (`--clean=`), а
   загрязнение предложенного набора печатается и служит сторожем: при
   `--clean=0` мерка НАЗЫВАЕТ его и возвращает 4.

3. **Цена порога по `sig_fit` = A/sigma(A)** (ковариация фита; считает её
   `gaussfit._amp_error`, гейтом она не служит). Для порогов 3 и 8: сколько
   линий остаётся, сколько остаётся ЛОЖНЫХ, и сколько спектров теряет опору.

**Стадия B — положительный контроль САМОЙ мерки вершины.** В спектр на пустое
место подсаживается гауссиана ЗАВЕДОМО СУЩЕСТВУЮЩАЯ, с модельной шириной
группы; меряется, с какого z проверка вершины начинает её видеть. Без этой
стадии долю из пункта 1 читать нельзя: неизвестно, «вершины нет» — это линии
нет или мерка не видит.

**Стадия C — ОТРИЦАТЕЛЬНЫЙ контроль мерки вершины, `V17`.** Стадия B меряет
ЧУВСТВИТЕЛЬНОСТЬ признака к настоящим пикам и по построению слепа к его
ложным срабатываниям: место для подсадки выбирается там, где признак уже
сказал «нет». Поэтому на пустое место подсаживается ШИРОКИЙ СМЕЩЁННЫЙ горб
(ширина BEND_W ПШПВ, центр отодвинут) — окно ловит его перегиб, вершины
модельной ширины там нет, и всякое «вершина есть» — ложь по построению.

Запуск:  python tools/CORPUS/scripts/gate_blind_check.py [--only=KEY,KEY]
                                                          [--csv=файл]
                                                          [--gate=prod|pass1]
                                                          [--vertex=ИМЯ]
                                                          [--clean=ПШПВ]
                                                          [--no-inject]

Коды возврата: 0 — всё сошлось; 3 — охват сломан (`T76`); 4 — ложный набор
загрязнён настоящими линиями (`V16`); 5 — признак вершины ловит подсаженный
изгиб чаще BEND_MAX (`V17`).
"""
import os
import sys
import io
import csv
import argparse

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)
SPECTRA = os.path.join(LAB, 'corpus', 'spectra')
PARTS = os.path.join(LAB, 'corpus', 'parts.csv')
DETECTORS = os.path.join(LAB, 'corpus', 'detectors.csv')
sys.path.insert(0, HERE)

# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.
# Кодировка utf-8, а не `backslashreplace` при cp1251: по замеру G5 06.09.2026
# (handover/g5-cp1251/04-readers.md) оба канала агента и check_all.py декодируют utf-8, и при
# cp1251 ВЕСЬ русский приходит как ������. Решение Amber 06.09.2026 — utf-8 тем же блоком (G7).
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

import corpus_def                                       # noqa: E402
import corpus_calib                                     # noqa: E402
import calibrate                                        # noqa: E402
import build_corpus                                     # noqa: E402
import gaussfit                                         # noqa: E402
import check_corpus                                     # noqa: E402
# `T76`: правило «сколько спектров каждой части объявлено» живёт В ОДНОМ месте.
# Своя копия тут уже была и уже отставала (не знала ни про ключи `--only` вне
# `corpus_def`, ни про часть '?').
from gaussfit_check import Coverage                     # noqa: E402

calibrate.sample_lines = build_corpus.sample_lines

FS = gaussfit.FWHM_SIGMA
#: последний (штатный) проход `corpus_calib.calibrate`
PROD = dict(tol_fwhm=0.7, width_lo=0.5, width_hi=1.8)
#: первый проход того же конвейера — им меряет `gaussfit_check.py`
PASS1 = dict(tol_fwhm=2.5, width_lo=0.35, width_hi=2.6)
#: на сколько ПШПВ отодвигается линия в отрицательном контроле
FAKE_OFFSET = 5.0
#: ложная линия ДАЛЬШЕ этого от любой НАСТОЯЩЕЙ линии считается «вдали»
FAR_FWHM = 2.0
#: `V16`: ближе этого (в ПШПВ) к настоящему излучению ложной позиции не бывает
CLEAN_FWHM = 1.0
#: `V16`: порог интенсивности, с которого излучение считается настоящей линией
I_MIN_FULL = 0.1
#: `V16`: доля загрязнения, выше которой ложный набор не отрицательный контроль
DIRTY_MAX = 0.05
#: `V17`: меньший спад не мельче этой доли от большего
SYM_MIN = 0.25
PARTS_ORDER = ('known', 'unknown', 'excluded', '?')
#: корзины значимости, в которых сравниваются наблюдение и контроль
ZBINS = ((0.0, 10.0, u'z 5..10'), (10.0, 30.0, u'z 10..30'),
         (30.0, 100.0, u'z 30..100'), (100.0, 1e18, u'z >100'))
#: стадия B
INJ_POS = 6
INJ_Z = (5.0, 8.0, 12.0, 20.0, 35.0, 60.0, 120.0)
INJ_SEED = 20260827
#: стадия C (`V17`): ширина подсаживаемого горба в МОДЕЛЬНЫХ ПШПВ и смещение
#: его центра — тоже в МОДЕЛЬНЫХ ПШПВ; z — высота над континуумом.
#: ⚠ Смещение мерится в ПШПВ ЛИНИИ, а не в ширинах горба: у канонического
#: изгиба вершина стоит в 4.2 канала от центра окна при модельной ПШПВ 9.16 —
#: то есть 0.46 ПШПВ, ВНУТРИ окна. Первая постановка мерила смещение в
#: ширинах горба (0.4…0.7 от 3…5 ПШПВ = 11…32 канала), выносила вершину за
#: край окна ±8.6 канала, и стадия давала 0 % ложных у ОБОИХ признаков —
#: контроль, который не может отказать, а не хороший признак.
#: `w=1.0, shift=0.0` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ САМОЙ СТАДИИ: горб модельной
#: ширины по центру окна признак обязан ПРИНЯТЬ, иначе нули строк ниже
#: означают лишь то, что подсадка не доехала.
BEND_W = (1.0, 2.5, 4.0)
BEND_SHIFT = (0.0, 0.5)
BEND_Z = (12.0, 35.0, 120.0)
BEND_POS = 6
#: `V17`: доля ложных срабатываний на изгибе, выше которой признак негоден.
#: ⚠ Это ЗАПИСЬ ИЗМЕРЕННОГО УРОВНЯ ПО ЧАСТЯМ, а не физическая граница: сторож
#: ловит УХУДШЕНИЕ признака, а не доказывает, что нынешний хорош. Уровни сняты
#: 05.09.2026 на z = 120, где признак к настоящим пикам чувствителен на
#: 88…100 % (собственный контроль стадии) и всякое «да» на широком горбе —
#: именно ложь. Замерено у поставочного `пик-3√N+симм`: понятная 30.5 %,
#: непонятная 28.5 %, германий 52.1 %; у прежнего `пик-3√N` — 37.5 / 30.4 /
#: 75.0 %, и на нём сторож отказывает в двух частях из трёх (проверено).
#: ⛔ Сама величина — находка, а не мелочь: «доля с вершиной» НЕ ПРОВЕРЯЕТ
#: ШИРИНУ, и яркая структура втрое шире модельной принимается за вершину
#: линии в трети случаев.
BEND_MAX = {'known': 0.35, 'unknown': 0.35, 'excluded': 0.60}
BEND_MAX_DEFAULT = 0.35

#: ⛔ `V17`: имена признаков вершины. Всякое число «доля с вершиной» обязано
#: печататься вместе с именем — разброс между признаками доходит до 14 пунктов.
VERTEX_NAMES = (u'пик-3√N', u'пик-3√N+симм')
VERTEX_DEFAULT = u'пик-3√N+симм'

#: ⛔ `V17`: именной отрицательный контроль признака. Окно, про которое дерево
#: знает, что вершины там НЕТ: отсчёты идут вверх через всё окно и лишь
#: заваливаются к правому краю — это перегиб комптоновского континуума, а не
#: пик. Вердикт КАЖДОГО признака печатается поимённо; поставочный обязан
#: сказать «нет», иначе мерка отказывает.
CANON = (('G1S24_Y88_P5', 198.1, False),)


def det_res_a():
    u"""a модели разрешения группы: FWHM(E) = a·sqrt(E)."""
    out = {}
    with io.open(DETECTORS, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            out[row['det']] = (float(row['fwhm_662_pct']) / 100.0 * 662.0
                               / np.sqrt(662.0))
    return out


def parts_of():
    out = {}
    with io.open(PARTS, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            out[row['spectrum']] = row['part']
    return out


# ---------------------------------------------------------------------------
# проверка вершины — БЕЗ гаусса
# ---------------------------------------------------------------------------
def vertex(counts, ch0, sigma0, window=2.2):
    u"""Есть ли в окне вокруг ch0 настоящая вершина. Фит не участвует.

    Считаются ОБА признака сразу (`V17`), потому что «доля с вершиной» — это
    величина признака, и один из них без другого читать нельзя:

      * `ok` — `пик-3√N`: внутренний максимум, подъём над хордой и спад по обе
        стороны не меньше 3·sqrt(N);
      * `ok_sym` — он же плюс `sym >= SYM_MIN`, где `sym` — отношение меньшего
        спада к большему. Поставочный: именно он отвергает канонический изгиб
        `G1S24_Y88_P5` 198.1 кэВ (sym = 0.227), который `пик-3√N` принимает.

    Возвращает dict или None (окно короче восьми каналов).
    """
    n = len(counts)
    half = max(4, int(round(window * sigma0)))
    lo = int(max(0, round(ch0 - half)))
    hi = int(min(n - 1, round(ch0 + half)))
    if hi - lo + 1 < 8:
        return None
    x = np.arange(lo, hi + 1, dtype=float)
    y = np.asarray(counts[lo:hi + 1], dtype=float)
    ys = np.convolve(y, np.ones(3) / 3.0, mode='same')
    ys[0], ys[-1] = y[0], y[-1]
    edge = max(2, x.size // 6)
    xe = np.concatenate([x[:edge], x[-edge:]])
    ye = np.concatenate([y[:edge], y[-edge:]])
    try:
        b1, b0 = np.polyfit(xe, ye, 1)
    except Exception:
        b1, b0 = 0.0, float(np.median(ye))
    k = int(np.argmax(ys))
    top = float(y[k] - (b0 + b1 * x[k]))
    need = 3.0 * np.sqrt(max(y[k], 1.0))
    inner = bool(1 <= k <= x.size - 2)
    rise = bool(top >= need)
    if inner:
        dl = float(ys[k] - ys[:k].min())
        dr = float(ys[k] - ys[k + 1:].min())
    else:
        dl = dr = 0.0
    drop = min(dl, dr)
    twoside = bool(drop >= need)
    sym = (drop / max(dl, dr)) if max(dl, dr) > 0 else 0.0
    ok = bool(inner and rise and twoside)
    d = np.diff(ys)
    return dict(ok=ok, ok_sym=bool(ok and sym >= SYM_MIN),
                sym=float(sym), inner=inner, rise=rise,
                twoside=twoside, monotone=bool(np.all(d >= 0) or np.all(d <= 0)),
                peak_ch=float(x[k]), top=top, need=need)


def verdict(v, name):
    u"""Вердикт ИМЕНОВАННОГО признака. Имя обязано ехать вместе с числом."""
    if v is None:
        return None
    if name == u'пик-3√N':
        return bool(v['ok'])
    if name == u'пик-3√N+симм':
        return bool(v['ok_sym'])
    raise ValueError(u'неизвестный признак вершины: %s' % name)


def full_lines(entry):
    u"""ВСЁ, что образец может излучить, — не только курированные опоры.

    ⛔ `V16`: отбирать ложные позиции по расстоянию до КУРИРОВАННЫХ опор
    неверно. Опор у спектра десяток, а излучает проба сотни линий, и «5 ПШПВ
    от опоры» на сцинтилляторе — это ровно соседняя настоящая линия.
    """
    return [(en, ii, nm) for en, ii, nm in build_corpus.sample_lines(entry)
            if ii >= I_MIN_FULL]


def fwhm_model_ch(cal, res_a, e_ref):
    u"""(канал, модельная ПШПВ в каналах) — как их считает `match_lines`."""
    ch0 = cal.channel(e_ref)
    dedch = abs(cal.dEdch(ch0))
    if dedch <= 0:
        return ch0, None
    return ch0, max(res_a * np.sqrt(max(e_ref, 5.0)) / dedch, 1.2)


def in_window(cal, n, e):
    u"""Дошла бы линия до фита вообще: те же границы, что в `match_lines`."""
    ch = cal.channel(e)
    return bool(4 <= ch <= n - 5 and abs(cal.dEdch(ch)) > 0)


def fake_lines(lines, res_a, sign, forbidden=None, clean=CLEAN_FWHM):
    u"""Те же линии, отодвинутые на ±5 ПШПВ — отрицательный контроль.

    `forbidden` — ПОЛНЫЙ список излучений пробы (`full_lines`). Позиция,
    попавшая ближе `clean` ПШПВ к любому из них, из набора ВЫБРАСЫВАЕТСЯ:
    настоящая линия ложной не бывает, как её ни назови (`V16`).

    ⛔ Загрязнение СЧИТАЕТСЯ ВСЕГДА, в том числе при `clean=0`. Иначе
    выключенная чистка обнуляла бы и счётчик, и сторож молчал бы ровно там,
    где обязан кричать: замер 05.09.2026 показал `--clean=0` -> «загрязнено 0»
    -> код возврата 0, то есть плохой вход проходил как хороший.

    Возвращает (набор, сколько позиций стоит на настоящей линии).
    """
    out = []
    dirty = 0
    yard = clean if clean > 0 else CLEAN_FWHM       # чем МЕРИТЬ загрязнение
    for e_ref, label, purity, e_table in lines:
        e2 = e_ref + sign * FAKE_OFFSET * res_a * np.sqrt(max(e_ref, 5.0))
        if e2 < 20.0:
            continue
        if forbidden:
            fw = max(res_a * np.sqrt(max(e2, 5.0)), 1e-9)
            if min(abs(e2 - t[0]) for t in forbidden) / fw <= yard:
                dirty += 1
                if clean > 0:
                    continue                        # чем ЧИСТИТЬ — только clean
        out.append((e2, label + u'/ЛОЖЬ', purity, e2))
    return out, dirty


def load_scene(entry, res_by_det):
    u"""(counts, cal, res_a, lines, full) либо (None, причина)."""
    key = entry['key']
    path = os.path.join(SPECTRA, key + '.xml')
    if not os.path.isfile(path):
        return None, u'нет файла в corpus/spectra'
    res_a = res_by_det.get(entry['det'])
    if res_a is None:
        return None, u'нет группы %s в detectors.csv' % entry['det']
    try:
        counts, ecal, fwhm_coef, rd = check_corpus.load(path)
    except Exception as ex:
        return None, u'ОШИБКА чтения: %s' % ex
    cal = corpus_calib.Ecal(ecal, len(counts))
    ent = dict(entry)
    ent['wanted'] = build_corpus.wanted_lines(entry)
    lines = calibrate.curate(
        ent, lambda en: res_a * np.sqrt(max(float(en), 5.0)), min_purity=0.45)
    if not lines:
        return None, u'нет курированных линий'
    return (counts, cal, res_a, lines, full_lines(entry)), None


# ---------------------------------------------------------------------------
# стадия A
# ---------------------------------------------------------------------------
def stage_a(entries, res_by_det, part_of, gate, clean=CLEAN_FWHM,
            coverage_only=False):
    rows = []
    agg = {}
    skipped = []
    read = {}
    # `T76`: два `continue` внутри `collect` роняли принятую линию БЕЗ СЛЕДА.
    # Сегодня они не срабатывают ни разу (замер 05.09.2026: 0 из 492/304/75
    # настоящих и 0 из 368/216/9 ложных), но ноль по счастью и ноль по
    # построению — разные вещи, а читателя у этих путей не было вовсе.
    lost = dict(scale=0, window=0)
    for e in entries:
        key = e['key']
        scene, why = load_scene(e, res_by_det)
        if scene is None:
            skipped.append((key, why))
            continue
        counts, cal, res_a, lines, full = scene
        n = len(counts)
        part = part_of.get(key, '?')
        read[key] = part
        if coverage_only:
            # `T76`: `--coverage-only` — вход читателя кодов 3/4/5. Стадия
            # охвата (`read`) уже посчитана ТЕМ ЖЕ чтением, что и в полном
            # прогоне; всё, что ниже, — работа гейта, и она к охвату
            # отношения не имеет.
            continue
        a = agg.setdefault(part, dict(sp=0, off=0, off_f=0, off_far=0,
                                      dirty=0, proposed=0))
        a['sp'] += 1
        a['off'] += sum(1 for L in lines if in_window(cal, n, L[0]))

        def collect(found, kind, far=None):
            for r in found:
                ch0, fw = fwhm_model_ch(cal, res_a, r['e_ref'])
                if fw is None:
                    lost['scale'] += 1
                    print(u'⛔ МОЛЧА ПОТЕРЯНА БЫ (шкала не растёт): %-24s %-6s '
                          u'%8.2f кэВ' % (key, kind, r['e_ref']))
                    continue
                v = vertex(counts, ch0, fw / FS)
                if v is None:
                    lost['window'] += 1
                    print(u'⛔ МОЛЧА ПОТЕРЯНА БЫ (окно < 8 каналов): %-24s %-6s '
                          u'%8.2f кэВ, канал %.1f' % (key, kind, r['e_ref'], ch0))
                    continue
                rows.append(dict(
                    key=key, det=e['det'], part=part, kind=kind,
                    e_ref=float(r['e_ref']), label=r['label'], ch=float(r['ch']),
                    sig=float(r['sig']), sig_fit=float(r.get('sig_fit', 0.0)),
                    fwhm=float(r['fwhm']), fwhm_model=float(fw),
                    vertex=1 if v['ok'] else 0,
                    vertex_sym=1 if v['ok_sym'] else 0,
                    sym=float(v['sym']),
                    monotone=1 if v['monotone'] else 0,
                    inner=1 if v['inner'] else 0,
                    far=1 if (far is not None and round(r['e_ref'], 3) in far) else 0))

        collect(corpus_calib.match_lines(counts, cal, lines, res_a, **gate), u'штат')

        # ⛔ ЛОЖНЫЙ НАБОР ПРОХОДИТ ВОРОТА ОДНИМ ВЫЗОВОМ, КАК И НАСТОЯЩИЙ.
        # Прежде здесь стоял цикл `for sign in (+1.0, -1.0)` с ОТДЕЛЬНЫМ
        # `match_lines` на каждый знак. Дедуп у `match_lines` работает ВНУТРИ
        # вызова, поэтому ложная позиция, на которую садятся оба знака,
        # засчитывалась ДВАЖДЫ, а настоящий набор дедупился целиком — сравнение
        # выходило несимметричным и завышало ложь примерно на 15 %
        # (понятная 434 против 372, непонятная 270 против 234). Найдено
        # встречной проверкой 27.08.2026; германий не был затронут.
        fl = []
        for sign in (+1.0, -1.0):
            part_fl, dirty = fake_lines(lines, res_a, sign, forbidden=full,
                                        clean=clean)
            fl.extend(part_fl)
            a['dirty'] += dirty
            # ⚠ при `clean=0` грязные позиции ОСТАЮТСЯ в наборе, поэтому
            # предложено = длина набора, а не сумма
            a['proposed'] += len(part_fl) + (dirty if clean > 0 else 0)
        if fl:
            far = set()
            for L in fl:
                if not in_window(cal, n, L[0]):
                    continue
                a['off_f'] += 1
                # ⛔ `V16`: «вдали» — вдали от ЛЮБОГО излучения пробы, а не от
                # десятка курированных опор. Прежде мерилось до `lines`, и
                # позиция, севшая на настоящую линию, считалась «вдали».
                ref = full or [(t[0],) for t in lines]
                d = min(abs(L[0] - t[0]) for t in ref) \
                    / max(res_a * np.sqrt(max(L[0], 5.0)), 1e-9)
                if d > FAR_FWHM:
                    a['off_far'] += 1
                    far.add(round(L[0], 3))
            collect(corpus_calib.match_lines(counts, cal, fl, res_a, **gate),
                    u'ложь', far=far)
    return rows, agg, skipped, read, lost


# ---------------------------------------------------------------------------
# стадия B — положительный контроль мерки вершины
# ---------------------------------------------------------------------------
def stage_b(entries, res_by_det, part_of):
    rng = np.random.RandomState(INJ_SEED)
    out = []
    occupied = [0, 0]
    for e in entries:
        scene, why = load_scene(e, res_by_det)
        if scene is None:
            continue
        counts, cal, res_a, lines, full = scene
        n = len(counts)
        part = part_of.get(e['key'], '?')
        busy = [cal.channel(a[0]) for a in lines]
        picked = 0
        tries = 0
        while picked < INJ_POS and tries < 200:
            tries += 1
            ch0 = float(rng.randint(int(0.05 * n), int(0.90 * n)))
            ee = cal.energy(ch0)
            dedch = abs(cal.dEdch(ch0))
            if ee <= 5 or dedch <= 0:
                continue
            fw = max(res_a * np.sqrt(max(ee, 5.0)) / dedch, 1.2)
            if ch0 - 3 * fw < 4 or ch0 + 3 * fw > n - 5:
                continue
            if any(abs(ch0 - b) < 3 * fw for b in busy):
                continue
            v0 = vertex(counts, ch0, fw / FS)
            if v0 is None:
                continue
            occupied[1] += 1
            if v0['ok']:
                occupied[0] += 1
                continue                      # тут уже что-то есть — не место
            picked += 1
            sg = fw / FS
            lo = int(max(0, round(ch0 - 4 * sg)))
            hi = int(min(n - 1, round(ch0 + 4 * sg)))
            xx = np.arange(lo, hi + 1, dtype=float)
            prof = np.exp(-0.5 * ((xx - ch0) / sg) ** 2)
            prof = prof / prof.sum()
            nbg = max(float(np.median(counts[lo:hi + 1])), 0.0) * sg * np.sqrt(2.0 * np.pi)
            for z in INJ_Z:
                area = 0.5 * (z * z + np.sqrt(z ** 4 + 8.0 * z * z * nbg))
                y = counts.copy()
                y[lo:hi + 1] = y[lo:hi + 1] + rng.poisson(area * prof)
                r, status = gaussfit.fit_peak_ex(y, ch0, sg, window=2.2)
                v = vertex(y, ch0, sg)
                out.append(dict(part=part, fw=float(fw), z_want=z,
                                sig=(float(r['sig']) if r else 0.0),
                                vertex=1 if (v and v['ok']) else 0,
                                vertex_sym=1 if (v and v['ok_sym']) else 0))
    return out, occupied


# ---------------------------------------------------------------------------
# стадия C — ОТРИЦАТЕЛЬНЫЙ контроль мерки вершины (`V17`)
# ---------------------------------------------------------------------------
def stage_c(entries, res_by_det, part_of):
    u"""Подсадка ИЗГИБА: широкий смещённый горб вместо пика модельной ширины.

    ⛔ Стадия B выбирает место там, где признак уже сказал «нет», и потому
    меряет только его ЧУВСТВИТЕЛЬНОСТЬ. Здесь наоборот: в то же пустое место
    сажается структура, которой соответствовать линии НЕ ПОЛОЖЕНО — горб
    шириной BEND_W модельных ПШПВ, центр отодвинут на BEND_SHIFT его ширины,
    так что окно ловит перегиб. Вершины модельной ширины там нет по
    построению, и всякое «вершина есть» — ложное срабатывание.

    Сплошной обход НАСТОЯЩИХ изгибов вместо подсадки не годится: замер
    05.09.2026 нашёл во всём корпусе 2 таких окна у понятной части и 1 у
    непонятной — на сцинтилляторе линии стоят плотнее ПШПВ, и место «без
    единой линии в трёх ПШПВ» просто не встречается. Знаменателя нет — нет и
    доли.
    """
    rng = np.random.RandomState(INJ_SEED + 1)
    out = []
    for e in entries:
        scene, why = load_scene(e, res_by_det)
        if scene is None:
            continue
        counts, cal, res_a, lines, full = scene
        n = len(counts)
        part = part_of.get(e['key'], '?')
        busy = [cal.channel(a[0]) for a in lines]
        picked = 0
        tries = 0
        while picked < BEND_POS and tries < 400:
            tries += 1
            ch0 = float(rng.randint(int(0.05 * n), int(0.90 * n)))
            ee = cal.energy(ch0)
            dedch = abs(cal.dEdch(ch0))
            if ee <= 5 or dedch <= 0:
                continue
            fw = max(res_a * np.sqrt(max(ee, 5.0)) / dedch, 1.2)
            if any(abs(ch0 - b) < 3 * fw for b in busy):
                continue
            sg = fw / FS
            # ⛔ Тот же зазор, что у стадии B (3 ПШПВ), и не больше. Первая
            # постановка требовала девяти ПШПВ с обеих сторон — «чтобы горб
            # влез целиком», — и нашла во всей понятной части ОДНО место на
            # 82 спектра, а доля «0.0 %» рядом с ним выглядела как хорошая
            # новость. Ширина горба тут ни при чём: судится окно шириной
            # ±2.2 сигмы вокруг ch0, а хвосты горба обрезаются краем массива
            # много дальше него.
            if ch0 - 3 * fw < 4 or ch0 + 3 * fw > n - 5:
                continue
            v0 = vertex(counts, ch0, sg)
            if v0 is None or v0['ok']:
                continue                       # тут уже что-то есть — не место
            picked += 1
            for wf in BEND_W:
                bsg = wf * fw / FS
                for shf in BEND_SHIFT:
                    for z in BEND_Z:
                        c = ch0 + shf * fw
                        lo = int(max(0, round(c - 4 * bsg)))
                        hi = int(min(n - 1, round(c + 4 * bsg)))
                        xx = np.arange(lo, hi + 1, dtype=float)
                        prof = np.exp(-0.5 * ((xx - c) / bsg) ** 2)
                        prof = prof / prof.sum()
                        nbg = max(float(np.median(counts[lo:hi + 1])), 0.0) \
                            * sg * np.sqrt(2.0 * np.pi)
                        area = 0.5 * (z * z + np.sqrt(z ** 4 + 8.0 * z * z * nbg))
                        # площадь считана для ПИКА модельной ширины; горб шире
                        # во столько же раз, во сколько шире его сигма
                        y = counts.copy()
                        y[lo:hi + 1] = y[lo:hi + 1] + rng.poisson(area * wf * prof)
                        v = vertex(y, ch0, sg)
                        if v is None:
                            continue
                        out.append(dict(part=part, w=wf, shift=shf, z_want=z,
                                        vertex=1 if v['ok'] else 0,
                                        vertex_sym=1 if v['ok_sym'] else 0))
    return out


def eff_by_bin(inj, part, field='vertex'):
    u"""Эффективность мерки вершины на ЗАВЕДОМО настоящих пиках, по корзинам z."""
    eff = []
    for lo, hi, nm in ZBINS:
        s = [r for r in inj if r['part'] == part and lo <= r['sig'] < hi]
        eff.append((sum(r[field] for r in s) / float(len(s))) if s else None)
    return eff


def canon_check(res_by_det, name):
    u"""Именной отрицательный контроль признака вершины (`V17`).

    Печатает вердикт КАЖДОГО признака поимённо. Возвращает число случаев,
    где ПОСТАВОЧНЫЙ признак разошёлся с записанным ожиданием.
    """
    bad = 0
    print(u'')
    print(u'=== V17. ИМЕННОЙ ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ ПРИЗНАКА ВЕРШИНЫ ===')
    by_key = dict((e['key'], e) for e in corpus_def.ALL)
    for key, e_kev, want in CANON:
        e = by_key.get(key)
        if e is None:
            print(u'  ⛔ %s нет в corpus_def — контроль НЕ ПРОВЕРЕН' % key)
            bad += 1
            continue
        scene, why = load_scene(e, res_by_det)
        if scene is None:
            print(u'  ⛔ %s не читается (%s) — контроль НЕ ПРОВЕРЕН' % (key, why))
            bad += 1
            continue
        counts, cal, res_a, lines, full = scene
        ch0, fw = fwhm_model_ch(cal, res_a, e_kev)
        v = vertex(counts, ch0, fw / FS) if fw else None
        if v is None:
            print(u'  ⛔ %s %.1f кэВ: окно не собралось — контроль НЕ ПРОВЕРЕН'
                  % (key, e_kev))
            bad += 1
            continue
        print(u'  %s %.1f кэВ (канал %.1f, ПШПВ %.2f кан): вершины быть %s; '
              u'симметрия спада %.3f при пороге %.2f'
              % (key, e_kev, ch0, fw, u'ДОЛЖНО' if want else u'НЕ ДОЛЖНО',
                 v['sym'], SYM_MIN))
        for nm in VERTEX_NAMES:
            got = verdict(v, nm)
            mark = u'  ' if got == want else (u'⛔' if nm == name else u'⚠ ')
            print(u'    %s признак «%-14s»%s -> %s%s'
                  % (mark, nm, u' [ПОСТАВОЧНЫЙ]' if nm == name else u'             ',
                     u'вершина ЕСТЬ' if got else u'вершины нет',
                     u'' if got == want else u'   РАСХОЖДЕНИЕ С ОЖИДАНИЕМ'))
            if nm == name and got != want:
                bad += 1
    if bad:
        print(u'  ⛔ поставочный признак «%s» разошёлся с ожиданием в %d случаях '
              u'— доля «с вершиной» им НЕ ИЗМЕРИМА. Код возврата 5.' % (name, bad))
    return bad


# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only', default=None)
    ap.add_argument('--csv', default=None)
    ap.add_argument('--gate', default='prod', choices=('prod', 'pass1'))
    ap.add_argument('--vertex', default=VERTEX_DEFAULT, choices=VERTEX_NAMES)
    ap.add_argument('--clean', type=float, default=CLEAN_FWHM,
                    help=u'`V16`: чистить ложный набор по полному списку '
                         u'излучений, в ПШПВ; 0 — не чистить (прежнее поведение)')
    ap.add_argument('--no-inject', action='store_true')
    ap.add_argument('--coverage-only', action='store_true',
                    help=u'`T76`: считать ТОЛЬКО охват и выйти (код 3 при '
                         u'сломанном охвате); гейт, V16 и V17 не считаются')
    args = ap.parse_args()
    only = set(args.only.split(',')) if args.only else None
    gate = PROD if args.gate == 'prod' else PASS1
    vname = args.vertex
    vfield = 'vertex' if vname == u'пик-3√N' else 'vertex_sym'

    res_by_det = det_res_a()
    part_of = parts_of()
    entries = [e for e in corpus_def.ALL if not only or e['key'] in only]

    rows, agg, skipped, read, lost = stage_a(entries, res_by_det, part_of, gate,
                                             clean=args.clean,
                                             coverage_only=args.coverage_only)

    # ---- охват: правило ОДНО на обе мерки (`T76`) ----------------------------
    # ⛔ `frozen=set()` не украшение: эта мерка читает `corpus/spectra`, где
    # семёрка `LEGACY` лежит наравне со всеми, и «объяснять» её пропажу тут
    # значило бы завести немую дыру ровно того рода, о котором `T76`.
    cov = Coverage(requested=only, frozen=set())
    cov.add(u'прочитано из corpus/spectra', list(read), hard=True)
    broken = cov.report(title=u'ОХВАТ МЕРКИ')
    if skipped:
        print(u'  причины, поимённо (%d):' % len(skipped))
        for k, why in skipped:
            print(u'     %-26s %s' % (k, why))
    if lost['scale'] or lost['window']:
        print(u'⛔ ЛИНИИ ПОТЕРЯНЫ ВНУТРИ МЕРКИ: шкала %d, короткое окно %d. '
              u'Числитель доли «принято» уменьшен, знаменатель — нет.'
              % (lost['scale'], lost['window']))

    if args.coverage_only:
        # ⛔ Коды 4 и 5 отсюда НЕ приходят и приходить не должны: их считают
        # `V16` и `V17`, а они не считались. Читатель обязан знать, что
        # получил ответ только на один из трёх вопросов.
        print(u'')
        print(u'--coverage-only: посчитан ТОЛЬКО охват (код 3). Чистота ложного '
              u'набора (`V16`, код 4) и признак вершины (`V17`, код 5) НЕ '
              u'проверялись — их даёт полный прогон.')
        if broken:
            print(u'⛔ ОХВАТ СЛОМАН: %d. Код возврата 3.' % broken)
            return 3
        return 0

    print(u'')
    print(u'гейт «%s»: tol=%.2f ПШПВ, ширина %.2f…%.2f, min_sig=5'
          % (args.gate, gate['tol_fwhm'], gate['width_lo'], gate['width_hi']))
    print(u'признак вершины (ПОСТАВОЧНЫЙ): «%s»; считаются оба: %s'
          % (vname, u', '.join(u'«%s»' % n for n in VERTEX_NAMES)))

    # ---- `V16`: чист ли отрицательный контроль ------------------------------
    print(u'')
    print(u'=== V16. ЧИСТ ЛИ ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ («сдвиг на ±%.0f ПШПВ») ==='
          % FAKE_OFFSET)
    print(u'сдвиг мерится в ПШПВ ПРИБОРА, и на сцинтилляторе это десятки и '
          u'сотни кэВ — прыжок на СОСЕДНЮЮ настоящую линию, а не из спектра.')
    print(u'%-9s %10s %12s %8s %12s %14s'
          % (u'часть', u'предложено', u'на линии', u'доля', u'В РАБОТЕ',
             u'из них на линии'))
    dirty_bad = 0
    for p in PARTS_ORDER:
        a = agg.get(p)
        if not a or not a['proposed']:
            continue
        frac = float(a['dirty']) / a['proposed']
        # ⛔ Судится НЕ предложенный набор, а тот, которым мерка пользуется:
        # при включённой чистке он чист ПО ПОСТРОЕНИЮ, и загрязнение
        # предложенного — диагностика (это и есть находка `V16`), а не отказ.
        used = a['proposed'] if args.clean <= 0 else a['proposed'] - a['dirty']
        used_dirty = a['dirty'] if args.clean <= 0 else 0
        ufrac = float(used_dirty) / max(used, 1)
        print(u'%-9s %10d %12d %7.1f%% %12d %10d %4.1f%%%s'
              % (p, a['proposed'], a['dirty'], 100.0 * frac, used, used_dirty,
                 100.0 * ufrac,
                 u'   ⛔ НЕ ОТРИЦАТЕЛЬНЫЙ' if ufrac > DIRTY_MAX else u''))
        if ufrac > DIRTY_MAX:
            dirty_bad += 1
    if args.clean <= 0:
        print(u'⛔ --clean=0: ложный набор НЕ ЧИСТИЛСЯ. Позиции выше стоят на '
              u'настоящих линиях пробы, и «ложными» они не являются — числа '
              u'ниже отрицательным контролем НЕ ЯВЛЯЮТСЯ.')
    else:
        print(u'чистка включена: %.2f ПШПВ по полному списку излучений '
              u'(I >= %.1f %%). Оставшиеся позиции ложны ПО ПОСТРОЕНИЮ.'
              % (args.clean, I_MIN_FULL))
        print(u'⚠ колонка «доля» — это и есть находка `V16`: столько '
              u'«заведомо ложных» позиций стояло на НАСТОЯЩЕЙ линии пробы.')
    print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ')

    # ---- 1 + 2: приём настоящих и ложных, со знаменателями ------------------
    print(u'')
    print(u'=== 1+2. ПРИЁМ ГЕЙТА: настоящие линии против ЗАВЕДОМО ЛОЖНЫХ ===')
    print(u'%-9s %4s | %6s %6s %6s | %6s %6s %6s | %6s %6s %6s | %5s'
          % (u'часть', u'спк', u'предл', u'принят', u'доля',
             u'предл', u'принят', u'доля', u'предл', u'принят', u'доля', u'н/л'))
    print(u'%-9s %4s | %-20s | %-20s | %-20s |'
          % ('', '', u'      НАСТОЯЩИЕ', u'        ЛОЖНЫЕ', u'   ЛОЖНЫЕ ВДАЛИ'))
    for p in PARTS_ORDER:
        a = agg.get(p)
        if not a:
            continue
        st = [r for r in rows if r['kind'] == u'штат' and r['part'] == p]
        lz = [r for r in rows if r['kind'] == u'ложь' and r['part'] == p]
        fr = [r for r in lz if r['far']]
        ra = 100.0 * len(st) / max(a['off'], 1)
        rb = 100.0 * len(lz) / max(a['off_f'], 1)
        rc = 100.0 * len(fr) / max(a['off_far'], 1)
        print(u'%-9s %4d | %6d %6d %5.1f%% | %6d %6d %5.1f%% | %6d %6d %5.1f%% | %5s'
              % (p, a['sp'], a['off'], len(st), ra, a['off_f'], len(lz), rb,
                 a['off_far'], len(fr), rc,
                 (u'%.2f' % (ra / rb)) if rb > 0 else u'—'))
        print(u'%-9s %4s | %6s %6d %6s | %6s %6d %6s | %6s %6d %6s |'
              % ('', '', u'', sum(1 for r in st if not r[vfield]), u'',
                 u'', sum(1 for r in lz if not r[vfield]), u'',
                 u'', sum(1 for r in fr if not r[vfield]), u''))
        print(u'%-9s %4s |%s<- БЕЗ ВЕРШИНЫ по признаку «%s»'
              % ('', '', ' ' * 5, vname))
    print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ — числа разных моделей')
    print(u'⚠ «ЛОЖНЫЕ ВДАЛИ» — дальше %.0f ПШПВ от ЛЮБОГО излучения пробы '
          u'(прежде мерилось до курированных опор, `V16`).' % FAR_FWHM)

    # ---- стадия B и разбор доли вершин -------------------------------------
    inj = []
    bnd = []
    occ = [0, 0]
    bend_bad = 0
    if not args.no_inject:
        inj, occ = stage_b(entries, res_by_det, part_of)
        print(u'')
        print(u'=== B. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ МЕРКИ ВЕРШИНЫ ===')
        print(u'подсажено заведомо настоящих пиков: %d в %d местах'
              % (len(inj), len(inj) // max(len(INJ_Z), 1)))
        print(u'мерка сказала «вершина есть» на ПУСТОМ месте до подсадки: '
              u'%d из %d = %.1f%%' % (occ[0], occ[1],
                                      100.0 * occ[0] / max(occ[1], 1)))

        # ---- стадия C: ОТРИЦАТЕЛЬНЫЙ контроль (`V17`) ----------------------
        bnd = stage_c(entries, res_by_det, part_of)
        print(u'')
        print(u'=== C. ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: ПОДСАДКА ИЗГИБА (`V17`) ===')
        print(u'широкий смещённый горб вместо пика модельной ширины; вершины '
              u'модельной ширины там нет по построению, всякое «есть» — ложь')
        ztop = max(BEND_Z)
        print(u'⚠ судится z = %.0f: на нём признак видит НАСТОЯЩИЙ пик в '
              u'89…100 %% случаев (стадия B), значит «да» на широком горбе — '
              u'именно ложь, а не слепота. Остальные z показаны для полноты.'
              % ztop)
        print(u'%-9s %6s %6s %6s %8s %s'
              % (u'часть', u'ширина', u'сдвиг', u'z', u'случаев',
                 u' '.join(u'%15s' % n for n in VERTEX_NAMES)))
        for p in PARTS_ORDER:
            sub = [r for r in bnd if r['part'] == p]
            if not sub:
                continue
            for wf in BEND_W:
                for shf in BEND_SHIFT:
                    for z in BEND_Z:
                        s = [r for r in sub if r['w'] == wf and r['shift'] == shf
                             and r['z_want'] == z]
                        if not s:
                            continue
                        pos = (wf == BEND_W[0] and shf == 0.0 and z == ztop)
                        print(u'%-9s %6.1f %6.2f %6.0f %8d %s%s'
                              % (p, wf, shf, z, len(s),
                                 u' '.join(u'%14.1f%%'
                                           % (100.0 * sum(r[f] for r in s) / len(s))
                                           for f in ('vertex', 'vertex_sym')),
                                 u'  <- ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТАДИИ' if pos
                                 else u''))
            # ⛔ Положительный контроль самой стадии: горб МОДЕЛЬНОЙ ширины по
            # центру окна признак обязан принять. Если он его не видит, нули
            # остальных строк значат «подсадка не доехала», а не «признак чист».
            pos_s = [r for r in sub if r['w'] == BEND_W[0] and r['shift'] == 0.0
                     and r['z_want'] == ztop]
            pos_fr = (float(sum(r[vfield] for r in pos_s)) / len(pos_s)) \
                if pos_s else 0.0
            # ложные срабатывания считаются ТОЛЬКО на изгибах, то есть на
            # горбах ШИРЕ модельного; строка контроля в долю не входит
            bad_s = [r for r in sub if r['w'] > BEND_W[0] and r['z_want'] == ztop]
            fr = (float(sum(r[vfield] for r in bad_s)) / len(bad_s)) \
                if bad_s else 0.0
            cap = BEND_MAX.get(p, BEND_MAX_DEFAULT)
            mark = u'   ⛔ ВЫШЕ ЗАПИСАННОГО %.0f %%' % (100 * cap) \
                if fr > cap else u''
            print(u'%-9s ИТОГ  модельный горб (z=%.0f) принят %.0f %% из %d '
                  u'— контроль стадии, ждём ~100 %%%s'
                  % (p, ztop, 100.0 * pos_fr, len(pos_s),
                     u'   ⛔ СТАДИЯ НЕ РАБОТАЕТ' if pos_fr < 0.5 else u''))
            print(u'%-9s       ложных срабатываний «%s» на ШИРОКИХ горбах '
                  u'(z=%.0f): %.1f %% из %d, записано %.0f %%%s'
                  % ('', vname, ztop, 100.0 * fr, len(bad_s), 100.0 * cap, mark))
            if fr > cap or pos_fr < 0.5:
                bend_bad += 1
        print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ')

    print(u'')
    print(u'=== 1. ДОЛЯ С ВЕРШИНОЙ — и чего она стоит ===')
    print(u'⛔ `V17`: это величина ПРИЗНАКА, а не корпуса. Поэтому колонки две, '
          u'и имя признака стоит в заголовке каждой.')
    print(u'%-9s %-6s %6s | %13s | %13s | %9s %9s'
          % (u'часть', u'набор', u'линий', VERTEX_NAMES[0], VERTEX_NAMES[1],
             u'ОЖИД.если', u'ожид.доля'))
    print(u'%-9s %-6s %6s | %13s | %13s | %9s'
          % ('', '', '', u'верш   доля', u'верш   доля', u'ВСЕ настоящие'))
    for p in PARTS_ORDER:
        st = [r for r in rows if r['kind'] == u'штат' and r['part'] == p]
        if not st:
            continue
        eff = eff_by_bin(inj, p, vfield) if inj else [None] * len(ZBINS)
        for kind, nm in ((u'штат', u'штат'), (u'ложь', u'ложь')):
            sub = [r for r in rows if r['kind'] == kind and r['part'] == p]
            if not sub:
                continue
            o1 = sum(r['vertex'] for r in sub)
            o2 = sum(r['vertex_sym'] for r in sub)
            exp = None
            if any(x is not None for x in eff):
                exp = 0.0
                for (lo, hi, _n), ee in zip(ZBINS, eff):
                    s = [r for r in sub if lo <= r['sig'] < hi]
                    if s and ee is not None:
                        exp += ee * len(s)
            print(u'%-9s %-6s %6d | %5d %6.1f%% | %5d %6.1f%% | %9s %9s'
                  % (p, nm, len(sub), o1, 100.0 * o1 / len(sub),
                     o2, 100.0 * o2 / len(sub),
                     (u'%.1f' % exp) if exp is not None else u'—',
                     (u'%.1f%%' % (100.0 * exp / len(sub))) if exp is not None else u'—'))
        if any(x is not None for x in eff):
            print(u'   эффективность признака «%s» на ПОДСАЖЕННЫХ: %s'
                  % (vname, u', '.join(u'%s %.0f%%' % (n, 100 * ee)
                                       for (_l, _h, n), ee in zip(ZBINS, eff)
                                       if ee is not None)))
    print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ')
    print(u'⚠ «ожидаемое» — сколько вершин мерка нашла бы, будь КАЖДАЯ линия')
    print(u'  набора настоящей. Наблюдение не ниже ожидаемого значит только то,')
    print(u'  что мерка НЕ РАЗЛИЧАЕТ эти наборы, а не что набор чист.')
    print(u'⚠ `V17`: разница двух колонок — цена ПРИЗНАКА, а не свойство корпуса. '
          u'Цитировать долю без имени признака нельзя.')

    canon_bad = canon_check(res_by_det, vname)

    # ---- 3: цена порога -----------------------------------------------------
    print(u'')
    print(u'=== 3. ЦЕНА ПОРОГА ПО A/sigma(A) (`sig_fit`) ===')
    print(u'%-9s %5s | %6s %6s %6s %6s | %6s %6s | %5s | %4s %4s'
          % (u'часть', u'порог', u'линий', u'доля', u'верш', u'потер',
             u'ложных', u'доля', u'н/л', u'спк', u'<3'))
    for p in PARTS_ORDER:
        a = agg.get(p)
        st = [r for r in rows if r['kind'] == u'штат' and r['part'] == p]
        lz = [r for r in rows if r['kind'] == u'ложь' and r['part'] == p]
        if not st or not a:
            continue
        base_v = sum(r[vfield] for r in st)
        keys = set(r['key'] for r in st)
        for thr in (0.0, 3.0, 8.0):
            k1 = [r for r in st if r['sig_fit'] >= thr]
            k2 = [r for r in lz if r['sig_fit'] >= thr]
            v = sum(r[vfield] for r in k1)
            ra = 100.0 * len(k1) / max(a['off'], 1)
            rb = 100.0 * len(k2) / max(a['off_f'], 1)
            cnt = {}
            for r in k1:
                cnt[r['key']] = cnt.get(r['key'], 0) + 1
            print(u'%-9s %5.0f | %6d %5.1f%% %6d %6d | %6d %5.1f%% | %5s | %4d %4d'
                  % (p, thr, len(k1), ra, v, base_v - v, len(k2), rb,
                     (u'%.2f' % (ra / rb)) if rb > 0 else u'—', len(cnt),
                     sum(1 for k in keys if cnt.get(k, 0) < 3)))
    print(u'⛔ части НЕ СКЛАДЫВАЮТСЯ')
    print(u'  «верш» — сколько из оставшихся имеют вершину ПО ПРИЗНАКУ «%s»;'
          % vname)
    print(u'  «потер» — сколько линий С ВЕРШИНОЙ порог выбросил; «спк» —')
    print(u'  спектров хоть с одной линией; «<3» — спектров, у которых линий')
    print(u'  меньше трёх (на трёх точках квадратичную энергокалибровку уже не')
    print(u'  построить).')

    if args.csv:
        with io.open(args.csv, 'w', encoding='utf-8', newline='') as f:
            w = csv.writer(f)
            cols = ['key', 'det', 'part', 'kind', 'e_ref', 'label', 'ch', 'sig',
                    'sig_fit', 'fwhm', 'fwhm_model', 'vertex', 'vertex_sym',
                    'sym', 'monotone', 'inner', 'far']
            w.writerow(cols)
            for r in rows:
                w.writerow([r[c] for c in cols])
        print(u'\nподробности: %s' % args.csv)

    # ---- сторожа, у каждого читатель — код возврата -------------------------
    print(u'')
    if lost['scale'] or lost['window']:
        broken += lost['scale'] + lost['window']
    if broken:
        print(u'⛔ ОХВАТ СЛОМАН: %d единиц входа не посчитано (`T76`). '
              u'Код возврата 3.' % broken)
        return 3
    if dirty_bad:
        print(u'⛔ ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ ЗАГРЯЗНЁН настоящими линиями в %d '
              u'частях (`V16`). Код возврата 4.' % dirty_bad)
        return 4
    if bend_bad or canon_bad:
        print(u'⛔ ПРИЗНАК ВЕРШИНЫ «%s» ЛОВИТ ИЗГИБ: подсадка %d частей, именной '
              u'контроль %d случаев (`V17`). Код возврата 5.'
              % (vname, bend_bad, canon_bad))
        return 5
    print(u'✓ сторожа молчат: охват полон, ложный набор чист, признак «%s» '
          u'изгиб не ловит.' % vname)
    return 0


if __name__ == '__main__':
    sys.exit(main())
