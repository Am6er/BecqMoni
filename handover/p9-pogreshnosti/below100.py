# -*- coding: utf-8 -*-
u"""ГДЕ И ЧЕМ модель врёт ниже 100 кэВ — разбор дампа `--dump-curves` (`A283`).

    python handover/p9-pogreshnosti/below100.py <каталог дампа> [спектр …] [--edge=100]

`A281`/`A283` уже назвали МЕСТО: 85–87 % всего хи-квадрата сидит ниже 100 кэВ
на 3.2–3.5 % каналов. Эта оснастка отвечает на следующий вопрос — ЧТО ИМЕННО
там не так, и делает это ЧЕТЫРЬМЯ признаками, различающими виновников:

  1. ЗНАК. Модель систематически НИЖЕ данных (недобор) или ВЫШЕ (перебор)?
     Недобор широкой полосой — не хватает образа (рентген, обратное
     рассеяние, континуум); перебор — образ завышен (эффективность, отклик).

  2. ШИРИНА. Невязка узкая (в ПШПВ линии) или широкая (десятки каналов)?
     Узкая — форма пика; широкая — континуум либо кривая эффективности.

  3. ГРАНИЦА. Насколько невязка жмётся к НИЖНЕМУ КРАЮ полосы фита?
     Считается доля хи-квадрата в первых N каналах полосы. Прижата к краю —
     это край (пол полосы, первый узел сплайна, первый узел матрицы), а не
     физика линии.

  4. ЧЕЙ ЭТО КАНАЛ. Для каждого канала называется слой стека с наибольшим
     вкладом. Если хи-квадрат сгущается там, где господствует КОНТИНУУМ, —
     виноват континуум; если там, где господствует нуклидный образ, — форма
     образа.

⛔ Столбец `fit` (`A284`) — кривая, ПО КОТОРОЙ считан фит. `net` подрезан нулём
   и для меры невязки НЕПРИГОДЕН. Дамп старого формата (без `fit`) читается,
   но помечается в выводе как ПРИБЛИЖЁННЫЙ.

⛔ Веса — ТЕ ЖЕ отчётные, что у приложения: w = 1/max(fit_raw, 1) недоступен
   (сырых отсчётов в дампе нет), поэтому берётся w = 1/max(|model|, 1). На
   долевые выводы это не влияет (проверка — сверка полного хи-квадрата с
   числом приложения печатается первой строкой).
"""
import csv
import io
import os
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8')
except AttributeError:
    pass

SERVICE = ('continuum', 'continuum_raw', 'pile-up', 'Ann-511', 'residual')


