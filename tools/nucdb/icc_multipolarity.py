# -*- coding: utf-8 -*-
u"""`D37`: третья ловушка поставки `g4_gamma` — alpha не сходится с
мультипольностью, записанной в той же строке, и ни один прежний сторож её не
берёт.

ЗАЧЕМ. У части переходов полный alpha расходится с ЛСРМ больше чем в десять
раз, причём раскол идёт РОВНО по заявленному коду: где Geant4 выше — везде код
2 (E1, наименьшая конверсия), где выше ЛСРМ — M1/M2/M3/M4/E2/E4 (при сотне кэВ
конверсия обязана быть огромной). Обе стороны говорят одно: alpha в строке
относится к ДРУГОЙ мультипольности, не к записанной рядом. Порог
`AlphaCeiling` = 1e4 таких строк не видит (максимум alpha среди них 16.7), а
отсев по выходу — тем более (`intensity_ppm` > 0 у всех).

ЧТО ЭТОТ ИНСТРУМЕНТ. Он повторяет ЗАЖИМ ЧИТАТЕЛЯ — тот же порядок действий,
что в `BecquerelMonitor/FullSpectrumAnalysis/CascadeAtomicData.LoadScheme`, —
и служит двум делам: назвать полный охват зажима по всей поставке и сверить
его число с приложением. Сверено 10.09.2026 на шести схемах: приложение и этот
разбор дали 2 / 1 / 3 / 1 / 0 / 0, а значение alpha_K сошлось до всех
печатаемых цифр (Th-229 137.386 кэВ — 52.9989, Fe-54 1492.8 — 7.27834e-05).

⛔ БЕРЁТСЯ ОДНА ОБОЛОЧКА K, а не сумма K+L+M, которой считает
`compare_copies.py --pair 3`: читателю нужен ровно alpha_K, и сетка по одной
оболочке шире (у M-оболочек верх по энергии ниже). Замерено: по сумме
сверяется 55 591 переход, по K — 125 470; все 34 выброса аудита
(`database/audit-2026-08-08.md`, §7б) K-зажим берёт, плюс 10 того же рода
(Fe-54 1492.8 кэВ, код 304: ЛСРМ 7.3e-05 против 4.51 у Geant4).

⛔ НОЛЬ У Geant4 ЗАЖИМОМ НЕ БЕРЁТСЯ: это пробел поставки, а не противоречие,
и отношение к нулю бесконечно. Без этого условия зажим подставлял бы ЛСРМ
везде, где у Geant4 конверсии нет вовсе — у Th-229 таких было ШЕСТЬ из десяти
сработок, и поймано это первым же прогоном пробы.

⛔ ЕСТЬ АБСОЛЮТНЫЙ ПОРОГ: ниже alpha = 0.01 отношение двух малых чисел ничего
не значит (у P-31 на 5.9 МэВ ЛСРМ даёт 1.3e-06 против 2.1e-03 — расхождение в
1630 раз при нулевом вкладе в вакансии). Без порога зажим берёт 298 переходов
из 125 470, с порогом — 44.

⛔ ЗАПИСИ В БАЗУ НЕТ и не предполагается: обе базы открыты `mode=ro`.

    python tools/nucdb/icc_multipolarity.py [--db <каталог с базами>] [z:a ...]

Без списка схем печатается сводка по всей поставке, со списком — построчно,
что и на что зажим заменил бы.
"""
import argparse
import bisect
import collections
import math
import os
import sqlite3
import sys

#: Мусорный полный alpha: выше — «гаммы у перехода нет вовсе» (`D31`).
#: То же число, что `CascadeAtomicData.AlphaCeiling`.
ALPHA_CEILING = 1e4

#: Во сколько раз должно разойтись, чтобы строке перестали верить.
#: `CascadeAtomicData.AlphaMismatchFactor`.
MISMATCH_FACTOR = 10.0

#: Ниже этого alpha зажим не работает. `CascadeAtomicData.AlphaMismatchFloor`.
MISMATCH_FLOOR = 0.01

#: Колонки ЛСРМ в порядке e1…e4, m1…m4.
COLS = ('e1', 'e2', 'e3', 'e4', 'm1', 'm2', 'm3', 'm4')


def column(part):
    u"""Часть кода Geant4 -> номер колонки ЛСРМ.

    E0 = 1 (колонки нет); при k >= 1 E_k = 2k, M_k = 2k+1; E5 и выше колонок
    не имеют. Колонки идут e1…e4, m1…m4, отсюда k-1 и 3+k.
    """
    if part < 2:
        return None
    k = part // 2
    if k > 4:
        return None
    return (3 + k) if (part % 2) else (k - 1)


