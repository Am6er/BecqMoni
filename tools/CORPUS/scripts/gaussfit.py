# -*- coding: utf-8 -*-
"""Gauss-Newton fit of a single Gaussian on a linear background, numpy only.

Poisson weights (w = 1/max(y,1)) so that a peak sitting on a big Compton
continuum is not dominated by the continuum's absolute scale.

⛔ `V13`, 25.08.2026: ДЕМПФЕР ДЕРЖАЛ НЕ ТО, и фит расходился на сильных пиках.
Прежний демпфер проверял ровно два условия — что σ осталась положительной и что
центр не убежал из окна, — и НИЧЕГО не знал ни про знак амплитуды, ни про χ².
На узком сильном пике полный шаг Гаусса-Ньютона первым же движением роняет σ,
следующим уводит амплитуду в минус, χ² при этом растёт на четыре порядка, а на
выходе `A <= 0` возвращало `None` — и читатель (`corpus_calib.match_lines`)
читал это как «ЛИНИИ НЕТ». Трасса `G1S16_Ba133_P5`, линия 80.9 кэВ, пик в
191 220 отсчётов:

    затравка   A=1.58e5  σ=2.25  χ²=1.58e6
    шаг 0      A=1.37e5  σ=2.75  χ²=1.65e5   <- сюда и надо было сойтись
    шаг 1      A=2.7e4   σ=0.30  χ²=1.17e6
    шаг 2      A=-4.4e6  σ=18.6  χ²=9.1e9    -> демпфер исчерпан, None

Теперь шаг ПРИНИМАЕТСЯ, только если он допустим (амплитуда положительна, σ в
разумных пределах вокруг затравки, центр в окне) И χ² не вырос. Это обычный
поиск по лучу: λ делится пополам, пока такой шаг не найдётся; если не нашёлся —
мы в минимуме, а не в расходимости, и выходим с тем, что есть.

⛔ ОТКАЗ ФИТА И ОТСУТСТВИЕ ЛИНИИ — РАЗНЫЕ ВЕЩИ, и молчаливый `None` был ровно
тем дефектом, из-за которого корпус калибровался не по тем линиям. Исход фита
называется явно: `fit_gauss_ex` / `fit_peak_ex` возвращают пару
(результат, признак), признак последнего вызова лежит в `gaussfit.LAST`, а
счётчик исходов за прогон — в `gaussfit.STATS` (обнуляется `reset_stats()`).
Старые `fit_gauss` / `fit_peak` возвращают только результат — их читателей
полтора десятка, и ломать их незачем.
"""
import numpy as np

FWHM_SIGMA = 2.0 * np.sqrt(2.0 * np.log(2.0))

# --- исходы фита (`V13`) ---------------------------------------------------
#: сошлось, результат есть
OK = 'ok'
#: окно короче шести каналов — фитить нечем, это НЕ отказ фита
SHORT_WINDOW = 'окно'
#: `lstsq` отказал либо шаг вышел нечисловым
SINGULAR = 'вырождение'
#: НИ ОДИН шаг не принят: ни уменьшить χ², ни остаться допустимым не удалось
NOCONV = 'не сошлось'
#: сошлось в яму: амплитуда неположительна — линии в окне нет
FLAT = 'нет пика'
#: минимум лежит ЗА пределом σ: гаусс уполз шириной во всё окно (или в нуль),
#: то есть описывает не пик, а изгиб континуума. Тоже отказ, не «линии нет».
BOUND = 'предел σ'

STATUSES = (OK, SHORT_WINDOW, SINGULAR, NOCONV, FLAT, BOUND)

#: исход последнего вызова (как errno)
LAST = OK
#: счётчик исходов за прогон
STATS = dict((s, 0) for s in STATUSES)


def reset_stats():
    """Обнулить счётчик исходов."""
    for s in STATUSES:
        STATS[s] = 0


def _done(status):
    global LAST
    LAST = status
    STATS[status] = STATS.get(status, 0) + 1
    return status


