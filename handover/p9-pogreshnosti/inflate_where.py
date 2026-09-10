# -*- coding: utf-8 -*-
u"""ЧЕМ ПИТАЕТСЯ `inflate` — какой полосой шкалы (`A281` × `A283`).

    python handover/p9-pogreshnosti/inflate_where.py <дамп> <runs-каталог> [--edge=100]

`inflate = sqrt(max(1, хи2/ndf))` домножает СИГМУ КАЖДОГО компонента спектра —
и калия на 1461 кэВ, и цезия на 662. Вопрос строки: чем это число задано.

Считается хи-квадрат РЕШАТЕЛЯ (хуберовский, `HuberM` = 3) по дампу и делится
на две полосы — ниже и выше границы. Печатается доля низа и то, во сколько раз
`inflate` был бы меньше, если бы низ в него не входил.

⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВСТРОЕН И ПЕЧАТАЕТСЯ: восстановленный полный
   хи2/ndf решателя сверяется с числом ПРИЛОЖЕНИЯ (`chi2ndf` в `runs.csv`).
   Расходится сильно — числу полосы верить нельзя, и это видно сразу.

⚠ Дисперсия берётся как `max(|fit|, 1)`: сырых отсчётов в дампе нет, а `fit` —
   это спектр минус вычтенный фон. У спектров с крупным вычтенным фоном
   дисперсия тем самым занижена; такие спектры видно по расхождению контроля.
"""
import csv
import io
import math
import os
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8')
except AttributeError:
    pass

HUBER_M = 3.0


def main():
    d, runs_dir = sys.argv[1], sys.argv[2]
    edge = 100.0
    for a in sys.argv[3:]:
        if a.startswith('--edge='):
            edge = float(a[7:])

    app = {}
    for n in sorted(os.listdir(runs_dir)):
        if n.endswith('_runs.csv'):
            with io.open(os.path.join(runs_dir, n), encoding='utf-8-sig',
                         newline='') as f:
                for r in csv.DictReader(f):
                    app[r['spectrum']] = r

    print(u'%-24s %-8s %10s %10s %9s %9s %9s'
          % (u'спектр', u'часть', u'хи2 прил.', u'хи2 моё', u'контроль',
             u'χ² низа %', u'inflate'))
    rows = []
    for name in sorted(os.listdir(d)):
        if not name.endswith('_curves.csv'):
            continue
        key = name[:-len('_curves.csv')]
        if key not in app or app[key].get('error'):
            continue
        with io.open(os.path.join(d, name), encoding='utf-8-sig', newline='') as f:
            data = list(csv.DictReader(f))
        if 'fit' not in data[0]:
            continue
        kev = [float(r['keV']) for r in data]
        y = [float(r['fit']) for r in data]
        m = [float(r['model']) for r in data]
        idx = [i for i in range(len(m)) if m[i] > 0.0]
        if not idx:
            continue
        band = range(idx[0], idx[-1] + 1)
        lo_s = hi_s = 0.0
        n_band = 0
        for i in band:
            n_band += 1
            v = max(abs(y[i]), 1.0)
            r = y[i] - m[i]
            plain = r * r / v
            hub = HUBER_M * abs(r) / math.sqrt(v)
            c = plain if plain <= HUBER_M * HUBER_M else hub
            if kev[i] < edge:
                lo_s += c
            else:
                hi_s += c
        tot = lo_s + hi_s
        appchi = app[key]['chi2ndf']
        try:
            appchi = float(appchi)
        except ValueError:
            continue
        # ndf приложения не печатается; берём его ИЗ числа приложения, чтобы
        # сравнивать сумму с суммой: ndf ≈ хи2 / (хи2/ndf).
        mine_ndf = tot / appchi if appchi > 0.0 else 0.0
        ctl = 0.0
        if mine_ndf > 0.0:
            ctl = 100.0 * (mine_ndf / n_band - 1.0)
        infl_all = math.sqrt(max(1.0, appchi))
        infl_hi = math.sqrt(max(1.0, appchi * hi_s / tot)) if tot else 1.0
        share = 100.0 * lo_s / tot if tot else 0.0
        rows.append((app[key]['part'], share, infl_all, infl_hi, key, ctl))
        print(u'%-24s %-8s %10.2f %10.4g %8.1f %% %8.1f %% %9.3f'
              % (key, app[key]['part'], appchi, tot, ctl, share, infl_all))

    print()
    print(u'⚠ «контроль» = насколько восстановленное ndf разошлось с числом каналов'
          u' полосы; далеко от нуля — реконструкция не та, число полосы негодно')
    print()
    print(u'=== ПО ЧАСТЯМ (⛔ не складывать) ===')
    print(u'⛔ В свод идут ТОЛЬКО спектры с |контроль| < 10 %: на остальных'
          u' реконструкция не воспроизводит число приложения, и доля полосы'
          u' у них ничего не меряет.')
    for p in ('known', 'unknown'):
        v = [r for r in rows if r[0] == p and abs(r[5]) < 10.0]
        if not v:
            continue
        v.sort(key=lambda r: r[1])
        med = v[len(v) // 2]
        print(u'  %-8s спектров %d; доля χ² решателя НИЖЕ %.0f кэВ:'
              u' медиана %.1f %%, мин %.1f %%, макс %.1f %%'
              % (p, len(v), edge, med[1], v[0][1], v[-1][1]))
        ia = sorted(r[2] for r in v)
        ih = sorted(r[3] for r in v)
        print(u'     inflate как есть: медиана %.3f, максимум %.3f'
              % (ia[len(ia) // 2], ia[-1]))
        print(u'     inflate БЕЗ полосы ниже %.0f кэВ: медиана %.3f, максимум %.3f'
              % (edge, ih[len(ih) // 2], ih[-1]))
        print(u'     ⇒ порог 3 сегодня = %.1f сырых сигм, а без низа был бы %.1f'
              % (3.0 * ia[len(ia) // 2], 3.0 * ih[len(ih) // 2]))


if __name__ == '__main__':
    main()
