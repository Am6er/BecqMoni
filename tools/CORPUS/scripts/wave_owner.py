# -*- coding: utf-8 -*-
u"""`S91` — ЧЬЯ волна: прибора или сцены. Разрез по сходству формы, а не по величине.

⛔ **Зачем ещё один разрез.** `wave_shape.py` ответил на вопрос «волна ли это
калибровки» и ответил «нет»: регрессия на производные ln(измерения) берёт
единицы процентов дисперсии. Осталось назвать источник, и единственный
непроверенный кандидат — **рассеяние до кристалла и вещество вокруг него**,
которых в модели нет вовсе. У этого кандидата есть проверяемое следствие, и
проверить его можно НЕ СЧИТАЯ новой физики:

  * если волна родится **в приборе** (окно, оправа, отражатель, световод), её
    форма принадлежит ДЕТЕКТОРУ и обязана повторяться от спектра к спектру
    одной группы, какой бы источник в ней ни стоял;
  * если она родится **в сцене** (проба, сосуд, подставка, обстановка), она
    обязана следовать сцене и НЕ повторяться у разных постановок одной группы.

Мерка — попарная корреляция ФОРМЫ волны (детрендированного отношения
модель/измерение) на общей энергетической сетке. Плечи:

  1. **тот же прибор, та же эпоха** — железо и модель разрешения общие;
  2. **тот же прибор, другая эпоха** (`G1S16`↔`G1S24`) — железо то же, восемь
     лет и 10 % ширины между ними;
  3. **тот же прибор И ТА ЖЕ ПОСТАНОВКА** — общее и железо, и геометрия; это
     плечо закрывает лазейку «рассеяние зависит ещё и от того, где стоит
     источник»;
  4. **тот же нуклид, ПРИБОР ДРУГОЙ** — сцена похожа, железо другое;
  5. **фон** — ни того ни другого общего.

Волна прибора: (1)–(3) заметно выше (5), а (4) на уровне фона.
Волна пробы: (4) выше (5), а (1)–(3) на уровне фона.

✅ **ИЗМЕРЕНО 24.08.2026, база `out_v5`, и ответ однозначен: волна НЕ ПРИБОРНАЯ.**
Понятная часть (81 спектр, с матрицей): прибор даёт сверх фона **−0.004**,
прибор+постановка **−0.002**, эпоха не решает ничего (+0.061 против +0.063), а
нуклид даёт **+0.082** (перестановочный тест p = 0.021 на 36 парах).
Непонятная часть (40 спектров, девятнадцать групп, БЕЗ матрицы): прибор
**−0.043**, нуклид **+0.651**, |r| > 0.5 у 79 % пар. ⛔ Части не складывать: у
непонятной образ строится из одних пиков, и часть её согласия — просто
непосчитанный континуум своих же линий.

Отсюда: кандидат «рассеяние до кристалла и вещество вокруг него» ПРОТИВОРЕЧИТ
измерению — это свойство ДЕТЕКТОРА, а детектор не объясняет ничего, даже при
той же постановке источника и даже у одного прибора спустя восемь лет.

⚠ Детренд и полоса — ТЕ ЖЕ, что у `wave_shape.py` (полином 6-й степени,
60…450 кэВ), и берутся из него импортом: два разных детренда сравнивали бы
инструменты, а не формы.

⚠ Корреляция считается на ОБЩЕЙ сетке по энергии, а не по каналам: у корпуса
1024…16384 канала, и поканальное сравнение мерило бы разницу оцифровки.

    python tools/CORPUS/scripts/wave_owner.py <каталог слепков --dump-curves>
                                              [--from=60] [--to=450] [--grid=120]
"""
from __future__ import print_function

import glob
import os
import re
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import wave_shape                                       # noqa: E402


#: ⛔ ГРУППА КОРПУСА — НЕ ПРИБОР, и на этом здесь можно было погореть.
#: `G1S16` и `G1S24` — ОДИН И ТОТ ЖЕ прибор (`УДС-ГЦ-63х63-USB №0086-16`,
#: `CONFIGNAME` в оба года `Гамма-1С №0221-16`), разделённый по ЭПОХАМ 2016 и
#: 2024: за восемь лет ширина ушла на 9…12 % (6.58 против 7.18 % на 662 кэВ), и
#: модель разрешения им нужна разная. «Два экземпляра прибора» было ошибкой
#: чтения заголовков и снято 15.08.2026 (`g1s_split_check.py`).
#:
#: Для вопроса «чья волна» разница решающая: из 137 пар «тот же нуклид, другая
#: группа» 102 приходятся на `G1S16`↔`G1S24`, то есть прибор в них НЕ меняется,
#: и плечо, посчитанное по группам, прибор не разделяет вовсе.
INSTRUMENT = {'G1S16': 'G1S', 'G1S24': 'G1S'}


