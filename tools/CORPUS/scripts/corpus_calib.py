# -*- coding: utf-8 -*-
"""Калибровка спектра корпуса: разрешение из самих данных, потом энергия.

Отличие от calibrate.py, который делался под девять спектров трёх похожих
сцинтилляторов: там разрешение задавалось затравкой r662 = 7 %, и от неё
зависело окно поиска, ширинный фильтр и в конце концов то, какие линии вообще
будут приняты. Корпус охватывает от 0.2 % (HPGe) до 10 % (RadiaCode-101), и
затравку взять неоткуда — поэтому здесь разрешение сначала МЕРЯЕТСЯ поиском
неподвижной точки по лесенке пробных ширин, без всякого априори, и только потом
ищутся линии.

Второе отличие — как выбирается калибровка. Кандидаты (оставить хранившуюся,
поправить усиление, перефитить полином) сравниваются на ОДНОМ И ТОМ ЖЕ наборе
пар (канал, табличная энергия), найденном один раз: положение пика от кандидата
не зависит, зависит только невязка. Иначе поправка, подогнанная по одной линии,
садится на неё точно и с нулевой невязкой выигрывает у честного полинома по
семи линиям.
"""
import numpy as np

import gaussfit
from gaussfit import fit_peak, fit_peak_ex, FWHM_SIGMA

#: `V13`: линии, на которых фит ОТКАЗАЛ при последнем `match_lines` —
#: РАЗБЕГ, а не отсутствие линии: ни один шаг не принят либо `lstsq` отказал.
#: ⛔ До 25.08.2026 «фит не сошёлся» и «линии нет» были неразличимы: `gaussfit`
#: возвращал `None` в обоих случаях, а `match_lines` молча пропускал линию —
#: так корпус терял свои сильнейшие опоры. После починки демпфера этот список
#: пуст по всему корпусу (5936 фитов), и вот это и есть сторож: наполнился —
#: значит фит опять разбегается. Читатели: `check_corpus.check`,
#: `gaussfit_check.py`.
LAST_NOCONV = []

#: `V13`: линии, где минимум лежит ЗА пределом σ. Это НЕ разбег: чаще всего
#: линии в окне просто нет, гауссиана расползается по континууму и упирается в
#: предел. Держится отдельно от `LAST_NOCONV` именно поэтому — смешав их,
#: сторож разбега тонет в восьми «отказах» на спектр.
LAST_BOUND = []


class Ecal(object):
    def __init__(self, coef, nmax):
        self.coef = np.asarray(coef, dtype=float)
        self.nmax = nmax
        self._grid = np.arange(0, nmax, dtype=float)
        self._e = self.energy(self._grid)

    def energy(self, ch):
        ch = np.asarray(ch, dtype=float)
        return sum(c * ch ** i for i, c in enumerate(self.coef))

    def channel(self, e):
        return float(np.interp(e, self._e, self._grid))

    def dEdch(self, ch):
        return float(sum(i * c * float(ch) ** (i - 1)
                         for i, c in enumerate(self.coef) if i >= 1))

    def monotone(self):
        return bool(np.all(np.diff(self._e) > 0))


# ---------------------------------------------------------------------------
# 1. разрешение
# ---------------------------------------------------------------------------
MIN_SIG_WIDTH = 12.0


def probe_width(counts, ch0, nmax):
    """Ширина пика у канала ch0 без всякого априори — поиском неподвижной точки.

    Гауссиану фитим при целой лесенке пробных ширин и берём ту, при которой
    подогнанная ширина совпала с пробной. Половина высоты на континуум-вычтенном
    спектре, которой это делалось раньше, на фоновых спектрах цеплялась за
    остаточную рябь SNIP и давала полуширину в десять каналов там, где пик
    занимает сотню — после чего окно поиска линий сжималось и не находилось уже
    ничего.
    """
    best = None
    w = 1.5
    while w < nmax / 6.0:
        w0, w = w, w * 1.35
        r = fit_peak(counts, ch0, w0 / FWHM_SIGMA, window=2.2)
        if r is None or r['sig'] < MIN_SIG_WIDTH:
            continue
        wf = r['fwhm']
        if wf <= 2.5 or abs(r['mu'] - ch0) > 0.7 * w0:
            continue
        err = abs(np.log(wf / w0))
        if err > 0.4:                       # подогнанная ширина не похожа на пробную
            continue
        if best is None or err < best[0]:
            best = (err, wf, r['sig'])
    return None if best is None else (best[1], best[2])


def measure_resolution(counts, ecal, energies, default=0.065, pct=30.0):
    """FWHM(E) = a*sqrt(E); возвращает a и относительную ширину на 662 кэВ.

    Берётся не медиана приведённых ширин, а нижний квантиль: примесь соседней
    линии ширину только УВЕЛИЧИВАЕТ, уменьшить её ничто не может, поэтому
    оценку задают самые узкие из уверенно измеренных пиков. Медиана на
    1024-канальных приборах, где половина списка — бленды, давала 13-15 % на
    662 кэВ вместо настоящих 7-8 %, а отбор одиночных линий по чистоте тут не
    помогает: чистота сама считается по разрешению, и итерация разбегалась.
    """
    n = len(counts)
    e_top = ecal.energy(n - 3)
    pts = []
    for e0 in energies:
        if e0 < 25.0 or e0 > e_top:
            continue
        ch0 = ecal.channel(e0)
        if ch0 < 4 or ch0 > n - 5:
            continue
        r = probe_width(counts, ch0, n)
        if r is None:
            continue
        w_ch, sig = r
        w_kev = w_ch * abs(ecal.dEdch(ch0))
        if w_kev <= 0 or w_kev > 0.35 * e0:
            continue
        pts.append((sig, e0, w_kev))
    if not pts:
        return default * np.sqrt(662.0), default
    red = [w / np.sqrt(e) for _, e, w in pts]
    a = float(np.percentile(red, pct) if len(red) >= 3 else min(red))
    a = float(np.clip(a, 0.001 * np.sqrt(662.0), 6.0))
    return a, a / np.sqrt(662.0)


