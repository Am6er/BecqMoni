# -*- coding: utf-8 -*-
u"""Модель разрешения В СПЕКТРАХ корпуса: перенести честно и сменить форму (`V2`).

Здесь два РАЗНЫХ изменения, и они нарочно разведены по режимам, потому что
меряются отдельно.

──────────────────────────────────────────────────────────────────────────────
1. `--mode=proj` — ПЕРЕНОС модели группы в каналы. Это ПОЧИНКА, не улучшение.

Модель группы живёт в кэвах (`corpus/detectors.csv`), а спектр хранит её в
каналах: ПШПВ[ch] = √(c0 + c1·ch + c2·ch²). Перевод делает
`corpus_calib.fwhm_channel_coef` — считает целевую кривую на сетке каналов и
кладёт на неё многочлен ОБЫЧНЫМ МНК по ПШПВ².

Обычный МНК меряет невязку в кэвах, а не в долях, поэтому всю точность тратит
на верх шкалы, где ПШПВ большая. Свобода многочлена уходит в `c0`, и внизу
остаётся ПОЛКА √c0. Измерено на нынешнем корпусе (129 спектров, отношение
модели В ФАЙЛЕ к модели ЕЁ ГРУППЫ на 59.5 кэВ):

    90-й процентиль отклонения   100 %      худший `OBS_UGlass`   186 %
    `AS80_Lu176`  c0 = 13015 каналов² — полка 41.5 кэВ на пустом месте
    `GS4000_U`    43.8 кэВ против 15.4 у группы
    `G1S16_Am241_P5` 18.3 против 12.8

То есть у 36 спектров разбор считает по модели, которая ВНИЗУ ШКАЛЫ шире
собственной модели группы в 1.3…2.9 раза, и это не форма и не физика, а вес в
подгонке. Лечится одной строкой — весом 1/ПШПВ², то есть невязкой В ДОЛЯХ:
90-й процентиль 100 % → 2 %, худший 186 % → 32 %.

⚠ Сетка при этом обязана начинаться там, где энергия ФИЗИЧНА. У калибровок со
свободным членом −10…−27 кэВ нижние каналы дают отрицательную энергию, модель
там возвращает ноль, и относительный вес 1/ПШПВ² ловит этот ноль как самую
важную точку. Первый замер именно так и развалился — все варианты дали 0.00.

──────────────────────────────────────────────────────────────────────────────
2. `--mode=power` — ФОРМА модели группы: ПШПВ = a·E^p вместо √(c1E + c2E²).

Это `V2` по существу: `res_low.py` измерил, что нынешняя форма внизу шкалы шире
настоящей (медиана изм/мод 0.87, у `G1S16` 0.73), `res_form.py` подобрал
степенную. Здесь она берётся по тем же точкам и теми же правилами приёмки:
форма физична (0 < p < 1: ПШПВ растёт, ОТНОСИТЕЛЬНАЯ падает), низ стал лучше,
верх не испортился.

⛔ **Прежнее «формат не держит степенную» относится к подгонке В КЭВАХ и там
верно, а в КАНАЛАХ — нет.** Разница в том, где приближают. Квадратичная по E,
приближающая a·E^p при p > 0.5, обязана иметь c0 < 0 и зануляется на 90…200
кэВ — посреди рабочего диапазона. Та же квадратичная ПО КАНАЛАМ, положенная с
относительным весом на физичной части шкалы, зануляется НИЖЕ 7…14 кэВ, то есть
ниже рабочего низа (15…16 кэВ), и внутри диапазона держит степенную с медианой
1.07 на 60 кэВ (90-й процентиль 9 %). Проверено на всех 100 спектрах семи
переключаемых групп.

3. `--mode=power-node` — та же степенная, но записанная СВОИМ узлом
`<PowerFwhmCalibration>` (кривая заведена в приложении 16.08.2026). Внизу
точнее (медиана 0.98 против 1.07), вверху хуже (1.05 против 1.01 на 2614).
Режим существует, чтобы это была измеренная разница, а не предпочтение.

──────────────────────────────────────────────────────────────────────────────
⛔ Настоящий корпус НЕ ТРОГАЕТ никогда: пишет только в каталог `--out`, в
библиотеку сопровождающего не лезет (работает по копиям, как `res_low.py`).

⚠ Конфигурации приборов (`corpus/devices/`) КОПИРУЮТСЯ КАК ЕСТЬ. `FWHM_AT_0`,
`Width_Fwhm` и `Ch_Concat` пересборка считает из той же модели, но одно из них
правлено вручную (`E13`), и пересчёт снёс бы правку, а замер получил бы два
изменения вместо одного. Если модель принимается — их пересчитает пересборка.

    python tools/CORPUS/scripts/res_apply.py --mode=proj  --out=.../wd_res_proj
    python tools/CORPUS/scripts/res_apply.py --mode=power --out=.../wd_res_power
    python tools/CORPUS/scripts/res_apply.py --mode=power --dry
"""
import argparse
import csv
import io
import os
import shutil
import sys
import xml.etree.ElementTree as ET

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import corpus_calib
from res_low import measured_points
from spectrum import Spectrum

