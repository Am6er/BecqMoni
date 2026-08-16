# -*- coding: utf-8 -*-
u"""Какой ФОРМОЙ описывать разрешение группы (`V2`, остаток).

Зачем. `res_low.py` измерил: ниже 200 кэВ нынешняя модель у сцинтилляторов ШИРЕ
настоящей на 10–40 %. Причина не в точках, а в форме — нынешняя записана как

    ПШПВ² = c1·E + c2·E²,   свободного члена НЕТ НАМЕРЕННО

и отсутствие `c0` объяснено в `corpus_calib.fit_resolution_kev` прямо: опорные
линии почти всех спектров лежали выше 180 кэВ, `c0` ими не определялся, и
подгонка выносила его в плюс — у `AS80x80` выходило 75 % полуширины на 60 кэВ.
Довод был верный ДЛЯ ТЕХ ДАННЫХ. Теперь точки внизу шкалы есть, и вопрос
решается измерением, а не осторожностью.

Что делает. Берёт ТЕ ЖЕ измеренные точки (`res_low.measured_points`), для каждой
группы подгоняет несколько форм на ВСЕЙ шкале и печатает, как каждая ложится на
низ (< 200 кэВ) и на верх отдельно. Ничего не меняет: умолчание модели — решение
Amber, потому что оно двигает базу корпуса.

Формы:

    A  ПШПВ² = c1·E + c2·E²            нынешняя (c0 = 0)
    B  ПШПВ² = c0 + c1·E + c2·E²       со свободным членом
    C  ПШПВ² = c0 + c1·E + c2/E        форма GADRAS (`F5`)
    D  ПШПВ  = a·E^p                   степенная

Мерило — взвешенное СКО относительной невязки (изм − модель)/изм, отдельно по
низу и по верху: кэВ между группами несравнимы, а доля — да.

⚠ Форма обязана оставаться физичной: ПШПВ растёт, а ОТНОСИТЕЛЬНАЯ ширина падает
(за неё отвечает статистика фотоэлектронов). Формы, у которых это нарушено на
20…3000 кэВ, помечаются и в победители не годятся — тот же довод, что уже стоит
в `fit_resolution_kev`.

    python tools/CORPUS/scripts/res_form.py [--min-points=6]
"""
import argparse
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from res_low import measured_points

GRID = np.linspace(20.0, 3000.0, 400)


def fit_a(e, f, w):
    A = np.vstack([e, e ** 2]).T * w[:, None]
    c, *_ = np.linalg.lstsq(A, f ** 2 * w, rcond=None)
    return lambda x: np.sqrt(np.maximum(c[0] * x + c[1] * x ** 2, 1e-9)), c


def fit_b(e, f, w):
    A = np.vstack([np.ones_like(e), e, e ** 2]).T * w[:, None]
    c, *_ = np.linalg.lstsq(A, f ** 2 * w, rcond=None)
    return lambda x: np.sqrt(np.maximum(c[0] + c[1] * x + c[2] * x ** 2, 1e-9)), c


def fit_c(e, f, w):
    A = np.vstack([np.ones_like(e), e, 1.0 / e]).T * w[:, None]
    c, *_ = np.linalg.lstsq(A, f ** 2 * w, rcond=None)
    return lambda x: np.sqrt(np.maximum(c[0] + c[1] * x + c[2] / x, 1e-9)), c


def fit_d(e, f, w):
    A = np.vstack([np.ones_like(e), np.log(e)]).T * w[:, None]
    c, *_ = np.linalg.lstsq(A, np.log(f) * w, rcond=None)
    return lambda x: np.exp(c[0]) * x ** c[1], np.array([np.exp(c[0]), c[1]])


FORMS = [('A нынешняя', fit_a), ('B со свободным', fit_b),
         ('C GADRAS', fit_c), ('D степенная', fit_d)]


def physical(fn):
    u"""ПШПВ растёт, относительная ширина падает — на всей рабочей шкале."""
    v = fn(GRID)
    if not np.all(np.isfinite(v)) or np.any(v <= 0):
        return False
    return bool(np.all(np.diff(v) > 0) and np.all(np.diff(v / GRID) < 1e-12))


def rms(e, f, fn, mask):
    if mask.sum() == 0:
        return float('nan')
    d = (f[mask] - fn(e[mask])) / f[mask]
    return float(np.sqrt(np.mean(d ** 2)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--min-points', type=int, default=6)
    ap.add_argument('--split', type=float, default=200.0, help=u'граница низа, кэВ')
    args = ap.parse_args()

    pts = measured_points()
    by = {}
    for a in pts:
        by.setdefault(a['det'], []).append(a)

    print(u'подбор формы модели разрешения по измеренным точкам (V2)')
    print(u'мерило — СКО относительной невязки; «низ» = ниже %.0f кэВ' % args.split)
    print()
    print(u'%-10s %5s %5s  %-15s %8s %8s %8s' %
          (u'группа', u'точек', u'низ', u'форма', u'СКО низ', u'СКО верх', u'физична'))

    winners = {}
    for det in sorted(by):
        rows = by[det]
        if len(rows) < args.min_points:
            continue
        e = np.array([r['energy'] for r in rows])
        f = np.array([r['fwhm'] for r in rows])
        w = np.sqrt(np.clip(np.array([r['sig'] for r in rows]), 1.0, 1e4))
        low = e < args.split
        if low.sum() == 0 or (~low).sum() == 0:
            continue

        best, best_rms = None, float('inf')
        for name, fit in FORMS:
            try:
                fn, _ = fit(e, f, w)
            except Exception:
                continue
            ok = physical(fn)
            r_low, r_high = rms(e, f, fn, low), rms(e, f, fn, ~low)
            print(u'%-10s %5d %5d  %-15s %8.3f %8.3f %8s'
                  % (det, len(e), int(low.sum()), name, r_low, r_high,
                     u'да' if ok else u'НЕТ'))
            if ok and np.isfinite(r_low) and r_low < best_rms:
                best, best_rms = name, r_low
        winners[det] = best
        print()

    print(u'победитель по низу шкалы (только физичные формы):')
    for det in sorted(winners):
        print(u'   %-10s %s' % (det, winners[det] or u'— ни одна не физична'))


if __name__ == '__main__':
    main()