# ---------------------------------------------------------------------------
# 2. сопоставление линий
# ---------------------------------------------------------------------------
def match_lines(counts, ecal, lines, res_a, tol_fwhm=1.5, min_sig=5.0,
                width_lo=0.45, width_hi=2.0, min_sig_fit=0.0):
    """[(канал, табличная энергия, ...)] — что реально нашлось.

    lines — результат calibrate.curate: (энергия группы, метка, чистота,
    табличная энергия).

    min_sig_fit — порог по `sig_fit` = A/σ(A) (ковариация фита, `gaussfit.
    _amp_error`). ⛔ Умолчание 0.0 = гейт ВЫКЛЮЧЕН и ветка не исполняется
    вовсе: `match_lines` зовут ещё семь мест в четырёх файлах (мерки
    `gaussfit_check`, `gate_blind_check`, `ecal_accept_check`,
    `ecal_extrapolation`), и сдвинуть их молча нельзя — мерка, у которой
    отбор изменился под ногами, меряет уже не то. Кто хочет гейт — просит
    его поимённо; в конвейере это делает `calibrate` и только на первом
    проходе (см. `PASS1_MIN_SIG_FIT`).
    """
    global LAST_NOCONV, LAST_BOUND
    n = len(counts)
    out = []
    noconv = []
    bound = []
    for e_ref, label, purity, e_table in lines:
        ch0 = ecal.channel(e_ref)
        if ch0 < 4 or ch0 > n - 5:
            continue
        dedch = abs(ecal.dEdch(ch0))
        if dedch <= 0:
            continue
        fwhm_ch = res_a * np.sqrt(max(e_ref, 5.0)) / dedch
        if fwhm_ch < 1.2:
            fwhm_ch = 1.2
        r, status = fit_peak_ex(counts, ch0, fwhm_ch / FWHM_SIGMA, window=2.2)
        if status in (gaussfit.NOCONV, gaussfit.SINGULAR):
            # `V13`: не «линии нет», а фит разбежался — это надо видеть отдельно.
            noconv.append(dict(e_ref=e_ref, label=label, ch=ch0, status=status))
        elif status == gaussfit.BOUND:
            bound.append(dict(e_ref=e_ref, label=label, ch=ch0, status=status))
        if r is None or r['sig'] < min_sig:
            continue
        # `V14`: значимость счётная (`sig`) у гауссианы, севшей на изгиб
        # континуума во всё окно, огромна — «площадь» под ней огромна.
        # Значимость АМПЛИТУДЫ по ковариации у такого фита обваливается:
        # амплитуда там не определена. Гейт по ней — не строже, а ДРУГОЙ.
        if min_sig_fit > 0.0 and float(r.get('sig_fit', 0.0)) < min_sig_fit:
            continue
        if abs(r['mu'] - ch0) > tol_fwhm * fwhm_ch:
            continue
        ratio = r['fwhm'] / fwhm_ch
        if ratio < width_lo or ratio > width_hi:
            continue
        out.append(dict(ch=r['mu'], e_ref=e_ref, label=label, purity=purity,
                        fwhm=r['fwhm'], sig=r['sig'], area=r['area'],
                        # `V13`: значимость амплитуды ПО КОВАРИАЦИИ. Читателей
                        # ЧЕТЫРЕ (сверено грепом `sig_fit` по дереву 10.09.2026,
                        # `T185`): мерки `gaussfit_check.py`, `gate_blind_check.py`,
                        # `calib_null_check.py` и сам этот файл — гейт
                        # `min_sig_fit` выше (`:178`). Величина считается не
                        # здесь, а в `gaussfit.py:249`. Снимать её как
                        # «никем не читаемую» НЕЛЬЗЯ.
                        sig_fit=float(r.get('sig_fit', 0.0))))
    out.sort(key=lambda a: a['ch'])
    dedup = []
    for a in out:
        if dedup and abs(a['ch'] - dedup[-1]['ch']) < 0.4 * min(a['fwhm'], dedup[-1]['fwhm']):
            if a['sig'] > dedup[-1]['sig']:
                dedup[-1] = a
            continue
        dedup.append(a)
    LAST_NOCONV = noconv
    LAST_BOUND = bound
    return dedup


# ---------------------------------------------------------------------------
# 3. кандидаты в калибровку и выбор между ними
# ---------------------------------------------------------------------------
def _weights(pairs):
    return np.sqrt(np.minimum([a['sig'] for a in pairs], 100.0))


def affine_of(stored, pairs, nmax, scale_only=False):
    """E' = alpha*E_stored + beta — уплывшее усиление, а не новая форма кривой.

    scale_only оставляет один параметр: это всё, что можно себе позволить, когда
    найдена одна линия. Свободных параметров у кандидата должно быть меньше, чем
    точек, иначе он садится на них точно, получает нулевую невязку и выигрывает
    у любой честной калибровки (см. choose).
    """
    if not pairs:
        return None
    x = np.array([stored.energy(a['ch']) for a in pairs], dtype=float)
    y = np.array([a['e_ref'] for a in pairs], dtype=float)
    w = _weights(pairs)
    if scale_only or len(x) == 1 or float(np.ptp(x)) < 1e-9:
        alpha, beta = float((w * y).sum() / max((w * x).sum(), 1e-9)), 0.0
    else:
        alpha, beta = np.polyfit(x, y, 1, w=w)
    if not np.isfinite(alpha) or not 0.5 < alpha < 2.0:
        return None
    coef = [alpha * c for c in stored.coef]
    coef[0] += beta
    cal = Ecal(coef, nmax)
    return cal if cal.monotone() else None


# ---------------------------------------------------------------------------
# `B24`: запрет экстраполировать ВНИЗ от самой нижней опоры
# ---------------------------------------------------------------------------
# ⛔ Ниже самой нижней опорной линии у кандидата нет НИ ОДНОГО свидетельства, и
# он волен уходить куда угодно. Ровно так `G1S16_Ba133_P5` получил прямую
# −25.024 + 3.0032·ch по трём опорам выше 295 кэВ (одна из них — фоновый K-40 на
# 1460, то есть рычаг во всю шкалу) и увёл линию 81 кэВ на 68.1. Разбор после
# этого бария в спектре бария НЕ НАШЁЛ ВОВСЕ.
#
# ⚠ Запрещать РАСХОЖДЕНИЕ С ПОСТАВОЧНОЙ нельзя: поставочная бывает плоха целиком,
# и честная поправка усиления расходится с ней везде — на `AS1Pro_UGlass` это
# 36 кэВ на 1764. Запрещать надо РОСТ расхождения там, где опор нет: кандидат
# имеет право отличаться от поставочной ровно настолько, насколько это
# подтверждено опорами, и не больше. Отсюда «избыток» — насколько сильнее он
# расходится ниже нижней опоры, чем в пределах опор.
#
# Мера — в долях ПШПВ: у HPGe и RadiaCode-101 кэВ несравнимы (та же шкала, что у
# residual_fwhm). None выключает проверку целиком (ключ `--extrap=off`).
#
# ⚖ Величина ВЫВЕДЕНА развёрткой по корпусу, а не назначена. Мерка — промах по
# линиям ниже 200 кэВ на НЕПОДВИЖНОМ наборе (канал пика определён по данным
# один раз, `ecal_compare.py`), 122 спектра, 87 линий: без запрета Σ|промах|
# 14.03 ПШПВ, худший 1.414; с запретом 11.12 и 1.232, спектров хуже 0.5 ПШПВ
# 7 → 3. Развёртка 0.5/0.6/0.7/0.75/0.8/0.9/1.0/1.25/1.5/2.0/3.0 даёт ПОЛКУ
# 0.6…0.9 (Σ 8.35…8.54 на общей сетке), и 0.75 — её середина: на 1.0 больной
# спектр `B24` откатывается на поставочную, на 0.5 портятся ещё двое. Край
# сетки не берём сознательно (`S93`).
EXTRAP_EXCESS_FWHM = 0.75

# На кого распространяется запрет. 'poly' — только на кандидатов, которые строят
# кривую С НУЛЯ (`poly1`, `poly2`); 'all' — на всех, включая поправки к
# поставочной (`gain`, `affine`). Разница не косметическая: `gain` и `affine`
# суть α·поставочная + β, то есть ниже опор они повторяют ФОРМУ поставочной и
# выдумать там ничего не могут, а `poly*` могут — именно они и уводят низ шкалы.
EXTRAP_SCOPE = 'poly'

#: `V19`: кому запрет экстраполяции действует ещё и ВЫШЕ верхней опоры.
#: Кубика (`poly3`) — единственный кандидат, у которого выше верхней опоры
#: есть своя свобода: квадратика там ограничена `bend_ok`, а кубический член
#: растёт как ch³ и на 1024-канальной шкале G1S с верхней опорой 1408 кэВ
#: способен увести верх на сотни кэВ, не тронув ни одной опоры. Поэтому
#: третья степень допускается только там, где она ПОДТВЕРЖДЕНА опорами с
#: обеих сторон — тем же правилом «расходиться с поставочной можно ровно
#: настолько, насколько это подтверждено опорами», что и `B24` для низа.
#: Для `poly1`/`poly2` верх не проверяется намеренно: это меняло бы нынешнее
#: поведение корпуса, а оно — база, то есть слово Amber.
EXTRAP_ABOVE = ('poly3',)

