# -*- coding: utf-8 -*-
"""`S88` — ЧЕМ объясняется волна отношения модель / измерение.

Волна измерена 19.08.2026: на `Cs 137 в домике 24.11.2022.xml` отношение
модель/измерение колеблется с rms 7.1 % при размахе 47 %, масштаб волны
50…130 кэВ, а сам спектр гладок в пределах счёта. Осталось назвать, ЧТО
расходится. Мерка «та же геометрия без домика» неисполнима — кривую отклика в
папке несут четыре спектра, и второго с той же геометрией нет.

ЗДЕСЬ ДРУГОЙ РАЗРЕЗ, и он не требует второго спектра. Три кандидата дают волну
РАЗНОЙ формы, и различаются они по тому, с чем волна коррелирует:

  * **сдвиг шкалы** (энергокалибровка, дрейф усиления): модель стоит в точке
    E + δ вместо E, отношение r − 1 ≈ δ · d(ln y)/dE — то есть повторяет ПЕРВУЮ
    производную логарифма измерения;
  * **ширина отклика** (модель разрешения, `V2`): свёртка с чуть другой σ даёт
    r − 1 ≈ ½·Δ(σ²) · d²(ln y)/dE² — ВТОРУЮ производную;
  * **недостающая физика** (домик, рассеяние до кристалла, сумм-континуум): своя
    форма, с производными измерения не связанная вовсе.

Поэтому: снять r(E), убрать тренд полиномом (окна скользящего среднего НЕ
применять — грабля `B17`: окно фиксированной ширины само создаёт периодичность),
и посмотреть, какую долю дисперсии объясняет регрессия на две производные.
Высокая доля — волна от ширины и положения отклика; низкая — расходится сама
форма, и чинить надо не калибровку.

    python tools/CORPUS/scripts/wave_shape.py dump.csv [--from=60] [--to=450]

Вход — csv пробы `FsaStackShot --dump=`: ch,keV,net,model,continuum,<слои>.
"""
from __future__ import print_function

import csv
import io
import os
import sys

import numpy as np


def load(path):
    ch, kev, net, model = [], [], [], []
    with io.open(path, encoding='utf-8', newline='') as f:
        for row in csv.DictReader(f):
            ch.append(int(row['ch']))
            kev.append(float(row['keV']))
            net.append(float(row['net']))
            model.append(float(row['model']))
    return (np.array(ch), np.array(kev), np.array(net), np.array(model))


def detrend(x, y, degree=6):
    """Тренд — полином по энергии. Скользящее окно здесь запрещено (`B17`)."""
    c = np.polyfit(x, y, degree)
    return y - np.polyval(c, x)


