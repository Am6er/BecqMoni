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

from gaussfit import fit_peak, FWHM_SIGMA


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
                width_lo=0.45, width_hi=2.0):
    """[(канал, табличная энергия, ...)] — что реально нашлось.

    lines — результат calibrate.curate: (энергия группы, метка, чистота,
    табличная энергия).
    """
    n = len(counts)
    out = []
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
        r = fit_peak(counts, ch0, fwhm_ch / FWHM_SIGMA, window=2.2)
        if r is None or r['sig'] < min_sig:
            continue
        if abs(r['mu'] - ch0) > tol_fwhm * fwhm_ch:
            continue
        ratio = r['fwhm'] / fwhm_ch
        if ratio < width_lo or ratio > width_hi:
            continue
        out.append(dict(ch=r['mu'], e_ref=e_ref, label=label, purity=purity,
                        fwhm=r['fwhm'], sig=r['sig'], area=r['area']))
    out.sort(key=lambda a: a['ch'])
    dedup = []
    for a in out:
        if dedup and abs(a['ch'] - dedup[-1]['ch']) < 0.4 * min(a['fwhm'], dedup[-1]['fwhm']):
            if a['sig'] > dedup[-1]['sig']:
                dedup[-1] = a
            continue
        dedup.append(a)
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


def choose(stored, pairs, res_a, nmax, max_order=2, keep_margin=0.9):
    """Хранившаяся калибровка остаётся, если поправка не улучшила невязку
    заметно (на десятую часть): менять калибровку ради шума незачем.

    У каждого кандидата должна остаться хотя бы одна степень свободы. Иначе
    прямая по двум точкам проходит через них ровно, получает нулевую невязку и
    побеждает всегда — а за пределами этих двух точек может уходить куда угодно.
    """
    n = len(pairs)
    cands = [('stored', stored)]
    # Поправка усиления по одной линии — вырожденный случай (параметр один,
    # точка одна, невязка ноль), и обычно так делать нельзя. Исключение: очень
    # значимая линия и поправка в пределах пяти процентов. Это ровно то, что
    # делает стабилизатор прибора по опорному пику, и без этого урановое стекло
    # AS1 Pro оставалось с промахом 36 кэВ на 1764 кэВ.
    strongest = max(a['sig'] for a in pairs) if pairs else 0.0
    if n >= 2 and strongest >= 20.0 or n == 1 and strongest >= 40.0:
        cal = affine_of(stored, pairs, nmax, scale_only=True)
        if cal is not None and (n >= 2 or gain_drift(cal, stored, nmax) <= 0.05):
            cands.append(('gain', cal))
    if n >= 3:
        cal = affine_of(stored, pairs, nmax)
        if cal is not None:
            cands.append(('affine', cal))
    for order in range(1, max_order + 1):
        if n < order + 2:
            continue
        cal = poly_of(pairs, order, nmax)
        if cal is not None:
            cands.append(('poly%d' % order, cal))

    base = residual_fwhm(stored, pairs, res_a)
    best_tag, best_cal, best_score = 'stored', stored, base
    for tag, cal in cands[1:]:
        score = residual_fwhm(cal, pairs, res_a)
        drift = gain_drift(cal, stored, nmax)
        if drift > 0.06:
            score += 20.0 * (drift - 0.06)       # усиление врёт на проценты, не в разы
        if score < best_score * keep_margin:
            best_tag, best_cal, best_score = tag, cal, score
    return best_tag, best_cal, best_score


def calibrate(counts, stored_coef, lines, res_a, max_order=2):
    """Полный цикл: грубое сопоставление -> поправка -> точное сопоставление.

    Возвращает (Ecal, пары, res_a, метка режима).
    """
    nmax = len(counts)
    stored = Ecal(stored_coef, nmax)

    cal = stored
    pairs = []
    tag = 'stored'
    for tol, wlo, whi in ((2.5, 0.35, 2.6), (1.2, 0.45, 2.0), (0.7, 0.5, 1.8)):
        found = match_lines(counts, cal, lines, res_a, tol_fwhm=tol,
                            width_lo=wlo, width_hi=whi)
        if len(found) < 1:
            break
        pairs = found
        # ширины найденных линий уточняют разрешение
        red = [a['fwhm'] * abs(cal.dEdch(a['ch'])) / np.sqrt(max(a['e_ref'], 5.0))
               for a in found if a['purity'] > 0.85]
        if len(red) >= 2:
            res_a = float(np.median(red))
        step_tag, step_cal, _ = choose(stored, pairs, res_a, nmax, max_order)
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
        # выбросы по MAD и перефитываем, если стало лучше.
        if len(pairs) >= 5:
            d = np.array([cal.energy(a['ch']) - a['e_ref'] for a in pairs])
            mad = float(np.median(np.abs(d - np.median(d)))) * 1.4826
            limit = max(3.0 * mad, 0.35 * res_a * np.sqrt(np.median(
                [a['e_ref'] for a in pairs])))
            keep = [a for a, dd in zip(pairs, d) if abs(dd - np.median(d)) <= limit]
            if 4 <= len(keep) < len(pairs):
                t2, c2, _ = choose(stored, keep, res_a, nmax, max_order)
                if residual_fwhm(c2, keep, res_a) < residual_fwhm(cal, pairs, res_a):
                    cal, tag = c2, t2 + '/robust'
                    pairs = match_lines(counts, cal, lines, res_a, tol_fwhm=0.7,
                                        width_lo=0.5, width_hi=1.8) or keep
    return cal, pairs, res_a, tag


# ---------------------------------------------------------------------------
# 4. FWHM-калибровка в каналах
# ---------------------------------------------------------------------------
def fit_resolution_kev(points):
    """FWHM^2 = c1*E + c2*E^2 по точкам (E, FWHM, вес).

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


def fwhm_channel_coef(ecal, res_fn, nmax):
    """SqrtFwhmCalibration хранит FWHM[канал] = sqrt(c0 + c1*ch + c2*ch^2),
    поэтому модель в кэвах проецируется в каналы через dE/dch."""
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
