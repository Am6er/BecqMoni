# -*- coding: utf-8 -*-
u"""A62: сколько энергии беты уходит в ТОРМОЗНОЕ — по нашим же данным ESTAR.

ЗАЧЕМ. Строка требует оценить долю отсчётов, которую может дать тормозное
бета-частиц пробы. Косвенный признак (невязка модели у жёстких бета-излучателей
против прочих) следа не показал; здесь оценка прямая и из тех же таблиц,
которыми считает перенос.

Радиационный выход — доля энергии электрона, ушедшая в кванты, пока он
тормозится:  Y(E) = (1/E) · ∫₀^E [ S_rad / (S_col + S_rad) ] dE'.
Интеграл берётся по узлам таблицы, трапециями по энергии.

⚠ Это ВЕРХНЯЯ граница вклада в спектр: из пробы наружу выходит меньше —
тормозной спектр мягкий, а мягкие кванты в самой пробе и поглощаются.

    python tools/CORPUS/scripts/brem_yield.py [--material=<кусок имени>]
"""
import os
import sqlite3
import sys

# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir, os.pardir))
DB = os.path.join(REPO, 'BecquerelMonitor', 'matdb.sqlite')

# Средняя и граничная энергии беты, МэВ, у бета-излучателей корпуса.
BETAS = [
    (u'K-40',   0.585, 1.311),
    (u'Cs-137', 0.174, 1.176),
    (u'Bi-214', 0.640, 3.272),
    (u'Tl-208', 0.560, 1.803),
    (u'Y-90',   0.934, 2.280),
    (u'Lu-176', 0.180, 0.593),
]


def yields(conn, star_id):
    u"""[(энергия МэВ, радиационный выход)] по узлам таблицы."""
    rows = list(conn.execute(
        'select energy_mev, collision_mev_cm2_g, radiative_mev_cm2_g'
        ' from estar_collision_stopping where material_star_id=?'
        ' order by energy_mev', (star_id,)))
    out, acc = [], 0.0
    prev_e, prev_f = None, None
    for e, col, rad in rows:
        f = rad / (col + rad) if (col + rad) > 0.0 else 0.0
        if prev_e is not None:
            acc += 0.5 * (f + prev_f) * (e - prev_e)
        prev_e, prev_f = e, f
        out.append((e, acc / e if e > 0.0 else 0.0))
    return out


def at(curve, energy):
    u"""Выход на энергии — линейно между узлами."""
    prev = None
    for e, y in curve:
        if e >= energy:
            if prev is None:
                return y
            e0, y0 = prev
            f = (energy - e0) / (e - e0) if e > e0 else 0.0
            return y0 + f * (y - y0)
        prev = (e, y)
    return curve[-1][1] if curve else 0.0


def main():
    want = 'water'
    for a in sys.argv[1:]:
        if a.startswith('--material='):
            want = a[11:]

    conn = sqlite3.connect('file:' + DB.replace('\\', '/') + '?mode=ro', uri=True)
    # ⚠ Берём только те вещества, у которых таблица торможения ЕСТЬ:
    # в поставке ESTAR их всего два (CsI и NaI — они нужны переносу для
    # вылета электронов), и совпадение по имени без этой проверки
    # находило вещество без данных и отказывало на пустом месте.
    mats = list(conn.execute(
        'select m.star_id, m.name from star_materials m'
        ' join estar_collision_stopping s on s.material_star_id = m.star_id'
        ' where lower(m.name) like ? group by m.star_id, m.name order by m.name',
        ('%' + want.lower() + '%',)))
    if not mats:
        sys.stderr.write(u'вещества по «%s» не нашлось\n' % want)
        return 2

    star_id, name = mats[0]
    curve = yields(conn, star_id)
    if not curve:
        sys.stderr.write(u'нет таблицы торможения для «%s»\n' % name)
        return 2

    print(u'вещество: %s (star_id %s), узлов %d' % (name, star_id, len(curve)))
    print(u'')
    print(u'%-9s %10s %10s %12s %12s' % (u'нуклид', u'E_ср МэВ', u'E_max',
                                         u'выход(E_ср)', u'выход(E_max)'))
    for nuclide, mean, top in BETAS:
        print(u'%-9s %10.3f %10.3f %11.3f %% %11.3f %%'
              % (nuclide, mean, top, 100.0 * at(curve, mean), 100.0 * at(curve, top)))

    print(u'')
    print(u'⚠ Доля энергии беты, ушедшая в тормозное. В спектр попадает МЕНЬШЕ:')
    print(u'  тормозной спектр мягкий, и мягкое поглощается в самой пробе.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