# --- пределы, в которых демпферу разрешено двигать параметры ---------------
#: σ уже трети канала не бывает ни у одного прибора корпуса (у HPGe на 8192
#: каналах самая узкая линия занимает около двух).
SIGMA_FLOOR = 0.35
#: и σ, и затравка приходят из модели разрешения; уйти от неё в разы — значит
#: фитить не ту структуру.
#:
#: ⛔ ВЕРХНИЙ ПРЕДЕЛ ЛОВИТ РАЗБЕГ, А НЕ ФАНТОМЫ, и это ИЗМЕРЕНО (`V13`,
#: `gaussfit_check.py`, стадия 1 по всему корпусу, понятная часть 81 спектр).
#: Затянуть его так, чтобы отсечь гаусс, севший на изгиб континуума, нельзя:
#: вместе с фантомами уходят настоящие линии, у которых измеренная ширина
#: честно шире модельной (бленды).
#:
#:     предел σ   опор стало   вернулось   из них с вершиной   ПОТЕРЯНО
#:        1.2         400          16              5              89
#:        1.3         426          20              5              67
#:        1.5         458          26              5              41
#:        2.0         505          47              8              15
#:        5.0         556          86              8               3
#:
#: Поэтому предел ставится ШИРЕ ширинного фильтра читателя (`match_lines`
#: пропускает 0.35…2.6 ПШПВ): он обязан ловить только разбег σ в разы, а
#: отбраковка широких фитов — дело гейта, а не демпфера (строка `S99`).
SIGMA_LO_FRAC = 0.2
SIGMA_HI_FRAC = 3.0
#: доля предела, в которой σ считается «упёршейся»: фит, чей минимум лежит за
#: пределом, ползёт к нему крошечными шагами и останавливается вплотную.
BOUND_TOL = 0.02
#: сколько раз делить λ пополам, прежде чем признать, что шага нет
LINE_SEARCH = 12


def _chi2(p, xc, y, w):
    A, dmu, s, c0, c1 = p
    if not np.isfinite(p).all() or s <= 0.0:
        return np.inf
    model = A * np.exp(-0.5 * ((xc - dmu) / s) ** 2) + c0 + c1 * xc
    r = (y - model) * w
    v = float((r * r).sum())
    return v if np.isfinite(v) else np.inf


def _admissible(q, s_lo, s_hi, span):
    """Допустим ли шаг: амплитуда положительна, σ в пределах, центр в окне."""
    if not np.isfinite(q).all():
        return False
    return (q[0] > 0.0) and (s_lo <= q[2] <= s_hi) and (abs(q[1]) < 0.6 * span)