def group_of(key, manifest):
    return manifest.get(key, ('?', '?'))[0]


def instrument_of(key, manifest):
    det = group_of(key, manifest)
    return INSTRUMENT.get(det, det)


def nuclide_of(key, manifest):
    return manifest.get(key, ('?', '?'))[1]


#: Постановка — хвост ключа корпуса: `_P5`, `_P25`, `_Mar`, `_Petri`,
#: `_Denta120`, `_0cm`… Нужна затем, что рассеяние ДО кристалла зависит не
#: только от прибора, но и от того, где стоит источник: у одного и того же
#: детектора путь кванта сквозь оправу разный на 5 и на 25 см. Плечо «тот же
#: прибор, ТА ЖЕ постановка» закрывает эту лазейку — там общее и железо, и
#: геометрия, и если волна от вещества на пути, она обязана совпадать.
PLACEMENT = re.compile(r'_(P\d+|\d+cm|Mar\w*|Petri\w*|Denta\w*|WT\d+)(?:_\d+)?$')


def placement_of(key):
    m = PLACEMENT.search(key)
    return m.group(1) if m else '?'


def read_manifest():
    u"""Ключ -> (группа, объявленный состав) из манифеста корпуса."""
    import csv
    import io
    path = os.path.join(HERE, os.pardir, 'corpus', 'manifest.csv')
    out = {}
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            key = list(row.values())[0]
            what = ';'.join(sorted(
                [x for x in (row.get('nuclides') or '').split(';') if x]
                + [x for x in (row.get('chains') or '').split(';') if x]))
            out[key] = (row.get('det', '?'), what or '?')
    return out


def shape(path, lo, hi, grid):
    u"""Форма волны на общей сетке энергий; None — считать не по чему.

    ⛔ Взвешивания по счёту здесь НЕТ нарочно, в отличие от `one_line`: там
    меряется ВЕЛИЧИНА волны, и пустой участок её раздувает, а здесь меряется
    СХОДСТВО формы, и вес исказил бы корреляцию в пользу самых ярких участков —
    то есть окрестностей пиков, где сравнивались бы уже не волны, а линии.
    """
    ch, kev, net, model = wave_shape.load(path)
    band = (kev >= lo) & (kev <= hi) & (net > 0.0)
    kev, net, model = kev[band], net[band], model[band]
    if len(kev) < 60:
        return None
    w = wave_shape.detrend(kev, model / net - 1.0)
    g = np.linspace(lo, hi, grid)
    y = np.interp(g, kev, w)
    y = y - y.mean()
    s = y.std()
    return (y / s) if s > 1e-12 else None