def components(code, delta):
    u"""Код Geant4 -> (младшая колонка, старшая либо None).

    Смесь = 100*Nx + Ny (304 = M1+E2). ⛔ Смесь БЕЗ delta отвергается целиком:
    ноль в `mixing_ratio` означает и «чистый переход», и «данных нет», и
    свернуть смесь в первый компонент значило бы считать не ту величину.
    """
    if code <= 0:
        return None
    hi = code // 100 if code >= 100 else code
    lo = code % 100 if code >= 100 else None
    c1 = column(hi)
    if c1 is None:
        return None
    if lo is None:
        return (c1, None)
    if delta == 0.0:
        return None
    c2 = column(lo)
    if c2 is None:
        return None
    # Младшая мультипольность первой: delta^2 приходится на старшую.
    if (lo // 2) < (hi // 2):
        return (c2, c1)
    return (c1, c2)


class Grid(object):
    u"""Сетка ЛСРМ по K-оболочке, `variant = 1`. Экстраполяции нет намеренно:
    сечение конверсии падает степенью энергии, и продлённая сетка мерила бы
    саму себя."""

    def __init__(self, mat):
        self.e = collections.defaultdict(list)
        self.v = collections.defaultdict(list)
        cols = ', '.join(COLS)
        for row in mat.execute("select z, energy_kev, " + cols +
                               " from icc_coefficients"
                               " where variant = 1 and shell = 'K'"
                               " order by z, energy_kev"):
            self.e[row[0]].append(row[1])
            self.v[row[0]].append(row[2:])

    def one(self, z, e_kev, col):
        xs = self.e.get(z)
        if not xs or e_kev < xs[0] or e_kev > xs[-1]:
            return None
        i = bisect.bisect_left(xs, e_kev)
        if i < len(xs) and xs[i] == e_kev:
            y = self.v[z][i][col]
            return y if y > 0 else None
        y0, y1 = self.v[z][i - 1][col], self.v[z][i][col]
        if y0 <= 0 or y1 <= 0:
            return None
        t = ((math.log(e_kev) - math.log(xs[i - 1])) /
             (math.log(xs[i]) - math.log(xs[i - 1])))
        y = math.exp(math.log(y0) + t * (math.log(y1) - math.log(y0)))
        return y if y > 0 else None

    def alpha_k(self, z, e_kev, code, delta):
        comps = components(code, delta)
        if comps is None:
            return None
        a1 = self.one(z, e_kev, comps[0])
        if a1 is None:
            return None
        if comps[1] is None:
            return a1
        a2 = self.one(z, e_kev, comps[1])
        if a2 is None:
            return None
        d2 = delta * delta
        return (a1 + d2 * a2) / (1.0 + d2)


def main():
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except AttributeError:
        pass
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(os.path.dirname(here))
    ap = argparse.ArgumentParser()
    ap.add_argument('--db', default=os.path.join(root, 'BecquerelMonitor'))
    ap.add_argument('pairs', nargs='*', metavar='z:a')
    args = ap.parse_args()
    d = args.db.replace('\\', '/').rstrip('/')
    mat = sqlite3.connect('file:' + d + '/matdb.sqlite?mode=ro', uri=True)
    sch = sqlite3.connect('file:' + d + '/schemedb.sqlite?mode=ro', uri=True)
    grid = Grid(mat)

    where = ''
    params = ()
    if args.pairs:
        want = [tuple(int(x) for x in p.split(':')[:2]) for p in args.pairs]
        where = ' where ' + ' or '.join(['(z=? and a=?)'] * len(want))
        params = tuple(x for pair in want for x in pair)

    per = collections.Counter()
    seen = collections.Counter()
    fired = []
    n_rows = 0
    for z, a, e_ev, tot, kp, ippm, code, delta in sch.execute(
            "select z, a, energy_ev, icc_total, icc_k_ppm, intensity_ppm,"
            " multipolarity, mixing_ratio from g4_gamma" + where, params):
        if ippm is None or ippm <= 0:
            continue                   # переход, которого не испускают
        n_rows += 1
        seen[(z, a)] += 1
        tot = 0.0 if tot is None else float(tot)
        if tot > ALPHA_CEILING:
            tot = ALPHA_CEILING
        kshare = 0.0 if kp is None else kp / 1e6
        ak = tot * kshare
        if ak > tot:
            ak = tot
        if not (ak > 0):
            continue                   # ноль у Geant4 — пробел, а не противоречие
        lsrm = grid.alpha_k(z, e_ev / 1000.0, code or 0, delta or 0.0)
        if lsrm is None:
            continue
        if max(lsrm, ak) < MISMATCH_FLOOR:
            continue
        if lsrm > ak * MISMATCH_FACTOR or ak > lsrm * MISMATCH_FACTOR:
            per[(z, a)] += 1
            fired.append((z, a, e_ev / 1000.0, code, ak, min(lsrm, ALPHA_CEILING)))

    sys.stdout.write(u'строк схем, доходящих до читателя (intensity_ppm > 0):'
                     u' %d\n' % n_rows)
    sys.stdout.write(u'зажим сработал на %d переходах, в %d схемах (z, a)\n'
                     % (len(fired), len(per)))
    if args.pairs:
        for p in args.pairs:
            z, a = (int(x) for x in p.split(':')[:2])
            sys.stdout.write(u'   z=%d a=%d: переходов %d, зажато %d\n'
                             % (z, a, seen[(z, a)], per[(z, a)]))
            for f in fired:
                if f[0] == z and f[1] == a:
                    sys.stdout.write(
                        u'        %9.3f кэВ код %-4s  было %-12.6g стало %-12.6g\n'
                        % (f[2], f[3], f[4], f[5]))
    else:
        sys.stdout.write(u'   схемы с наибольшим числом сработок:\n')
        for (z, a), n in per.most_common(10):
            sys.stdout.write(u'   z=%d a=%d: %d\n' % (z, a, n))
    return 0


if __name__ == '__main__':
    sys.exit(main())