def derivatives(kev, y, smooth):
    """Первая и вторая производные ln(y) по энергии.

    Сглаживание — полиномом 2-й степени в скользящем окне (Савицкий — Голей
    вручную, чтобы не тянуть scipy): производная измеренной кривой без него
    состоит из одного пуассоновского шума.
    """
    ln = np.log(np.maximum(y, 1e-9))
    half = max(2, int(smooth) // 2)
    n = len(ln)
    d1 = np.zeros(n)
    d2 = np.zeros(n)
    for i in range(n):
        lo, hi = max(0, i - half), min(n, i + half + 1)
        if hi - lo < 5:
            continue
        c = np.polyfit(kev[lo:hi] - kev[i], ln[lo:hi], 2)
        d2[i] = 2.0 * c[0]
        d1[i] = c[1]
    return d1, d2


def report(path, lo_kev, hi_kev):
    ch, kev, net, model = load(path)
    band = (kev >= lo_kev) & (kev <= hi_kev) & (net > 0.0)
    kev, net, model = kev[band], net[band], model[band]
    if len(kev) < 100:
        raise SystemExit('в полосе %g…%g кэВ всего %d каналов' % (lo_kev, hi_kev, len(kev)))

    r = model / net - 1.0
    w = detrend(kev, r)

    print('%s, полоса %g…%g кэВ, каналов %d' % (path, lo_kev, hi_kev, len(kev)))
    print('  отношение модель/измерение: среднее %+.1f %%, rms по тренду %.1f %%, размах %.0f %%'
          % (100.0 * r.mean(), 100.0 * w.std(), 100.0 * (w.max() - w.min())))

    # Масштаб сглаживания производных берётся от ширины волны, а не от ПШПВ:
    # мерить надо ту структуру, о которой спор.
    for smooth in (21, 41, 81):
        d1, d2 = derivatives(kev, net, smooth)
        a = np.column_stack([d1, d2, np.ones(len(d1))])
        coef, _, _, _ = np.linalg.lstsq(a, w, rcond=None)
        fit = a.dot(coef)
        r2 = 1.0 - np.var(w - fit) / np.var(w)

        only1 = np.column_stack([d1, np.ones(len(d1))])
        c1, _, _, _ = np.linalg.lstsq(only1, w, rcond=None)
        r2_1 = 1.0 - np.var(w - only1.dot(c1)) / np.var(w)

        only2 = np.column_stack([d2, np.ones(len(d2))])
        c2, _, _, _ = np.linalg.lstsq(only2, w, rcond=None)
        r2_2 = 1.0 - np.var(w - only2.dot(c2)) / np.var(w)

        # Коэффициент при первой производной — эффективный сдвиг шкалы, кэВ;
        # при второй — половина разницы квадратов σ, кэВ².
        print('  окно %3d кан: R² обе %.3f (сдвиг %.3f, ширина %.3f) | '
              'δE = %+.2f кэВ, Δσ²/2 = %+.1f кэВ²'
              % (smooth, r2, r2_1, r2_2, coef[0], coef[1]))


def one_line(path, lo_kev, hi_kev, smooth=41):
    """Одна строка сводки по спектру; None — считать не по чему.

    ⛔ Разброс считается ВЗВЕШЕННЫМ ПО СЧЁТУ, и это не косметика. Отношение
    модель/измерение в канале с двумя отсчётами шумит на сотни процентов, и
    простое среднеквадратичное меряет там не форму модели, а пустоту: первый же
    прогон по корпусу дал `RC103_Lu176` 755 % при пуассоновском пределе 28 %.
    Вес ∝ net уравнивает вклад участка спектра с его статистической ценой.
    """
    ch, kev, net, model = load(path)
    band = (kev >= lo_kev) & (kev <= hi_kev) & (net > 0.0)
    kev, net, model = kev[band], net[band], model[band]
    if len(kev) < 100:
        return None

    r = model / net - 1.0
    w = detrend(kev, r)
    weight = net / net.sum()

    def wrms(v):
        return float(np.sqrt(np.sum(weight * v * v)))

    d1, d2 = derivatives(kev, net, smooth)
    a = np.column_stack([d1, d2, np.ones(len(d1))])
    coef, _, _, _ = np.linalg.lstsq(a * np.sqrt(weight)[:, None],
                                    w * np.sqrt(weight), rcond=None)
    r2 = 1.0 - wrms(w - a.dot(coef)) ** 2 / wrms(w - np.sum(weight * w)) ** 2

    # Пуассоновский предел той же величины, тем же весом: шум измерения сам по
    # себе двигает отношение, и волну надо читать ПРОТИВ него, а не в пустоте.
    poisson = float(np.sqrt(np.sum(weight / np.maximum(net, 1.0))))
    return dict(name=os.path.basename(path), n=len(kev), mean=100.0 * float(np.sum(weight * r)),
                rms=100.0 * wrms(w), span=100.0 * (w.max() - w.min()),
                r2=r2, dE=coef[0], noise=100.0 * poisson)


def batch(paths, lo_kev, hi_kev):
    rows = []
    for p in paths:
        try:
            row = one_line(p, lo_kev, hi_kev)
        except Exception as exc:                      # noqa: BLE001 — сводка не должна падать на одном файле
            print('  %-38s ОШИБКА %s' % (os.path.basename(p), exc))
            continue
        if row:
            rows.append(row)

    rows.sort(key=lambda x: -x['rms'])
    print('полоса %g…%g кэВ, спектров %d' % (lo_kev, hi_kev, len(rows)))
    print('%-40s %6s %7s %7s %7s %6s %7s' % ('спектр', 'кан', 'сред,%', 'rms,%', 'шум,%', 'R2', 'dE,кэВ'))
    for row in rows:
        print('%-40s %6d %+7.1f %7.1f %7.1f %6.3f %+7.2f'
              % (row['name'][:40], row['n'], row['mean'], row['rms'],
                 row['noise'], row['r2'], row['dE']))

    rms = np.array([x['rms'] for x in rows])
    noise = np.array([x['noise'] for x in rows])
    r2 = np.array([x['r2'] for x in rows])
    print('--- медианы: rms %.1f %%, пуассоновский шум %.1f %%, R2 %.3f; '
          'rms выше шума у %d из %d'
          % (np.median(rms), np.median(noise), np.median(r2),
             int((rms > noise).sum()), len(rows)))


def main(argv):
    args = [a for a in argv[1:] if not a.startswith('--')]
    keys = dict(a[2:].split('=', 1) for a in argv[1:] if a.startswith('--') and '=' in a)
    if not args:
        raise SystemExit(__doc__)

    lo = float(keys.get('from', 60.0))
    hi = float(keys.get('to', 450.0))
    if 'batch' in keys or len(args) > 1:
        batch(args, lo, hi)
        return

    for path in args:
        report(path, lo, hi)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main(sys.argv)