def main(argv):
    args = [a for a in argv[1:] if not a.startswith('--')]
    keys = dict(a[2:].split('=', 1) for a in argv[1:] if a.startswith('--') and '=' in a)
    if not args:
        raise SystemExit(__doc__)
    lo = float(keys.get('from', 60.0))
    hi = float(keys.get('to', 450.0))
    grid = int(keys.get('grid', 120))

    paths = []
    for a in args:
        paths.extend(sorted(glob.glob(os.path.join(a, '*_curves.csv')))
                     if os.path.isdir(a) else [a])

    manifest = read_manifest()
    shapes = {}
    for p in paths:
        key = re.sub(r'_curves\.csv$', '', os.path.basename(p))
        y = shape(p, lo, hi, grid)
        if y is not None:
            shapes[key] = y

    print(u'полоса %g…%g кэВ, сетка %d точек, спектров %d'
          % (lo, hi, grid, len(shapes)))
    if len(shapes) < 4:
        raise SystemExit(u'нечего сравнивать')

    # Четыре плеча, а не три: прибор и ЭПОХА прибора разведены нарочно (см.
    # INSTRUMENT). Пара «тот же нуклид» берётся только там, где прибор РАЗНЫЙ, —
    # иначе плечо мерило бы нуклид и прибор разом.
    same_epoch, other_epoch, same_nuc, other = [], [], [], []
    same_place = []
    ks = sorted(shapes)
    for i in range(len(ks)):
        for j in range(i + 1, len(ks)):
            a, b = ks[i], ks[j]
            c = float(np.dot(shapes[a], shapes[b]) / len(shapes[a]))
            ia, ib = instrument_of(a, manifest), instrument_of(b, manifest)
            na, nb = nuclide_of(a, manifest), nuclide_of(b, manifest)
            if ia == ib:
                (same_epoch if group_of(a, manifest) == group_of(b, manifest)
                 else other_epoch).append((c, a, b))
                pa, pb = placement_of(a), placement_of(b)
                if pa == pb and pa != '?' and na != nb:
                    same_place.append((c, a, b))
            elif na == nb and na != '?':
                same_nuc.append((c, a, b))
            else:
                other.append((c, a, b))
    same_det = same_epoch + other_epoch

    def stat(rows, title):
        if not rows:
            print(u'  %-34s пар нет' % title)
            return float('nan')
        v = np.array([r[0] for r in rows])
        print(u'  %-34s пар %5d   медиана %+.3f   |r|>0.5 у %.0f %%'
              % (title, len(v), float(np.median(v)),
                 100.0 * float(np.mean(np.abs(v) > 0.5))))
        return float(np.median(v))

    print()
    print(u'СХОДСТВО ФОРМЫ ВОЛНЫ (корреляция; 1 — одна и та же волна, 0 — ничего общего)')
    m0 = stat(same_epoch, u'тот же ПРИБОР, та же эпоха')
    me = stat(other_epoch, u'тот же ПРИБОР, другая эпоха')
    m1 = stat(same_det, u'  оба вместе: прибор один')
    mp = stat(same_place, u'тот же прибор И ТА ЖЕ постановка')
    m2 = stat(same_nuc, u'тот же НУКЛИД, ПРИБОР ДРУГОЙ')
    m3 = stat(other, u'фон: ни прибор, ни нуклид')
    print()
    if not (np.isnan(m1) or np.isnan(m3)):
        print(u'  прибор даёт сверх фона: %+.3f' % (m1 - m3))
    if not (np.isnan(m2) or np.isnan(m3)):
        print(u'  нуклид даёт сверх фона: %+.3f' % (m2 - m3))
    if not (np.isnan(mp) or np.isnan(m3)):
        print(u'  прибор+постановка сверх фона: %+.3f' % (mp - m3))
    if not (np.isnan(m0) or np.isnan(me)):
        print(u'  эпоха того же прибора: %+.3f против %+.3f' % (m0, me))

    # Поимённо по группам: одна группа с высокой внутренней корреляцией при
    # низких прочих — уже находка, даже если медиана по корпусу её топит.
    print()
    print(u'ПО ГРУППАМ (только там, где пар не меньше трёх)')
    by = {}
    for c, a, b in same_det:
        by.setdefault(group_of(a, manifest), []).append(c)
    print(u'  %-12s %5s %9s' % (u'группа', u'пар', u'медиана'))
    for det in sorted(by):
        if len(by[det]) < 3:
            continue
        print(u'  %-12s %5d %+9.3f' % (det, len(by[det]), float(np.median(by[det]))))

    # ⚠ Значимость плеча «нуклид»: пар там ДЕСЯТКИ против сотен в фоне, и
    # разность медиан на глаз ничего не значит. Перестановочный тест: метки
    # тасуются, считается доля перестановок, где разность не меньше наблюдённой.
    # Своими руками, чтобы не тянуть scipy, — здесь его нет ни у кого.
    if same_nuc and other:
        obs = float(np.median([r[0] for r in same_nuc])
                    - np.median([r[0] for r in other]))
        pool = np.array([r[0] for r in same_nuc] + [r[0] for r in other])
        n = len(same_nuc)
        rng = np.random.RandomState(20260824)
        hits = 0
        trials = 20000
        for _ in range(trials):
            rng.shuffle(pool)
            if np.median(pool[:n]) - np.median(pool[n:]) >= obs:
                hits += 1
        print(u'  перестановочный тест плеча «нуклид»: разность медиан %+.3f, '
              u'p = %.4f (%d перестановок)' % (obs, (hits + 1.0) / (trials + 1.0), trials))

    print()
    print(u'САМЫЕ ПОХОЖИЕ ПАРЫ (форма волны почти одна)')
    for c, a, b in sorted(same_det + same_nuc + other, key=lambda r: -r[0])[:12]:
        # ⚠ Ярлык считается по ПРИБОРУ, а не по группе: пара `G1S16`↔`G1S24` —
        # один и тот же прибор в двух эпохах, и подпись «общее: нуклид» на ней
        # была бы ровно той ошибкой, ради которой заведена карта INSTRUMENT.
        if instrument_of(a, manifest) == instrument_of(b, manifest):
            tag = (u'ПРИБОР' if group_of(a, manifest) == group_of(b, manifest)
                   else u'ПРИБОР (другая эпоха)')
            if nuclide_of(a, manifest) == nuclide_of(b, manifest):
                tag += u' + нуклид'
        elif nuclide_of(a, manifest) == nuclide_of(b, manifest):
            tag = u'нуклид (приборы РАЗНЫЕ)'
        else:
            tag = u'—'
        print(u'  %+.3f  %-24s %-24s  общее: %s' % (c, a, b, tag))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main(sys.argv)