def load(path):
    with io.open(path, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    head = list(rows[0].keys())
    return head, rows


def main():
    d = sys.argv[1]
    edge = 100.0
    keys = []
    for a in sys.argv[2:]:
        if a.startswith('--edge='):
            edge = float(a[7:])
        else:
            keys.append(a)
    if not keys:
        keys = [n[:-len('_curves.csv')] for n in sorted(os.listdir(d))
                if n.endswith('_curves.csv')]

    for key in keys:
        path = os.path.join(d, key + '_curves.csv')
        if not os.path.exists(path):
            print(u'%s: дампа нет' % key)
            continue
        head, rows = load(path)
        meas = 'fit' if 'fit' in head else 'net'
        layers = [h for h in head if h not in ('ch', 'keV', 'net', 'fit', 'model',
                                               'continuum_raw')]
        ch, kev, y, m, cont = [], [], [], [], []
        lay = {n: [] for n in layers}
        for r in rows:
            ch.append(int(r['ch']))
            kev.append(float(r['keV']))
            y.append(float(r[meas]))
            m.append(float(r['model']))
            cont.append(float(r['continuum_raw']))
            for n in layers:
                lay[n].append(float(r[n]))

        # Полоса фита: там, где модель ненулевая (печатной границы в дампе нет).
        idx = [i for i in range(len(m)) if m[i] > 0.0]
        if not idx:
            print(u'%s: модель пуста' % key)
            continue
        lo, hi = idx[0], idx[-1]
        band = list(range(lo, hi + 1))

        chi = {}
        for i in band:
            w = 1.0 / max(abs(m[i]), 1.0)
            chi[i] = (y[i] - m[i]) ** 2 * w
        tot = sum(chi.values())

        below = [i for i in band if kev[i] < edge]
        above = [i for i in band if kev[i] >= edge]
        s_below = sum(chi[i] for i in below)

        print(u'')
        print(u'=== %s ===  измерение из столбца `%s`%s' %
              (key, meas, u'' if meas == 'fit' else u'   ⚠ ПРИБЛИЖЁННО (A284)'))
        print(u'  полоса фита: каналы %d..%d (%.1f..%.1f кэВ), слоёв %d'
              % (lo, hi, kev[lo], kev[hi], len(layers)))
        print(u'  ниже %.0f кэВ: каналов %d (%.1f %% полосы), хи-квадрата %.1f %%'
              % (edge, len(below), 100.0 * len(below) / len(band),
                 100.0 * s_below / tot if tot else 0.0))

        if not below:
            continue

        # 1. ЗНАК
        pos = sum(chi[i] for i in below if y[i] > m[i])
        print(u'  1. ЗНАК: доля хи-квадрата, где данные ВЫШЕ модели (недобор) —'
              u' %.1f %%; где модель выше (перебор) — %.1f %%'
              % (100.0 * pos / s_below, 100.0 * (s_below - pos) / s_below))
        # сумма невязки в отсчётах, обоими знаками
        rp = sum(y[i] - m[i] for i in below if y[i] > m[i])
        rn = sum(m[i] - y[i] for i in below if y[i] <= m[i])
        dat = sum(y[i] for i in below)
        print(u'     в ОТСЧЁТАХ: недобор +%.0f, перебор −%.0f, итог %+.0f'
              u' при данных %.0f (%.2f %% полосы данных)'
              % (rp, rn, rp - rn, dat, 100.0 * (rp - rn) / dat if dat else 0.0))

        # 2. ШИРИНА — самый тяжёлый канал и его окрестность
        worst = sorted(below, key=lambda i: -chi[i])
        w0 = worst[0]
        run = [w0]
        i = w0 - 1
        while i >= lo and chi.get(i, 0.0) > 0.1 * chi[w0]:
            run.insert(0, i)
            i -= 1
        i = w0 + 1
        while i <= hi and chi.get(i, 0.0) > 0.1 * chi[w0]:
            run.append(i)
            i += 1
        print(u'  2. ШИРИНА: худший канал %d (%.2f кэВ), хи %.0f = %.1f %% всего;'
              u' сплошная область >10 %% от него — %d каналов (%.2f..%.2f кэВ)'
              % (w0, kev[w0], chi[w0], 100.0 * chi[w0] / tot,
                 len(run), kev[run[0]], kev[run[-1]]))

        # 3. ГРАНИЦА
        for n in (1, 3, 5, 10):
            first = [i for i in band[:n]]
            print(u'  3. ГРАНИЦА: первые %2d каналов полосы (%.2f..%.2f кэВ) —'
                  u' %.1f %% всего хи-квадрата'
                  % (n, kev[first[0]], kev[first[-1]],
                     100.0 * sum(chi[i] for i in first) / tot))

        # 4. ЧЕЙ КАНАЛ
        own = {}
        for i in below:
            best, bv = 'continuum', abs(cont[i])
            for n in layers:
                if abs(lay[n][i]) > bv:
                    best, bv = n, abs(lay[n][i])
            own[best] = own.get(best, 0.0) + chi[i]
        print(u'  4. ЧЕЙ КАНАЛ (по наибольшему слою), доля хи-квадрата ниже'
              u' %.0f кэВ:' % edge)
        for n, v in sorted(own.items(), key=lambda kv: -kv[1]):
            print(u'       %-16s %6.1f %%' % (n, 100.0 * v / s_below))

        # 5. ПРОФИЛЬ по 10 кэВ
        print(u'  5. ПРОФИЛЬ ниже %.0f кэВ (шаг 10 кэВ):' % edge)
        print(u'       %10s %8s %12s %12s %10s %9s'
              % ('кэВ', 'каналов', 'данные', 'модель', 'хи2 %', 'модель/данные'))
        step = 10.0
        e = 0.0
        while e < edge:
            sel = [i for i in below if e <= kev[i] < e + step]
            if sel:
                dy = sum(y[i] for i in sel)
                dm = sum(m[i] for i in sel)
                print(u'       %5.0f..%-4.0f %8d %12.0f %12.0f %10.1f %9.3f'
                      % (e, e + step, len(sel), dy, dm,
                         100.0 * sum(chi[i] for i in sel) / tot,
                         dm / dy if dy else 0.0))
            e += step


if __name__ == '__main__':
    main()