CORPUS = os.path.abspath(os.path.join(HERE, os.pardir, 'corpus'))

#: Ниже этой энергии модель никем не спрашивается (самая низкая линия корпуса —
#: 26.3 кэВ у Am-241), а сетка подгонки там уже вырождена: у калибровок со
#: свободным членом −10…−27 кэВ первые каналы дают отрицательную энергию.
E_MIN = 15.0

#: А приёмка записанной кривой мерится от 40 кэВ. Ниже неё ни одна линия корпуса
#: не живёт (самая низкая — 26.3 кэВ у Am-241, и она ниже порога всех приборов
#: кроме германия), обе формы там уже экстраполяция, и требовать от них согласия
#: значило бы вето по участку, о котором никто не спрашивает.
E_CARE = 40.0

#: И сверху тоже: выше 2700 кэВ у корпуса нет ни одной линии (самая высокая —
#: 2614 кэВ у Tl-208). Предел нужен потому, что модели ДВУХ групп там уже
#: разворачиваются вниз: у `AS80x80` ПШПВ² = 5.548E − 9.9975e-4·E² достигает
#: максимума на 2774 кэВ, а всякая форма, обязанная расти, за ним обязана и
#: разойтись с ней. Это дефект модели группы на неизмеренном участке, а не
#: переноса, и вето по нему было бы вето по пустому месту.
E_CARE_HI = 2700.0


# ---------------------------------------------------------------------------
# форма модели группы
# ---------------------------------------------------------------------------
def fit_power_kev(e, f, w):
    A = np.vstack([np.ones_like(e), np.log(e)]).T * w[:, None]
    c, *_ = np.linalg.lstsq(A, np.log(f) * w, rcond=None)
    return float(np.exp(c[0])), float(c[1])


def rms(e, f, fn, mask):
    if mask.sum() == 0:
        return float('nan')
    d = (f[mask] - fn(e[mask])) / f[mask]
    return float(np.sqrt(np.mean(d ** 2)))