def fit_gauss_ex(x, y, mu0, sigma0, iterations=40):
    """(dict|None, признак). Признак — одна из `STATUSES`.

    dict: mu, sigma, fwhm, amp, area, b0, b1, sig, chi2ndf, steps, status.
    """
    x = np.asarray(x, dtype=float)
    y = np.asarray(y, dtype=float)
    if x.size < 6:
        return None, _done(SHORT_WINDOW)
    xc = x - mu0
    span = float(xc[-1] - xc[0])
    # initial linear background from the two outer thirds
    edge = max(2, x.size // 6)
    xe = np.concatenate([xc[:edge], xc[-edge:]])
    ye = np.concatenate([y[:edge], y[-edge:]])
    try:
        b1, b0 = np.polyfit(xe, ye, 1)
    except Exception:
        b0, b1 = float(np.median(ye)), 0.0
    amp = max(float(y.max() - (b0 + b1 * 0.0)), 1.0)
    sigma0 = float(sigma0)
    s_lo = max(SIGMA_FLOOR, SIGMA_LO_FRAC * sigma0)
    s_hi = SIGMA_HI_FRAC * max(sigma0, SIGMA_FLOOR)
    p = np.array([amp, 0.0, float(np.clip(sigma0, s_lo, s_hi)),
                  float(b0), float(b1)], dtype=float)
    w = 1.0 / np.sqrt(np.maximum(y, 1.0))

    chi2 = _chi2(p, xc, y, w)
    taken = 0
    for _ in range(iterations):
        A, dmu, s, c0, c1 = p
        t = (xc - dmu) / s
        g = np.exp(-0.5 * t * t)
        model = A * g + c0 + c1 * xc
        r = (y - model) * w
        J = np.empty((x.size, 5))
        J[:, 0] = g * w
        J[:, 1] = A * g * t / s * w
        J[:, 2] = A * g * t * t / s * w
        J[:, 3] = w
        J[:, 4] = xc * w
        try:
            step, *_ = np.linalg.lstsq(J, r, rcond=None)
        except np.linalg.LinAlgError:
            return (None, _done(SINGULAR)) if taken == 0 else _finish(
                p, xc, y, w, mu0, taken)
        if not np.isfinite(step).all():
            return (None, _done(SINGULAR)) if taken == 0 else _finish(
                p, xc, y, w, mu0, taken)

        # ⛔ ДЕМПФЕР (`V13`): шаг принимается, только если он допустим И χ² не
        # вырос. Прежний вариант проверял лишь допустимость и уводил фит в
        # минус по амплитуде, откуда возврата не было.
        lam = 1.0
        q = None
        chi2q = np.inf
        for _k in range(LINE_SEARCH):
            cand = p + lam * step
            if _admissible(cand, s_lo, s_hi, span):
                c = _chi2(cand, xc, y, w)
                if c <= chi2 * (1.0 + 1e-12):
                    q, chi2q = cand, c
                    break
            lam *= 0.5
        if q is None:
            # уменьшить χ², не выходя из пределов, нечем: либо это минимум
            # (шаги уже были), либо фит с места не сдвинулся — и вот это отказ.
            break
        p, chi2 = q, chi2q
        taken += 1
        if np.max(np.abs(lam * step[:3]) / np.maximum(np.abs(p[:3]), 1e-6)) < 1e-6:
            break

    if taken == 0:
        return None, _done(NOCONV)
    if p[2] >= s_hi * (1.0 - BOUND_TOL) or p[2] <= s_lo * (1.0 + BOUND_TOL):
        # минимум за пределом: это не пик, а то, что фиту разрешено описывать
        # только вылезая из окна. Отказ, и отказ НАЗВАННЫЙ.
        return None, _done(BOUND)
    return _finish(p, xc, y, w, mu0, taken)


def _amp_error(p, xc, y, w, chi2ndf):
    """σ(A) из ковариации — насколько амплитуда вообще ОПРЕДЕЛЕНА.

    ⛔ `V13`: `sig` ниже — счётная значимость площади над подогнанным фоном, и
    она НЕ ОТЛИЧАЕТ пик от изгиба континуума: гаусс шириной во всё окно набирает
    огромную «площадь» и получает z в сотни (`G1S24_Y88_P5` 198.1 кэВ: z = 912
    там, где отсчёты растут поперёк окна монотонно и вершины нет вовсе). У такой
    гауссианы столбец матрицы почти коллинеарен фоновым, и ковариация это видит:
    A/σ(A) обваливается. Величина считается ЗДЕСЬ и кладётся в `sig_fit`;
    гейтом она пока не служит — это отдельная строка (`S99`), потому что смена
    порога отбора двигает калибровку всего корпуса.
    """
    A, dmu, s, c0, c1 = p
    t = (xc - dmu) / s
    g = np.exp(-0.5 * t * t)
    J = np.empty((xc.size, 5))
    J[:, 0] = g * w
    J[:, 1] = A * g * t / s * w
    J[:, 2] = A * g * t * t / s * w
    J[:, 3] = w
    J[:, 4] = xc * w
    try:
        cov = np.linalg.pinv(J.T.dot(J))
    except np.linalg.LinAlgError:
        return float('inf')
    v = float(cov[0, 0]) * max(chi2ndf, 1.0)
    return np.sqrt(v) if v > 0 else float('inf')


def _finish(p, xc, y, w, mu0, taken):
    A, dmu, s, c0, c1 = p
    if A <= 0 or s <= 0:
        return None, _done(FLAT)
    mu = mu0 + dmu
    model = A * np.exp(-0.5 * ((xc - dmu) / s) ** 2) + c0 + c1 * xc
    resid = (y - model) * w
    chi2ndf = float((resid ** 2).sum() / max(xc.size - 5, 1))
    area = A * s * np.sqrt(2.0 * np.pi)
    # amplitude significance against the local background level
    base = max(c0 + c1 * dmu, 1.0)
    # counting significance of the net area vs background under the peak
    n_bg = base * s * np.sqrt(2.0 * np.pi)
    sig = area / np.sqrt(max(area + 2.0 * n_bg, 1.0))
    err = _amp_error(p, xc, y, w, chi2ndf)
    sig_fit = float(A / err) if np.isfinite(err) and err > 0 else 0.0
    return dict(mu=float(mu), sigma=float(s), fwhm=float(s * FWHM_SIGMA), amp=float(A),
                area=float(area), b0=float(c0), b1=float(c1), sig=float(sig),
                sig_fit=sig_fit, chi2ndf=chi2ndf, steps=int(taken),
                status=OK), _done(OK)


def fit_gauss(x, y, mu0, sigma0, iterations=40):
    """Returns dict(mu, sigma, amp, area, b0, b1, sig, chi2ndf) or None.

    Почему отказ не различается: см. `fit_gauss_ex`, признак там.
    """
    return fit_gauss_ex(x, y, mu0, sigma0, iterations=iterations)[0]


def _window(counts, mu0, sigma0, window, nmin):
    n = len(counts)
    half = max(nmin // 2, int(round(window * sigma0)))
    lo = int(max(0, round(mu0 - half)))
    hi = int(min(n - 1, round(mu0 + half)))
    if hi - lo + 1 < nmin:
        return None
    return lo, hi


def fit_peak_ex(counts, mu0, sigma0, window=2.6, nmin=8):
    """(dict|None, признак) — фит в окне ±window·σ вокруг mu0."""
    lim = _window(counts, mu0, sigma0, window, nmin)
    if lim is None:
        return None, _done(SHORT_WINDOW)
    lo, hi = lim
    x = np.arange(lo, hi + 1, dtype=float)
    return fit_gauss_ex(x, counts[lo:hi + 1], mu0, sigma0)


def fit_peak(counts, mu0, sigma0, window=2.6, nmin=8):
    return fit_peak_ex(counts, mu0, sigma0, window=window, nmin=nmin)[0]