#: Наибольшая степень полиномиального кандидата, когда вызывающий её не
#: назвал (`calibrate(..., max_order=None)`). Умолчание 2 — прежнее; 3
#: впускает кубику на условиях `V19` (см. `choose`). Модульная константа,
#: а не правка умолчания: смена степени двигает шкалу спектров корпуса, то
#: есть базу, — как `FORM` у `V2`.
MAX_ORDER = 2

#: `V26` (решение Amber 05.09.2026): сторож «поправка усиления ≤ 5 %» у `gain`
#: действует, пока опор не больше `GAIN_GUARD_MAX_N`. До 05.09.2026 он жил
#: только при n = 1, и ВТОРАЯ опора любого качества открывала поправку без
#: предела (при n = 2 конкурентов у `gain` ещё нет: `affine` и `poly1` ждут
#: трёх опор, остаётся лишь штраф 20·(drift − 0.06) в оценке). Нулевое плечо
#: `calib_null_check.py` давало 175.5 / 139.7 / 102.9 кэВ (`G1S16_Am241_P25`,
#: `G1S24_Am241_P5`; `V18`). При n ≥ 3 сторожа нет, как и прежде.
GAIN_GUARD_MAX_N = 2
GAIN_GUARD_DRIFT = 0.05

#: `V18`: правило, по которому кандидат с лишними степенями свободы должен
#: за них ПЛАТИТЬ.
#:
#: 'margin' — прежнее правило: кандидат берётся, если его СКО в долях ПШПВ
#:   меньше СКО текущего лучшего на десятую часть (`keep_margin`), без
#:   оглядки на число параметров. Порог один и тот же при трёх опорах и при
#:   семнадцати — и на трёх опорах квадратика (три параметра, одна степень
#:   свободы) выигрывает у прямой почти всегда: лишний параметр СКО только
#:   роняет.
#: 'df' — ⛔ **УМОЛЧАНИЕ с 06.09.2026, решение Amber: «Дефект: кандидат платит
#:   за степень свободы».** Кандидат, забравший степени свободы, обязан
#:   уронить сумму квадратов невязок хотя бы во столько же раз, во сколько он
#:   их забрал:
#:       SS_cand / ν_cand^k  <  SS_best / ν_best^k,   ν = n − p,
#:   то есть ln(SS_best/SS_cand) > k·ln(ν_best/ν_cand). При k = 1 это в
#:   точности сравнение НЕСМЕЩЁННЫХ оценок дисперсии SS/ν вместо СКО: та же
#:   мысль, что у AIC/скорректированного R², только без нормировки на n.
#:   Коэффициент — `MODEL_FREEDOM_K`, выведен развёрткой (см. ниже).
#: 'ftest' — то же, и сверх того кандидат с Δp дополнительными параметрами
#:   против текущего лучшего должен пройти F-критерий
#:       F = ((SS_best − SS_cand)/Δp) / (SS_cand/ν),  ν = n − p_cand,
#:   на уровне `MODEL_ALPHA`. Это и есть плата за свободу: при ν = 1 порог
#:   F(1,1) = 161, при ν = 3 — 10.1, при ν = 10 — 5.0, при ν → ∞ — 3.84;
#:   прежний порог «СКО меньше на 10 %» отвечает F ≈ 0.23·ν, то есть на
#:   малых n он ЛЬГОТНЕЕ F-критерия примерно на порядок, а сравнивается с
#:   ним только к двум десяткам опор.
#:
#: ⚠ Что плата за свободу даёт нулевому плечу `V18` и чего не даёт — измерено,
#: а не выведено (`calib_null_check.py`, `calib_sweep_f56.py`, журналы
#: `handover-2026-09-05-f5-v18-v19` и `handover-2026-09-06-f56-v18`).
#: Точка с нулевой невязкой под нынешним ответом НЕ МОЖЕТ увеличить выигрыш
#: ни одного конкурента: SS победителя не меняется, SS конкурента после
#: перефита только растёт. Двигать выбор она может лишь одним путём —
#: ОТКРЫВАЯ кандидата, которому раньше не хватало опор по счёту (n < p + 2).
#:
#: ⛔ И вот тут развёртка F56 говорит «нет»: у такого кандидата ν = 1, он
#: садится на свои точки почти точно, роняет SS в разы и платит ЛЮБУЮ
#: разумную цену. Из 20 ненейтральных спектров 12 — ровно этот механизм
#: (`unlock`), и плата их не снимает. Больше того, у всякого правила, чей
#: порог ЗАВИСИТ от числа опор ('df', 'aic', 'ftest'), лишняя опора порог
#: УДЕШЕВЛЯЕТ (ν_best/ν_cand: 3/1 → 4/2), то есть плата заводит СВОЙ путь
#: ненейтральности. Развёрнуто по четырём формам платы и двум счётным
#: порогам: нейтральность растёт только вместе с числом спектров, у которых
#: положительный контроль перестаёт что-либо двигать, — то есть покупается
#: заморозкой отбора. **Нейтральной машинку эта правка не делает и сделать
#: не может; она делает выбор ЧЕСТНЕЕ там, где кандидат выдумывает кривую.**
#: ⛔ РЕШЕНИЕ Amber 06.09.2026 (вопросник, после развёртки F56): УМОЛЧАНИЕМ
#: ПЛАТЫ НЕТ — 'margin', как было. Довод назван замером: нейтральность
#: нулевого плеча выросла со 100 до 101 спектра из 120, а внешняя мерка
#: промаха известных линий ухудшилась 200.07 → 200.69 ПШПВ (+0.31 %), и из
#: шести сдвинутых спектров лучше не стало НИ ОДНОМУ (хуже 3, ровно 3).
#: Режим 'df' сохранён рабочим ключом: им воспроизводится вся развёртка
#: полосы (handover/handover-2026-09-06-f56-v18.md), и он понадобится тому
#: заходу, который возьмётся за НАСТОЯЩИЙ путь избыточной опоры — счётный
#: порог, открывающий кандидата (12 из 20 ненейтральных спектров).
MODEL_TEST = 'margin'
MODEL_ALPHA = 0.05

#: Коэффициент платы за степень свободы в режиме 'df'. 0 — платы нет вовсе
#: (прежнее правило), 1 — кандидат платит ровно теми степенями свободы, что
#: забрал, дальше — с наценкой.
#:
#: ⚖ ВЫВЕДЕН РАЗВЁРТКОЙ по корпусу, а не назначен (`calib_sweep_f56.py`,
#: 122 спектра стадии 1, по 360 опытов нулевого плеча на каждое значение, на
#: неподвижном наборе опор; `calib_quality_f56.py` — внешняя мерка по промаху
#: известных линий). При `MODEL_TEST_SCOPE='poly'` развёртка 0.5 / 1 / 1.5 /
#: 2 / 3 / 4 / 8 даёт ПОЛКУ 1…2: на ней меняют ответ одни и те же ЧЕТЫРЕ
#: спектра (`poly*` по 4–5 опорам, где ν = 1…2, уходят в `affine`/`gain`/
#: `stored`), положительный контроль не трогается вовсе (114 из 120
#: реагируют, как и было), а внешняя мерка ниже 200 кэВ совпадает с прежней
#: до сотых. Ниже 1 плата не меняет НИЧЕГО (0 спектров), от 3 начинает
#: снимать подгонку у спектров с 7–10 опорами. 1.5 — середина полки; край
#: сетки не берём сознательно (`S93`).
#:
#: ⛔ Чего эта плата НЕ ДАЁТ — измерено: нулевого плеча она не закрывает.
#: 100 нейтральных спектров из 120 при прежнем правиле, 102 при этой плате;
#: полностью нейтральной машинка не становится ни при каком коэффициенте ни в
#: одной из четырёх испробованных форм платы, потому что главный путь нулевой
#: опоры — счётный порог, а не сравнение (журнал §3).
MODEL_FREEDOM_K = 1.5