def stored_models():
    out = {}
    with io.open(os.path.join(CORPUS, 'detectors.csv'),
                 encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            out[list(r.values())[0]] = [float(r['res_c0']), float(r['res_c1']),
                                        float(r['res_c2'])]
    return out


def manifest_rows():
    rows = []
    with io.open(os.path.join(CORPUS, 'manifest.csv'),
                 encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            r['key'] = list(r.values())[0]
            rows.append(r)
    return rows


def decide(points, split, min_points, min_low):
    u"""Каким группам менять форму. Возвращает {группа: (a, p, отчёт)}."""
    stored = stored_models()
    by = {}
    for a in points:
        by.setdefault(a['det'], []).append(a)

    verdict = {}
    for det in sorted(by):
        rows = by[det]
        e = np.array([r['energy'] for r in rows])
        f = np.array([r['fwhm'] for r in rows])
        w = np.sqrt(np.clip(np.array([r['sig'] for r in rows]), 1.0, 1e4))
        low = e < split
        rep = dict(det=det, n=len(e), n_low=int(low.sum()))

        if det not in stored:
            rep['why'] = u'нет в detectors.csv'
        elif len(e) < min_points or low.sum() < min_low:
            rep['why'] = u'мало точек'
        else:
            old_fn = corpus_calib.resolution_fn(stored[det])
            old = lambda x: np.array([old_fn(v) for v in np.atleast_1d(x)])
            a, p = fit_power_kev(e, f, w)
            new = lambda x: a * np.asarray(x, dtype=float) ** p
            rep.update(a=a, p=p,
                       old_low=rms(e, f, old, low), old_high=rms(e, f, old, ~low),
                       new_low=rms(e, f, new, low), new_high=rms(e, f, new, ~low))
            if not (0.0 < p < 1.0) or not (a > 0.0):
                rep['why'] = u'нефизична (p = %.3f)' % p
            elif not (rep['new_low'] < rep['old_low']):
                rep['why'] = u'низ не лучше'
            elif rep['new_high'] > rep['old_high'] * 1.15 + 0.005:
                rep['why'] = u'верх портится'
            else:
                rep['why'] = u'ПЕРЕКЛЮЧАЕМ'
                verdict[det] = (a, p, rep)
                continue
        verdict[det] = (None, None, rep)
    return verdict


# ---------------------------------------------------------------------------
# перенос в каналы
# ---------------------------------------------------------------------------
def working_grid(ecal, nmax, count=400):
    u"""Каналы, на которых модель вообще имеет смысл: E ≥ E_MIN и шкала растёт."""
    ch = np.linspace(1.0, nmax - 1.0, 4 * count)
    e = np.array([ecal.energy(c) for c in ch])
    sl = np.array([abs(ecal.dEdch(c)) for c in ch])
    ok = (e >= E_MIN) & (sl > 1e-9)
    if ok.sum() < 20:
        return None
    return np.linspace(float(ch[ok][0]), float(ch[ok][-1]), count)


def quad_channel_coef(ecal, res_fn, nmax):
    u"""(c0, c1, c2) для ПШПВ[ch] = √(c0 + c1·ch + c2·ch²), невязка ОТНОСИТЕЛЬНАЯ.

    Отличие от `corpus_calib.fwhm_channel_coef` ровно одно — вес 1/ПШПВ².
    Проверки те же: кривая положительна и растёт на рабочей шкале.
    """
    ch = working_grid(ecal, nmax)
    if ch is None:
        return None, u'шкала вырождена'
    fw = np.array([res_fn(ecal.energy(c)) / max(abs(ecal.dEdch(c)), 1e-9) for c in ch])
    if not np.all(fw > 0):
        return None, u'модель даёт неположительную ширину'
    y = fw ** 2
    w = 1.0 / y
    for order in (2, 1):
        A = np.vstack([ch ** i for i in range(order + 1)]).T
        coef, *_ = np.linalg.lstsq(A * w[:, None], y * w, rcond=None)
        coef = np.concatenate([coef, np.zeros(3 - len(coef))])
        v = coef[0] + coef[1] * ch + coef[2] * ch ** 2
        if np.all(v > 0) and np.all(np.diff(v) > 0):
            return np.array(coef, dtype=float), None
    k = float((y * ch * w).sum() / max((ch * ch * w).sum(), 1e-9))
    return np.array([0.0, k, 0.0]), u'запасная однопараметрическая'


def power_channel_coef(ecal, res_fn, nmax):
    u"""(a, p) для ПШПВ[ch] = a·ch^p — узел `PowerFwhmCalibration`."""
    ch = working_grid(ecal, nmax)
    if ch is None:
        return None, u'шкала вырождена'
    fw = np.array([res_fn(ecal.energy(c)) / max(abs(ecal.dEdch(c)), 1e-9) for c in ch])
    if not np.all(fw > 0):
        return None, u'модель даёт неположительную ширину'
    A = np.vstack([np.ones(len(ch)), np.log(ch)]).T
    c, *_ = np.linalg.lstsq(A, np.log(fw), rcond=None)
    a, p = float(np.exp(c[0])), float(c[1])
    if not (a > 0.0) or not (0.0 < p < 1.0):
        return None, u'нефизична в каналах (p = %.3f)' % p
    return (a, p), None


def fidelity(ecal, res_fn, kind, coef, nmax):
    u"""Худшее отклонение записанной кривой от модели группы ВЫШЕ `E_CARE`.

    Возвращает (доля, энергия худшей точки) — энергия нужна, чтобы отказ можно
    было прочитать, а не только увидеть.
    """
    ch = working_grid(ecal, nmax, count=200)
    if ch is None:
        return float('inf'), 0.0
    e = np.array([ecal.energy(c) for c in ch])
    keep = (e >= E_CARE) & (e <= E_CARE_HI)
    if keep.sum() < 10:
        return float('inf'), 0.0
    ch, e = ch[keep], e[keep]
    want = np.array([res_fn(v) for v in e])

    # ⚠ Мерить согласие там, где модель ГРУППЫ разворачивается вниз, нельзя.
    # У `AS80x80` и `GS4000` она записана как ПШПВ² = c1·E + c2·E² с c2 < 0 и
    # достигает максимума на 2774 кэВ; выше него ширина у неё ПАДАЕТ, а всякая
    # форма, обязанная расти, обязана там с ней и разойтись. Первый заход этого
    # не учитывал, и два спектра — `OBS_UGlass` и `AS80_K40` — были завёрнуты
    # расхождением на 2690 кэВ, то есть остались с ПРЕЖНЕЙ моделью, вравшей
    # внизу шкалы в 2.9 раза. Размен неверный: наверху спорят две экстраполяции,
    # внизу — модель и измерение.
    grow = np.ones(len(want), dtype=bool)
    falling = np.nonzero(np.diff(want) <= 0.0)[0]
    if falling.size:
        grow[falling[0] + 1:] = False
    if grow.sum() >= 10:
        ch, e, want = ch[grow], e[grow], want[grow]
    sl = np.array([abs(ecal.dEdch(c)) for c in ch])
    if kind == 'quad':
        got = np.sqrt(np.maximum(coef[0] + coef[1] * ch + coef[2] * ch ** 2, 1e-12)) * sl
    else:
        got = coef[0] * ch ** coef[1] * sl
    rel = np.abs(got - want) / np.maximum(want, 1e-9)
    i = int(np.argmax(rel))
    return float(rel[i]), float(e[i])


# ---------------------------------------------------------------------------
# запись
# ---------------------------------------------------------------------------
def rewrite(src, dest, kind, coef):
    u"""Заменить коэффициенты (и при надобности тип) узла кривой ПШПВ.

    ⛔ Разбором XML, а не регэкспом: `<Efficiency>` уже стоил двух битых файлов.
    Узел переиспользуется целиком — тип пика, хвосты и χ² принадлежат спектру,
    а не форме кривой, и молчаливая их потеря изменила бы форму пика.
    """
    tree = ET.parse(src)
    rd = tree.getroot().find('ResultDataList/ResultData')
    if rd is None:
        return u'нет ResultData'
    node = rd.find('SqrtFwhmCalibration')
    if node is None:
        node = rd.find('PowerFwhmCalibration')
    if node is None:
        return u'нет узла кривой ПШПВ'

    # ⛔ Чужие узлы снять. Видов кривой три, а поле в модели одно: у трёх
    # спектров корпуса рядом со своим узлом лежал `SimpleSqrtFwhmCalibration`
    # из исходного файла, и разбор брал ЕГО — у `AS80_Cs137_0cm` ядро поиска
    # выходило 163 канала вместо 243. Своим считается тот, что пишет корпус.
    for name in ('SimpleSqrtFwhmCalibration', 'SqrtFwhmCalibration',
                 'PowerFwhmCalibration'):
        for other in rd.findall(name):
            if other is not node:
                rd.remove(other)
    node.tag = 'SqrtFwhmCalibration' if kind == 'quad' else 'PowerFwhmCalibration'
    cs = node.find('Coefficients')
    if cs is None:
        cs = ET.SubElement(node, 'Coefficients')
    for child in list(cs):
        cs.remove(child)
    for v in coef:
        ET.SubElement(cs, 'Coefficient').text = repr(float(v))
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    write_tree(tree, dest)
    return None


def write_tree(tree, dest, tries=5, pause=0.4):
    u"""Записать XML, переждав чужой перехват файла (`A74`).

    ⛔ Зачем. Пересборка пишет 129 файлов подряд, и на Windows одиночная запись
    изредка отказывает `OSError [Errno 22] Invalid argument` — файл в этот
    момент держит кто-то ещё (антивирус, индексатор, чужая приёмка, читающая
    те же спектры). Отказ случайный: 02.09.2026 он свалил две пересборки подряд
    на РАЗНЫХ файлах и РАЗНЫХ шагах, а тесты на 60 и 25 записях его не
    воспроизвели ни разу.

    Цена отказа несоразмерна причине: шаг падает целиком, корпус остаётся
    НЕДОСОБРАННЫМ (узлы стёрты, новые не положены), и это состояние выглядит
    как готовый корпус — 129 файлов на месте и свежие.

    ⚠ Повтор — не «глушение ошибки»: последняя попытка бросает исключение
    как прежде, то есть настоящая беда (нет прав, нет места, битый путь)
    по-прежнему валит шаг и не притворяется успехом.
    """
    import time
    for attempt in range(tries):
        try:
            tree.write(dest, encoding='utf-8', xml_declaration=True)
            return attempt
        except OSError:
            if attempt == tries - 1:
                raise
            time.sleep(pause * (attempt + 1))
    return 0


#: Виды узла кривой ПШПВ. Их ТРИ, а поле в модели одно (`B18`), поэтому и
#: читать надо все три: спектр, у которого рядом со своим лежит чужой,
#: разбирается ЧУЖИМ, и это уже стоило одного разъехавшегося ядра поиска.
FWHM_NODES = ('SimpleSqrtFwhmCalibration', 'SqrtFwhmCalibration',
              'PowerFwhmCalibration')

#: Какому тегу отвечает какой вид кривой в плане.
NODE_KIND = {'SqrtFwhmCalibration': 'quad', 'PowerFwhmCalibration': 'power'}


def stored_nodes(path):
    u"""Какие узлы кривой ПШПВ лежат В ФАЙЛЕ: [(тег, [коэффициенты]), …].

    Разбором XML, а не подстрокой: тег `<Efficiency>` уже показал, чем кончается
    поиск подстрокой в этих файлах (35 совпадений на один узел).
    """
    rd = ET.parse(path).getroot().find('ResultDataList/ResultData')
    if rd is None:
        return []
    out = []
    for name in FWHM_NODES:
        for node in rd.findall(name):
            out.append((name, [float(x.text) for x in
                               node.findall('Coefficients/Coefficient')]))
    return out


def plan(mode='power-node', only=None, split=200.0, min_points=6, min_low=2,
         tol=0.35, points=None):
    u"""Что и КАКИМ узлом получит каждый спектр — не написав ни байта.

    Возвращает `(строки, switched, verdict)`; строка — словарь с `key`, `det`,
    `kind` (`'power'` / `'quad'` / `None`, если спектр остаётся как был),
    `coef`, `dev`, `kept` (почему оставлен) и `note` (почему записан вопреки
    пределу).

    ⛔ Вынесено из `main` НАРОЧНО, и не ради красоты: этим же планом сторож
    приёмки (`check_corpus.check_fwhm_node`) проверяет, что третий шаг
    пересборки не пропущен (`T61`). Отдельно написанный «список, кому положен
    степенной узел» разошёлся бы с настоящей записью на первом же особом
    случае — а их здесь три: вырожденная шкала, нефизичная степень в каналах и
    приёмка «вне предела, но прежняя хуже». Сторож, который сверяет не с тем,
    что пишется, — тот же `D27`: поверка, не смотрящая на изменяемое.
    """
    stored = stored_models()
    switched, verdict = {}, {}
    if mode != 'proj':
        pts = measured_points() if points is None else points
        pts = [p for p in pts if only is None or p['det'] in only]
        verdict = decide(pts, split, min_points, min_low)
        for det, (a, p, _rep) in verdict.items():
            if a is not None:
                switched[det] = (a, p)

    rows = []
    for row in manifest_rows():
        det, key = row['det'], row['key']
        src = os.path.join(CORPUS, 'spectra', key + '.xml')
        if det not in stored or not os.path.isfile(src):
            continue
        if only is not None and det not in only:
            continue
        sp = Spectrum(src)
        ecal = corpus_calib.Ecal(sp.ecal, sp.n)

        if det in switched:
            a, p = switched[det]
            res_fn = lambda e, a=a, p=p: a * max(float(e), 1e-9) ** p
        else:
            res_fn = corpus_calib.resolution_fn(stored[det])

        if mode == 'power-node' and det in switched:
            coef, why = power_channel_coef(ecal, res_fn, sp.n)
            kind = 'power'
        else:
            coef, why = quad_channel_coef(ecal, res_fn, sp.n)
            kind = 'quad'

        rec = dict(key=key, det=det, src=src, kind=kind, coef=coef,
                   dev=None, e_worst=None, kept=None, note=None)
        if coef is None:
            rec.update(kind=None, kept=why)
            rows.append(rec)
            continue

        dev, e_worst = fidelity(ecal, res_fn, kind, coef, sp.n)
        rec.update(dev=dev, e_worst=e_worst)

        # Приёмка — «в предел ИЛИ не хуже той, что стоит», а не голый предел.
        # Голый предел завернул `OBS_UGlass` и `AS80_K40` расхождением 35 % на
        # 2690 кэВ — и оставил в них ПРЕЖНЮЮ кривую, которая на 60 кэВ врёт на
        # 186 % и 92 %. Кривую надо мерить против кривой, а не против числа:
        # прежняя считается тем же прежним правилом от той же модели группы,
        # то есть это ровно то, что лежит в файле.
        if dev > tol:
            old_fn = corpus_calib.resolution_fn(stored[det])
            old_coef = corpus_calib.fwhm_channel_coef(ecal, old_fn, sp.n)
            old_dev, old_e = fidelity(ecal, old_fn, 'quad', old_coef, sp.n)
            if dev > old_dev:
                rec.update(kind=None,
                           kept=u'отклонение %.0f %% на %.0f кэВ, у прежней %.0f %% на %.0f кэВ'
                                % (100 * dev, e_worst, 100 * old_dev, old_e))
                rows.append(rec)
                continue
            rec['note'] = (u'вне предела (%.0f %% на %.0f кэВ), но прежняя хуже'
                           u' (%.0f %% на %.0f кэВ) — пишем'
                           % (100 * dev, e_worst, 100 * old_dev, old_e))
        rows.append(rec)
    return rows, switched, verdict


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', default='proj', choices=('proj', 'power', 'power-node'))
    ap.add_argument('--out', default=None, help=u'каталог копии корпуса')
    ap.add_argument('--dets', default=None, help=u'только эти группы')
    ap.add_argument('--split', type=float, default=200.0, help=u'граница низа, кэВ')
    ap.add_argument('--min-points', type=int, default=6)
    ap.add_argument('--min-low', type=int, default=2)
    ap.add_argument('--tol', type=float, default=0.35,
                    help=u'предел отклонения записанной кривой от модели группы')
    ap.add_argument('--dry', action='store_true')
    ap.add_argument('--apply', action='store_true',
                    help=u'писать в САМ корпус (только с разрешения Amber)')
    args = ap.parse_args()
    only = set(args.dets.split(',')) if args.dets else None
    if args.apply and args.out:
        print(u'⛔ --apply и --out вместе не имеют смысла: либо в корпус, либо в копию')
        return
    if args.apply:
        # Корпус в коммите, и он же база всех чисел. Запись сюда — смена базы,
        # то есть решение Amber, а не агента: ключ отдельный и в конвейере не
        # стоит. Замер делается копией (`--out`), и только принятое пишется.
        args.out = CORPUS

    print(u'модель разрешения в спектрах корпуса, режим %s (V2)' % args.mode)

    rows, switched, verdict = plan(args.mode, only, args.split,
                                   args.min_points, args.min_low, args.tol)
    if args.mode != 'proj':
        print()
        print(u'%-10s %5s %4s %8s %6s %8s %8s %8s %8s  %s'
              % (u'группа', u'точек', u'низ', u'a', u'p', u'СКО низ', u'→ низ',
                 u'СКО верх', u'→ верх', u'решение'))
        for det in sorted(verdict):
            _a, _p, rep = verdict[det]
            if 'a' not in rep:
                print(u'%-10s %5d %4d %8s %6s %8s %8s %8s %8s  %s'
                      % (det, rep['n'], rep['n_low'], u'—', u'—', u'—', u'—',
                         u'—', u'—', rep['why']))
            else:
                print(u'%-10s %5d %4d %8.4f %6.3f %8.3f %8.3f %8.3f %8.3f  %s'
                      % (det, rep['n'], rep['n_low'], rep['a'], rep['p'],
                         rep['old_low'], rep['new_low'], rep['old_high'],
                         rep['new_high'], rep['why']))
        print()
        print(u'форму меняем у групп: %d' % len(switched))

    if args.dry or not args.out:
        if not args.dry:
            print(u'⚠ каталог не задан (--out=…) — ничего не записано')
        return

    out = os.path.abspath(args.out)
    if out == CORPUS and not args.apply:
        print(u'⛔ --out совпадает с настоящим корпусом — отказ, для записи есть --apply')
        return
    if args.apply:
        print(u'⚠ пишу В САМ КОРПУС: %s' % out)
    else:
        if os.path.isdir(out):
            shutil.rmtree(out)
        print(u'копирую корпус в %s' % out)
        shutil.copytree(CORPUS, out)

    done, kept, worst = 0, [], (0.0, '', 0.0)
    for rec in rows:
        key = rec['key']
        if rec['kept']:
            kept.append((key, rec['kept']))
            continue
        if rec['note']:
            print(u'   %-24s %s' % (key, rec['note']))
        if rec['dev'] > worst[0]:
            worst = (rec['dev'], key, rec['e_worst'])

        err = rewrite(rec['src'], os.path.join(out, 'spectra', key + '.xml'),
                      rec['kind'], rec['coef'])
        if err:
            kept.append((key, err))
            continue
        done += 1

    print(u'записано спектров: %d' % done)
    print(u'худшее отклонение записанной кривой от модели группы: %.0f %% (%s, %.0f кэВ)'
          % (100 * worst[0], worst[1], worst[2]))
    if kept:
        print(u'оставлены как были: %d' % len(kept))
        for key, why in kept:
            print(u'   %-24s %s' % (key, why))


if __name__ == '__main__':
    main()
