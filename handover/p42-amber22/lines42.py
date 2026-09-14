# -*- coding: utf-8 -*-
"""П42 13.09.2026, `AMBER22` — остаток ПО ЛИНИЯМ ряда Th-232 (расширенный список) на дампе сцены Amber:
положение отдельно от амплитуды, читателем П26 `peaks.py` (`fit_line`: net ≈ A·образ(E−δ) + остальное + a + b·E).

    python handover/p42-amber22/lines42.py <dump.csv> [...]

A − 1 — амплитуда образа-хозяина ПРИ ВЫРОВНЕННОМ положении (на сколько образ занижен); δ — положение
(кэВ, доли ПШПВ); «узкое» — model/net − 1 в ±1 ПШПВ (то, что видит строка); «хоз.» — доля образа-хозяина
в net окна. Линии слабее 1 % и в тесных смесях — с оговоркой (смесь 964.8/969.0, 727/785 Bi-212, 860 Tl-208
рядом с 835/840 Ac-228).
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), 'p26-amber22'))
import peaks as P  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

LINES = [(129.065, 'Ac-228'), (209.253, 'Ac-228'), (238.632, 'Pb-212'), (270.245, 'Ac-228'), (277.371, 'Tl-208'),
         (300.087, 'Pb-212'), (328.000, 'Ac-228'), (338.320, 'Ac-228'), (409.462, 'Ac-228'), (463.004, 'Ac-228'),
         (510.770, 'Tl-208'), (583.187, 'Tl-208'), (727.330, 'Bi-212'), (794.947, 'Ac-228'), (860.557, 'Tl-208'),
         (911.204, 'Ac-228'), (968.971, 'Ac-228'), (1588.200, 'Ac-228'), (1620.500, 'Bi-212'), (2614.511, 'Tl-208')]


def report(path):
    kev, cols = P.load(path)
    net, model = cols['net'], cols['model']
    print('== %s ==' % path)
    print('%-8s %-7s | %7s | %7s %7s | %8s | %8s | %6s' % ('линия', 'хозяин', 'центр d', 'δ кэВ', 'δ/ПШПВ', 'A−1 вырв', 'узкое', 'хоз.%'))
    for e, owner in LINES:
        if owner not in cols:
            print('%-8.1f %-7s — образа нет' % (e, owner))
            continue
        cd, w, best, a0, chi0, n, narrow, share = P.fit_line(kev, net, model, cols[owner], e)
        chi2, d, A, a, b = best
        print('%-8.1f %-7s | %7.1f | %+7.1f %+7.2f | %+7.1f%% | %+7.1f%% | %5.0f%%' % (e, owner, cd, d, d / w, 100 * (A - 1), narrow, share))


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