#: Сколько степеней свободы кандидат обязан ОСТАВИТЬ: допускается лишь при
#: n ≥ p + `MIN_FREE_DF`. 1 — прежний счётный порог («хотя бы одна степень
#: свободы»): `gain` от двух опор, `affine` от трёх, `poly2` от четырёх.
#:
#: ⚠ Это ВТОРАЯ половина платы за свободу, и поднимать её БЕСПОЛЕЗНО —
#: измерено, а не выведено (`calib_sweep_f56.py`, нулевое плечо на 120
#: спектрах): при ν ≥ 2 нейтральных не 100, а **90**, при ν ≥ 3 — 96. Порог
#: не закрывает лазейку, а переносит её на спектры с n = 3, которых больше,
#: чем с n = 2, и заодно снимает подгонку у 21 спектра. Константа заведена
#: затем, чтобы счётный порог был НАЗВАН (это он, а не сравнение, — главный
#: путь избыточной опоры) и чтобы следующий не мерил это заново.
MIN_FREE_DF = 1

#: С кого берётся плата за свободу. 'all' — со всякого кандидата с лишними
#: параметрами, включая `gain`/`affine`; 'poly' — только с тех, кто строит
#: кривую С НУЛЯ (`poly1`…`poly3`), по той же логике, что `EXTRAP_SCOPE`:
#: `gain` и `affine` суть α·поставочная + β и формы не выдумывают.
#:
#: ⛔ Умолчание 'poly' — РАЗНИЦА ИЗМЕРЕНА, а не выбрана по красоте. При 'all'
#: плата снимает честную поправку усиления у спектров с 8–13 опорами и портит
#: шкалу корпуса: `df` k=1 со всех даёт по внешней мерке 200.07 → 201.03 ПШПВ
#: (ниже 200 кэВ 19.85 → 20.63), 31 спектр сдвигается (лучше 7, хуже 9), 25
#: спектров из 120 перестают реагировать на положительный контроль, опор в
#: конвейере 829 → 816. Та же плата с одних полиномов: 200.07 → 200.42, ниже
#: 200 кэВ **без изменений**, сдвигаются 4 спектра, контроль не трогается.
#: F-критерий при 'all' F5 измерил тем же итогом ещё жёстче: поставочная
#: остаётся у 75 спектров из 120 вместо 21, и конвейер теряет опоры, потому
#: что следующие проходы ищут линии по неисправленной шкале.
MODEL_TEST_SCOPE = 'poly'

#: След последнего `choose`: по каждому кандидату — почему его нет
#: (`count` — не хватило опор по счёту, `fit` — не построился/немонотонен/
#: изгиб, `drift>5%`, `extrap_below`/`extrap_above` — запрет экстраполяции)
#: или его оценка (`score`, `ss`, `paid` — плата за свободу) и выбран ли он.
#: Читатель — мерка `calib_null_check.py`: по следу она называет МЕХАНИЗМ, по
#: которому нулевая опора сдвинула выбор. Конвейер его не читает.
LAST_CANDIDATES = []


def extrapolation_excess(cal, stored, pairs, res_a, side='below'):
    """Насколько сильнее кандидат расходится с поставочной ЗА опорами (ниже
    нижней при side='below', выше верхней при side='above'), чем в пределах
    опор. В долях ПШПВ; отрицательное значение — не расходится.
    """
    if not pairs:
        return 0.0
    ch = [a['ch'] for a in pairs]
    ch_lo, ch_hi = min(ch), max(ch)
    if ch_lo < 1.0:
        return 0.0

    def worst(grid):
        if not len(grid):
            return 0.0
        d = np.abs(cal.energy(grid) - stored.energy(grid))
        e = np.maximum(np.abs(stored.energy(grid)), 5.0)
        return float(np.max(d / (res_a * np.sqrt(e))))

    inside = worst(np.linspace(ch_lo, max(ch_hi, ch_lo + 1.0), 200))
    if side == 'above':
        top = float(stored.nmax - 1)
        if ch_hi >= top:
            return 0.0
        step = max(1.0, (top - ch_hi) / 200.0)
        outside = worst(np.arange(ch_hi, top + 0.5 * step, step))
    else:
        step = max(1.0, ch_lo / 200.0)
        outside = worst(np.arange(0.0, ch_lo, step))
    return outside - inside


def extrapolation_ok(cal, stored, pairs, res_a, tol=None, side='below'):
    tol = EXTRAP_EXCESS_FWHM if tol is None else tol
    if tol is None:
        return True
    return extrapolation_excess(cal, stored, pairs, res_a, side=side) <= tol


def bend_ok(cal, line, nmax, max_bend=0.15, lo=5.0):
    """Полином не должен уходить от прямой по своим же якорям больше чем на 15 %.

    Квадратика, построенная по линиям, покрывающим часть диапазона, проходит
    через все свои точки и уходит в бессмыслицу за их пределами. Проверяется
    только выше половины самого нижнего якоря: ниже прямая уходит в минус и
    критерий теряет смысл.
    """
    if line is None:
        return True
    grid = np.arange(lo, nmax, max(1, nmax // 400), dtype=float)
    straight = np.polyval(line, grid)
    tol = np.maximum(40.0, max_bend * np.abs(straight))
    return bool(np.all(np.abs(cal.energy(grid) - straight) <= tol))


def poly_of(pairs, order, nmax):
    if len(pairs) < order + 1:
        return None
    ch = np.array([a['ch'] for a in pairs], dtype=float)
    e = np.array([a['e_ref'] for a in pairs], dtype=float)
    w = _weights(pairs)
    line = np.polyfit(ch, e, 1, w=w) if len(ch) >= 2 else None
    A = np.vstack([ch ** i for i in range(order + 1)]).T * w[:, None]
    coef, *_ = np.linalg.lstsq(A, e * w, rcond=None)
    cal = Ecal(coef, nmax)
    lo = max(5.0, 0.5 * float(ch.min()))
    if not cal.monotone() or not bend_ok(cal, line, nmax, lo=lo):
        return None
    return cal


def residual_fwhm(cal, pairs, res_a):
    """Средневзвешенная невязка в долях FWHM — единственная шкала, в которой
    HPGe и RadiaCode-101 сравнимы между собой."""
    if not pairs:
        return float('inf')
    d = np.array([(cal.energy(a['ch']) - a['e_ref']) /
                  max(res_a * np.sqrt(max(a['e_ref'], 5.0)), 1e-9) for a in pairs])
    return float(np.sqrt((d ** 2).mean()))


def gain_drift(cal, stored, nmax):
    g = (cal.energy(nmax - 1) - cal.energy(0)) / max(nmax - 1, 1)
    g0 = (stored.energy(nmax - 1) - stored.energy(0)) / max(nmax - 1, 1)
    return abs(g - g0) / g0 if g0 > 0 else 0.0


def npar(tag):
    """Число СВОБОДНЫХ параметров кандидата по его метке: `stored` 0, `gain` 1,
    `affine` 2, `polyN` N+1. Хвосты меток (`/recal`, `/robust`) не в счёт."""
    base = tag.split('/')[0]
    if base.startswith('poly'):
        return int(base[4:]) + 1
    return {'stored': 0, 'gain': 1, 'affine': 2}.get(base, 0)


def residual_ss(cal, pairs, res_a):
    """Сумма квадратов невязок в долях ПШПВ — та же величина, что под корнем
    у `residual_fwhm`, но БЕЗ деления на n: F-критерию нужна именно она."""
    if not pairs:
        return float('inf')
    d = np.array([(cal.energy(a['ch']) - a['e_ref']) /
                  max(res_a * np.sqrt(max(a['e_ref'], 5.0)), 1e-9) for a in pairs])
    return float((d ** 2).sum())


def f_crit(dp, nu, alpha=None):
    """Критическое значение F(dp, nu) на уровне alpha (по умолчанию
    `MODEL_ALPHA`). Считает SciPy; без него режим 'ftest' работать не может, и
    сказать об этом надо вслух, а не подменять таблицей «примерно»."""
    alpha = MODEL_ALPHA if alpha is None else alpha
    try:
        from scipy.stats import f as _f
    except ImportError:
        raise RuntimeError(u'MODEL_TEST=ftest требует SciPy (scipy.stats.f.ppf)')
    return float(_f.ppf(1.0 - alpha, dp, nu))


def freedom_paid(best_tag, best_ss, tag, ss, n):
    """`V18`: может ли кандидат `tag` сместить текущего лучшего `best_tag`,
    если у него больше свободных параметров.

    В режиме 'margin' — всегда (прежнее поведение). В режиме 'df' — если он
    уронил SS не меньше чем в (ν_best/ν_cand)^k раз, то есть заплатил за
    забранные степени свободы. В режиме 'ftest' — пройдя F-критерий за каждый
    лишний параметр. Кандидат без единой степени свободы (ν < 1) не проходит
    никогда, а точный фит (ss = 0) при ν ≥ 1 проходит всегда. При Δp ≤ 0 (та
    же или меньшая сложность) платить не за что, решает `keep_margin`."""
    dp = npar(tag) - npar(best_tag)
    if MODEL_TEST == 'margin' or dp <= 0:
        return True
    if MODEL_TEST_SCOPE == 'poly' and not tag.startswith('poly'):
        return True
    if tag == 'gain' and n == 1:
        # Поправка усиления по ОДНОЙ линии — отдельное, ранее принятое решение
        # (стабилизатор прибора по опорному пику; урановое стекло AS1 Pro), со
        # своими сторожами в `choose`: значимость ≥ 40 и поправка ≤ 5 %.
        # Степеней свободы у неё нет по построению, судить тут нечего, и
        # снимать её плата за свободу не должна.
        return True
    nu = n - npar(tag)
    if nu < 1:
        return False
    if ss <= 0.0:
        return True
    if not np.isfinite(best_ss):
        return True
    if MODEL_TEST == 'df':
        nu_best = max(n - npar(best_tag), 1)
        # ⚠ Порог ЗАВИСИТ ОТ ЧИСЛА ОПОР, и это осознанно: чем больше данных,
        # тем дешевле обязан обходиться лишний параметр (при n = 3 у `affine`
        # против `stored` ν_best/ν_cand = 3, при n = 13 — 13/11 = 1.18). Цена
        # этого выбора — своя доля ненейтральности: лишняя опора двигает
        # отношение (3/1 → 4/2), то есть порог УДЕШЕВЛЯЕТ. Плата, свободная от
        # этого (`ln(SS_b/SS_c) > k·Δp`, ровная за параметр), измерена и
        # ОТВЕРГНУТА: она одинакова при трёх опорах и при тринадцати и снимает
        # честную поправку у спектров с 8–13 опорами (журнал F56 §3, §6).
        return np.log(best_ss / ss) > MODEL_FREEDOM_K * np.log(float(nu_best) / nu)
    F = ((best_ss - ss) / dp) / (ss / nu)
    return F > f_crit(dp, nu)


def choose(stored, pairs, res_a, nmax, max_order=None, keep_margin=0.9, force=False):
    """Хранившаяся калибровка остаётся, если поправка не улучшила невязку
    заметно (на десятую часть): менять калибровку ради шума незачем.

    У каждого кандидата должна остаться хотя бы одна степень свободы. Иначе
    прямая по двум точкам проходит через них ровно, получает нулевую невязку и
    побеждает всегда — а за пределами этих двух точек может уходить куда угодно.

    `V18`/`V19`: сверх того в режиме `MODEL_TEST='ftest'` кандидат с лишними
    параметрами платит за них F-критерием (`freedom_paid`), а кубика
    (`max_order=3`) допускается лишь при ν ≥ 1 (то есть от пяти опор),
    пройдя F-критерий против текущего лучшего И запрет экстраполяции с ОБЕИХ
    сторон опор (`EXTRAP_ABOVE`). Спектр с тремя опорами кубику не получит
    по счёту (n < 5), с пятью — только при 161-кратном выигрыше F(1,1).

    `force` снимает привилегию хранившейся: она перестаёт быть точкой отсчёта и
    участвует наравне с остальными (`B16`, указание Amber 16.08.2026 —
    «перекалибровать»). Нужно там, где хранившаяся заведомо плоха, но
    выигрывает по формальному порогу: у `G1S24_Am241_P5` она давала −14.97 кэВ
    на K-40 и промах 0.27 ПШПВ на самой линии Am-241, из-за чего разбор её и не
    видел. ⚠ Совсем выбросить хранившуюся нельзя: когда линий мало, кандидатов
    может не оказаться вовсе — тогда возвращается она же, и метка это назовёт.
    """
    n = len(pairs)
    if max_order is None:
        max_order = MAX_ORDER
    cands = [('stored', stored)]
    trace = []                     # `V18`: почему каждого кандидата нет или есть

    def note(tag, status):
        trace.append(dict(tag=tag, status=status, score=None, ss=None, paid=None,
                          chosen=False))
    # Поправка усиления по одной линии — вырожденный случай (параметр один,
    # точка одна, невязка ноль), и обычно так делать нельзя. Исключение: очень
    # значимая линия и поправка в пределах пяти процентов. Это ровно то, что
    # делает стабилизатор прибора по опорному пику, и без этого урановое стекло
    # AS1 Pro оставалось с промахом 36 кэВ на 1764 кэВ.
    strongest = max(a['sig'] for a in pairs) if pairs else 0.0
    if n >= 1 + MIN_FREE_DF and strongest >= 20.0 or n == 1 and strongest >= 40.0:
        cal = affine_of(stored, pairs, nmax, scale_only=True)
        # `V26`: предел поправки, пока опор не больше `GAIN_GUARD_MAX_N`.
        if cal is not None and (n > GAIN_GUARD_MAX_N
                                or gain_drift(cal, stored, nmax) <= GAIN_GUARD_DRIFT):
            cands.append(('gain', cal))
        else:
            note('gain', 'fit' if cal is None else 'drift>5%')
    else:
        note('gain', 'count')
    if n >= 2 + MIN_FREE_DF:
        cal = affine_of(stored, pairs, nmax)
        if cal is not None:
            cands.append(('affine', cal))
        else:
            note('affine', 'fit')
    else:
        note('affine', 'count')
    for order in range(1, max_order + 1):
        if n < order + 1 + MIN_FREE_DF:
            note('poly%d' % order, 'count')
            continue
        cal = poly_of(pairs, order, nmax)
        if cal is not None:
            cands.append(('poly%d' % order, cal))
        else:
            note('poly%d' % order, 'fit')

    # `B24`: кандидат, который ниже нижней опоры уходит от поставочной дальше,
    # чем его же опоры это подтверждают, выбывает. Хранившаяся не проверяется —
    # она сама себе точка отсчёта, расхождение с собой равно нулю, и выбывать ей
    # некуда: без неё список кандидатов может оказаться пустым.
    # Если не уцелел ни один, остаётся она же, и метка это назовёт: «мы отказались
    # выдумывать шкалу» — законный исход, а не сбой.
    if EXTRAP_EXCESS_FWHM is not None and len(cands) > 1:
        kept = [cands[0]]
        for t, c in cands[1:]:
            if EXTRAP_SCOPE == 'poly' and not t.startswith('poly'):
                kept.append((t, c))
            elif not extrapolation_ok(c, stored, pairs, res_a):
                note(t, 'extrap_below')
            elif t in EXTRAP_ABOVE and not extrapolation_ok(c, stored, pairs, res_a,
                                                             side='above'):
                note(t, 'extrap_above')
            else:
                kept.append((t, c))
        cands = kept

    def scored(tag, cal):
        score = residual_fwhm(cal, pairs, res_a)
        drift = gain_drift(cal, stored, nmax)
        if drift > 0.06:
            score += 20.0 * (drift - 0.06)       # усиление врёт на проценты, не в разы
        return score

    def seen(tag, cal, paid=None):
        rec = dict(tag=tag, status='ok', score=scored(tag, cal),
                   ss=residual_ss(cal, pairs, res_a), paid=paid, chosen=False)
        trace.append(rec)
        return rec

    def done(tag, cal, score):
        global LAST_CANDIDATES
        for rec in trace:
            rec['chosen'] = rec['status'] == 'ok' and rec['tag'] == tag.split('/')[0]
        LAST_CANDIDATES = trace
        return tag, cal, score

    if force and len(cands) > 1:
        # Хранившаяся снята с пьедестала: побеждает лучший ПОДОБРАННЫЙ кандидат,
        # порог `keep_margin` к нему не применяется — он и заведён затем, чтобы
        # не менять калибровку ради шума, а здесь смена как раз и требуется.
        # Плата за свободу (`freedom_paid`) применяется и здесь: «перекалибровать»
        # не значит «отдать кубике».
        seen(*cands[0])
        best_tag, best_cal = cands[1][0], cands[1][1]
        best_score = scored(*cands[1])
        best_ss = residual_ss(best_cal, pairs, res_a)
        seen(best_tag, best_cal)
        for tag, cal in cands[2:]:
            score = scored(tag, cal)
            ss = residual_ss(cal, pairs, res_a)
            paid = freedom_paid(best_tag, best_ss, tag, ss, n)
            seen(tag, cal, paid)
            if score < best_score and paid:
                best_tag, best_cal, best_score, best_ss = tag, cal, score, ss
        return done(best_tag + '/recal', best_cal, best_score)

    base = residual_fwhm(stored, pairs, res_a)
    best_tag, best_cal, best_score = 'stored', stored, base
    best_ss = residual_ss(stored, pairs, res_a)
    seen('stored', stored)
    for tag, cal in cands[1:]:
        score = scored(tag, cal)
        ss = residual_ss(cal, pairs, res_a)
        paid = freedom_paid(best_tag, best_ss, tag, ss, n)
        seen(tag, cal, paid)
        if score < best_score * keep_margin and paid:
            best_tag, best_cal, best_score, best_ss = tag, cal, score, ss
    return done(best_tag, best_cal, best_score)


#: `V14`, решение Amber 27.08.2026: порог гейта по A/σ(A) на ПЕРВОМ проходе.
#:
#: ⛔ Почему на первом проходе, а не на последнем — измерено, а не выведено.
#: Вырожденно широкий фит (ширина фита / модельная > 1.5) на понятной части
#: базы `out_v7`: **64 из 574** при допусках первого прохода (ширина
#: 0.35…2.60) и **9 из 493** при штатных (0.50…1.80). Ширинный фильтр
#: последнего прохода эту корзину уже вычистил, и гейт там покупает почти
#: ничего; в первом же проходе из 64 широких **59** имеют A/σ(A) < 3, то есть
#: порог 3 снимает именно их. Мерка: `gate_blind_check.py --gate=pass1|prod`.
#:
#: ⛔ Почему 3, а не 8: порог 8 отвергнут по цене. Понятная часть 493 → 303,
#: линий С ВЕРШИНОЙ теряется 66 из 362 (18.2 %) против 7 (1.9 %) при пороге 3;
#: спектров меньше чем с тремя опорами 11 → 39 из 81, а `G1S16_K40_Mar_2`
#: остаётся без единой опоры. Избирательность при этом растёт всего
#: 2.55 → 2.96 → 3.32.
PASS1_MIN_SIG_FIT = 3.0


def calibrate(counts, stored_coef, lines, res_a, max_order=None, force=False):
    """Полный цикл: грубое сопоставление -> поправка -> точное сопоставление.

    Возвращает (Ecal, пары, res_a, метка режима). `max_order=None` — взять
    модульную `MAX_ORDER` (`V19`); конвейер корпуса степень не называет, и
    флаг живёт здесь, одним местом.
    """
    nmax = len(counts)
    if max_order is None:
        max_order = MAX_ORDER
    stored = Ecal(stored_coef, nmax)

    cal = stored
    pairs = []
    tag = 'stored'
    for npass, (tol, wlo, whi) in enumerate(
            ((2.5, 0.35, 2.6), (1.2, 0.45, 2.0), (0.7, 0.5, 1.8))):
        # `V14`: гейт по A/σ(A) стоит ТОЛЬКО на первом проходе — там и только
        # там живут вырожденно широкие фиты (довод при `PASS1_MIN_SIG_FIT`).
        # Второй, третий, `final` и ветка `robust` идут БЕЗ него.
        found = match_lines(counts, cal, lines, res_a, tol_fwhm=tol,
                            width_lo=wlo, width_hi=whi,
                            min_sig_fit=(PASS1_MIN_SIG_FIT if npass == 0
                                         else 0.0))
        if len(found) < 1:
            break
        pairs = found
        # ширины найденных линий уточняют разрешение
        red = [a['fwhm'] * abs(cal.dEdch(a['ch'])) / np.sqrt(max(a['e_ref'], 5.0))
               for a in found if a['purity'] > 0.85]
        if len(red) >= 2:
            res_a = float(np.median(red))
        step_tag, step_cal, _ = choose(stored, pairs, res_a, nmax, max_order,
                                       force=force)
        cal, tag = step_cal, step_tag
    if pairs:
        final = match_lines(counts, cal, lines, res_a, tol_fwhm=0.7,
                            width_lo=0.5, width_hi=1.8)
        if final:
            pairs = final
        # Пара, оставшаяся дальше полуширины от своей табличной энергии, —
        # почти наверняка не та линия. Держать её в отчёте нельзя: одна такая
        # у фона Obsidian давала «одну опорную линию с невязкой 95 кэВ».
        pairs = [a for a in pairs
                 if abs(cal.energy(a['ch']) - a['e_ref'])
                 <= 0.5 * res_a * np.sqrt(max(a['e_ref'], 5.0))]
        # Одна перепутанная линия — это не ошибка калибровки, а неверное
        # отождествление, и она одна тянет полином за собой. Выбрасываем
        # выбросы по MAD и перефитываем, если стало лучше (`robust_refit`,
        # одно правило на двоих со сводной таблицей семьи, `V27`).
        rob = robust_refit(stored, cal, pairs, res_a, nmax, max_order, force=force)
        if rob is not None:
            c2, t2, keep = rob
            cal, tag = c2, t2 + '/robust'
            pairs = match_lines(counts, cal, lines, res_a, tol_fwhm=0.7,
                                width_lo=0.5, width_hi=1.8) or keep
    return cal, pairs, res_a, tag


def robust_refit(stored, cal, pairs, res_a, nmax, max_order=None, force=False):
    """Выбросы по MAD и перефит по оставшимся: -> (cal, tag, keep) либо None,
    если опор меньше пяти, выбросов нет или перефит не лучше.

    Вынесено из `calibrate` без изменения правила (`V27`): та же корзина
    3·MAD с полом 0.35 ПШПВ на медианной энергии, тот же порог «хотя бы четыре
    остались», та же приёмка по невязке. Сводная таблица семьи (`fit_table`)
    пользуется ровно им — у пула из разных спектров перепутанная линия
    вероятнее, чем у одного.
    """
    if len(pairs) < 5:
        return None
    d = np.array([cal.energy(a['ch']) - a['e_ref'] for a in pairs])
    mad = float(np.median(np.abs(d - np.median(d)))) * 1.4826
    limit = max(3.0 * mad, 0.35 * res_a * np.sqrt(np.median(
        [a['e_ref'] for a in pairs])))
    keep = [a for a, dd in zip(pairs, d) if abs(dd - np.median(d)) <= limit]
    if 4 <= len(keep) < len(pairs):
        t2, c2, _ = choose(stored, keep, res_a, nmax, max_order, force=force)
        if residual_fwhm(c2, keep, res_a) < residual_fwhm(cal, pairs, res_a):
            return c2, t2, keep
    return None


def fit_table(stored, table, res_a, nmax, max_order=None):
    """`V27`: одна калибровка на СВОДНУЮ таблицу опор семьи — спектров одной
    группы с побайтно общей поставочной шкалой (стадия 2б `build_corpus.py`).

    Правило то же, что у одного спектра: `choose` от поставочной (она
    остаётся, если пул не улучшил невязку на десятую часть; `B24`-запрет и
    плата за свободу действуют), затем `robust_refit`. Ни одного нового
    порога. Опоры разных спектров семьи НЕ склеиваются: одна и та же линия,
    найденная в трёх спектрах, — три независимых измерения канала, и каждое
    входит со своим весом.

    -> (cal, tag, keep): калибровка семьи, метка (`stored` — пул поставочную
    не побил), опоры после отбраковки.
    """
    tag, cal, _ = choose(stored, table, res_a, nmax, max_order)
    keep = list(table)
    rob = robust_refit(stored, cal, table, res_a, nmax, max_order)
    if rob is not None:
        cal, tag, keep = rob[0], rob[1] + '/robust', rob[2]
    return cal, tag, keep


# ---------------------------------------------------------------------------
# 4. FWHM-калибровка в каналах
# ---------------------------------------------------------------------------
#: Форма модели разрешения группы (`V2`). '' — нынешняя, 'power' — степенная.
#: Ключ, а не правка умолчания: смена формы двигает ПШПВ-калибровку КАЖДОГО
#: спектра корпуса, то есть базу целиком, а это решение Amber, не агента.
FORM = ''


def fit_resolution_power(points):
    """FWHM = a*E^p — степенная форма (`V2`, 16.08.2026).

    Зачем она вообще понадобилась. Нынешняя форма фактически означает
    FWHM ~ sqrt(E) (второй член мал), а корпус говорит другое: по 1000 с лишним
    измеренных линий показатель у ВСЕХ сцинтилляторов ВЫШЕ половины — ASN16
    0.569, RC103 0.585, G1S16 0.614, G1S24 0.616, AS80x80 0.656, GS4000 0.684.
    Ширина растёт БЫСТРЕЕ корня, поэтому модель, посаженная на верх шкалы, внизу
    выходит шире настоящей — то самое, что измерил `res_low.py` (медиана
    изм/мод 0.87, у G1S16 0.73).

    Возвращается ТА ЖЕ тройка (c0, c1, c2), что и у прежней формы: сюда её
    сажают приближением по методу наименьших квадратов на рабочей шкале, чтобы
    ни один читатель модели не узнал о смене формы (их полтора десятка, и
    менять их все ради опыта нельзя). Приближение честное: степенная и
    квадратичная по E формы на 20…3000 кэВ сходятся до долей процента.
    """
    e = np.array([p[0] for p in points], dtype=float)
    f = np.array([p[1] for p in points], dtype=float)
    w = np.sqrt(np.array([p[2] for p in points], dtype=float))
    good = (e > 0) & (f > 0)
    if good.sum() < 3:
        return fit_resolution_kev(points)

    A = np.vstack([np.ones(good.sum()), np.log(e[good])]).T * w[good][:, None]
    c, *_ = np.linalg.lstsq(A, np.log(f[good]) * w[good], rcond=None)
    a, power = float(np.exp(c[0])), float(c[1])

    grid = np.linspace(20.0, 3000.0, 300)
    target = (a * grid ** power) ** 2
    B = np.vstack([np.ones_like(grid), grid, grid ** 2]).T
    coef, *_ = np.linalg.lstsq(B, target, rcond=None)

    # ⛔ ФОРМАТ ХРАНЕНИЯ НЕ ДЕРЖИТ СТЕПЕННУЮ ФОРМУ, и это измерено (`V2`).
    # Квадратичная по E, приближающая a·E^p при p > 0.5, обязана иметь c0 < 0 —
    # и ниже точки, где сумма обращается в ноль, модель схлопывается в нулевую
    # ширину. У настоящих групп корпуса это происходит на 90…200 кэВ, ровно
    # там, где степенная форма и была нужна: G1S16 даёт 0.0 кэВ на 60 против
    # 9.9 у самой степенной, RC103 — 0.0 на 40 против 10.2.
    #
    # Поэтому здесь не «поправка», а ОТКАЗ: приближение проверяется на
    # положительность и монотонность, и при провале возвращается прежняя форма
    # с объяснением. Молча отдать модель, зануляющую ширину внизу шкалы, было
    # бы хуже любой неточности: поиск пиков и разложение считают по ней.
    check = coef[0] + coef[1] * grid + coef[2] * grid ** 2
    if np.any(check <= 0.0) or np.any(np.diff(check) <= 0.0):
        bad = grid[check <= 0.0]
        print(u'⛔ степенная форма (p = %.3f) в формат не влезает: квадратичное '
              u'приближение зануляется%s — оставлена прежняя'
              % (power, u' до %.0f кэВ' % bad.max() if bad.size else u''))
        return _fit_resolution_sqrt(points)

    return np.array(coef, dtype=float)


def fit_resolution_kev(points):
    """FWHM^2 = c1*E + c2*E^2 по точкам (E, FWHM, вес)."""
    if FORM == 'power':
        return fit_resolution_power(points)
    return _fit_resolution_sqrt(points)


def _fit_resolution_sqrt(points):
    """Прежняя форма, она же умолчание: FWHM^2 = c1*E + c2*E^2.

    Свободного члена нет намеренно. Опорные линии почти всех спектров корпуса
    лежат выше 180 кэВ, c0 ими не определён, и подгонка выносила его в плюс:
    у AS80x80 получалось 75 % полуширины на 60 кэВ — величина, при которой
    детектор не разделил бы вообще ничего. Без c0 остаётся физическая форма
    sqrt(k*E) со статистикой фотоэлектронов, ровно та, в которой записаны
    модели исходной девятки (ASN16 = sqrt(2.940*E)).
    """
    e = np.array([p[0] for p in points], dtype=float)
    f = np.array([p[1] for p in points], dtype=float)
    w = np.sqrt(np.array([p[2] for p in points], dtype=float))
    if len(e) >= 4 and float(np.ptp(e)) > 400.0:
        A = np.vstack([e, e ** 2]).T * w[:, None]
        coef, *_ = np.linalg.lstsq(A, f ** 2 * w, rcond=None)
        c = np.array([0.0, coef[0], coef[1]])
        grid = np.linspace(20.0, max(e.max() * 1.4, 3000.0), 500)
        v = c[1] * grid + c[2] * grid ** 2
        rel = np.sqrt(np.maximum(v, 0.0)) / grid
        # FWHM растёт, а ОТНОСИТЕЛЬНАЯ ширина обязана падать: за неё отвечает
        # статистика фотоэлектронов. Подгонка с большим c2 давала у GS4000
        # почти плоские 7 % на 662 и 6 % на 2615 — это уже не разрешение
        # детектора, а артефакт двух свободных параметров на шести точках.
        if np.all(v > 0) and np.all(np.diff(rel) < 1e-12):
            return c
    k = float((f ** 2 * e * w).sum() / max((e * e * w).sum(), 1e-9))
    return np.array([0.0, k, 0.0])


def resolution_fn(coef):
    def f(e):
        v = coef[0] + coef[1] * float(e) + coef[2] * float(e) ** 2
        return float(np.sqrt(max(v, 1e-6)))
    return f


def monotone_ch(coef, nmax):
    ch = np.arange(0, nmax, dtype=float)
    v = coef[0] + coef[1] * ch + coef[2] * ch ** 2
    return bool(np.all(np.diff(np.sqrt(np.maximum(v, 0.0))) >= -1e-12))


#: `V11`: ниже этой энергии модель разрешения никем не спрашивается (самая
#: низкая линия корпуса — 26.3 кэВ у Am-241), а сетка подгонки там уже
#: вырождена: у калибровок со свободным членом −10…−27 кэВ первые каналы дают
#: ОТРИЦАТЕЛЬНУЮ энергию, модель возвращает там ноль, и относительный вес ловит
#: этот ноль как самую важную точку. То же число стоит в `res_apply.E_MIN`.
E_MIN_PHYSICAL = 15.0


def working_grid(ecal, nmax, count=400):
    u"""Каналы, на которых модель вообще имеет смысл: E ≥ `E_MIN_PHYSICAL` и
    шкала растёт. Возвращает `None`, если таких каналов почти не осталось."""
    ch = np.linspace(1.0, nmax - 1.0, 4 * count)
    e = np.array([ecal.energy(c) for c in ch])
    sl = np.array([abs(ecal.dEdch(c)) for c in ch])
    ok = (e >= E_MIN_PHYSICAL) & (sl > 1e-9)
    if ok.sum() < 20:
        return None
    return np.linspace(float(ch[ok][0]), float(ch[ok][-1]), count)


def fwhm_channel_coef_relative(ecal, res_fn, nmax):
    u"""(коэффициенты, причина) для ПШПВ[ch] = √(c0 + c1·ch + c2·ch²),
    невязка ОТНОСИТЕЛЬНАЯ (вес 1/ПШПВ²) на ФИЗИЧНОЙ части шкалы — правило `V11`.

    Причина — текст, если правило свернуло на запасной путь, иначе `None`;
    коэффициенты `None`, если шкала вырождена и подгонять не на чем.
    """
    ch = working_grid(ecal, nmax)
    if ch is None:
        return None, u'шкала вырождена'
    fw = np.array([res_fn(ecal.energy(c)) / max(abs(ecal.dEdch(c)), 1e-9)
                   for c in ch])
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


def fwhm_channel_coef_plain(ecal, res_fn, nmax):
    u"""ПРЕЖНЕЕ правило переноса — обычный МНК по ПШПВ² на всей шкале каналов.

    Оставлено не для употребления, а как запасной путь на вырожденной шкале и
    как плечо для замера: им построен весь корпус до 06.09.2026.
    """
    ch = np.linspace(1.0, nmax - 1, 400)
    fw = np.array([res_fn(ecal.energy(c)) / max(abs(ecal.dEdch(c)), 1e-9) for c in ch])
    for order in (2, 1):
        A = np.vstack([ch ** i for i in range(order + 1)]).T
        coef, *_ = np.linalg.lstsq(A, fw ** 2, rcond=None)
        coef = np.concatenate([coef, np.zeros(3 - len(coef))])
        if monotone_ch(coef, nmax) and (coef[0] + coef[1] * ch[-1] + coef[2] * ch[-1] ** 2) > 0:
            return coef
    k = float((fw ** 2 * ch).sum() / max((ch * ch).sum(), 1e-9))
    return np.array([0.0, k, 0.0])


def fwhm_channel_coef(ecal, res_fn, nmax):
    u"""SqrtFwhmCalibration хранит FWHM[канал] = √(c0 + c1·ch + c2·ch²),
    поэтому модель в кэвах проецируется в каналы через dE/dch.

    ⛔ **Невязка ОТНОСИТЕЛЬНАЯ, а не в кэвах** (`V11`, внесено 06.09.2026
    решением Amber). Обычный МНК меряет невязку в кэвах и потому всю точность
    тратит на верх шкалы, где ПШПВ большая; свобода многочлена уходит в `c0`, и
    внизу остаётся ПОЛКА √c0. Мерено на всех 129 спектрах корпуса (худшее
    расхождение записанной кривой с моделью СВОЕЙ группы на 40…2700 кэВ):
    медиана **50.7 % → 1.6 %**, 90-й процентиль **100 % → 4.5 %**, худший
    (`OBS_UGlass`) **245 % → 36 %**; лучше стало у 129 спектров из 129, хуже ни
    у одного. На точке 59.5 кэВ, которой писана строка: 90-й процентиль
    отклонения 100 % → **2 %**, худший 187 % → **3 %**.

    ⚠ **Замер 24.08.2026, отвергавший этот вес (`B24`), мерил ДРУГОЕ.** Там вес
    1/ПШПВ² клали на сетку `linspace(1, nmax−1)` ЦЕЛИКОМ, а у калибровок со
    свободным членом −10…−27 кэВ первые каналы дают отрицательную энергию:
    модель возвращает там ноль, и относительный вес ловит этот ноль как самую
    важную точку — отсюда и промах −71 % на 60 кэВ. Сетка здесь начинается там,
    где энергия физична (`working_grid`), и промаха нет.

    ⚠ Посылка строки «у ЛИНЕЙНОЙ шкалы перенос точен» ЗАМЕРОМ НЕ ПОДТВЕРДИЛАСЬ:
    у 68 спектров с кривизной шкалы < 0.5 % прежнее правило давало медиану
    54.8 %, то есть ровно столько же, сколько у кривых. Дело не в кривизне
    шкалы, а в весе.
    """
    coef, _why = fwhm_channel_coef_relative(ecal, res_fn, nmax)
    if coef is None:
        # Вырожденная шкала: молча вернуть ничего нельзя — у вызывающего нет
        # ветки на `None`, и прежнее правило здесь хотя бы что-то даёт.
        return fwhm_channel_coef_plain(ecal, res_fn, nmax)
    return coef
