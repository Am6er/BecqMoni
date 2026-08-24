# -*- coding: utf-8 -*-
"""`B24`: насколько подгонка энергокалибровки экстраполирует ВНИЗ от своих опор.

Строка `B24` доказала на одном спектре (`G1S16_Ba133_P5`), что подгонка,
опирающаяся на три линии выше 160 кэВ, молча продолжает прямую ниже них и
уводит линию 81 кэВ на 68.1. Здесь это меряется по ВСЕМУ корпусу.

⚠ **Меряется ОБА прохода, а не первый.** Первый проход (разрешение меряется по
самому спектру) у `G1S16_Ba133_P5` линию 81 кэВ НАХОДИТ и ставит poly2 по
четырём опорам. Теряется она на втором — там разрешение берётся из модели
ГРУППЫ, окно поиска другое, линия не находится, опор остаётся три, и подгонка
становится прямой. Мерило, которое считает только первый проход, дефекта не
видит вовсе.

Три числа на спектр:

1. **Плечо экстраполяции** — от самой нижней опорной линии до низа РАБОЧЕГО
   диапазона спектра. Оно говорит, на какой длине кривая идёт без единой опоры.
2. **Зазор с поставочной калибровкой** на этом плече. Ниже опор поставочная —
   единственное свидетельство, какое есть; расходятся они там ровно настолько,
   насколько подгонка себе позволила.
3. **Развёртка «выбрось нижнюю опору»** — на спектрах, у которых нижняя опора
   ЕСТЬ: убрать её, перефитить тем же кодом и посмотреть, насколько мимо ляжет
   выброшенная линия. Прямая мера того, что делает подгонка, когда ей нечем
   держать низ шкалы, и единственный способ откалибровать допуск, не подгоняя
   его под один больной спектр.

Запуск (ничего не меняет, читает свои копии из `_corpus_raw`):

    python tools/CORPUS/scripts/ecal_extrapolation.py [--only=KEY,KEY]
                                                      [--csv=файл] [--pass1]
"""
import os
import sys
import csv
import io

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import corpus_def                                     # noqa: E402
import corpus_calib                                   # noqa: E402
import build_corpus                                   # noqa: E402
from spectrum import Spectrum                         # noqa: E402

RAW = os.path.join(HERE, '_corpus_raw')


def working_low(counts, frac=1e-4):
    """Первый канал, с которого спектр перестаёт быть пустым.

    Порог — доля от полного счёта на канал плюс доля от медианы ненулевых.
    Берётся снизу: шумовая полка АЦП обрезана в самом приборе, и ниже неё
    стоят нули либо единицы.
    """
    c = np.asarray(counts, dtype=float)
    if c.sum() <= 0:
        return 0
    ref = float(np.median(c[c > 0])) if np.any(c > 0) else 0.0
    thr = max(1.0, frac * c.sum() / max(len(c), 1), 0.02 * ref)
    idx = np.nonzero(c >= thr)[0]
    return int(idx[0]) if len(idx) else 0


def build_state(entries, two_pass=True):
    """Стадии 1 и 2а конвейера корпуса на своих копиях, без библиотеки.

    Повторяет `build_corpus.main` ровно в той части, что определяет
    энергокалибровку; верность повторения проверяется сверкой метки режима с
    `corpus/manifest.csv` (ключ `--csv` печатает её).
    """
    state = {}
    for e in entries:
        raw = os.path.join(RAW, e['key'] + '.xml')
        if not os.path.isfile(raw):
            print('%-24s НЕТ своей копии в _corpus_raw' % e['key'])
            continue
        try:
            sp = Spectrum(raw)
            ecal, acc, r662, mode = build_corpus.calibrate_one(sp, e)
        except Exception as ex:
            print('%-24s ОШИБКА стадии 1: %s' % (e['key'], ex))
            continue
        state[e['key']] = dict(entry=e, det=e['det'], sp=sp, ecal=ecal,
                               accepted=acc, r662=r662, mode=mode)
    if not two_pass:
        return state

    for _round in (1, 2):
        res_a = {}
        for det in sorted({st['det'] for st in state.values()}):
            pts = build_corpus.resolution_points(state, det)
            if len(pts) >= 2:
                res_a[det] = float(np.median([w / np.sqrt(e) for e, w, _ in pts]))
        moved = 0
        for st in state.values():
            hint = res_a.get(st['det'])
            if hint is None:
                continue
            try:
                ecal, acc, r662, mode = build_corpus.calibrate_one(
                    st['sp'], st['entry'], res_a_hint=hint)
            except Exception:
                continue

            # ⛔ Правило приёмки ОДНО на двоих с конвейером (`V12`): до
            # 24.08.2026 здесь лежала его вторая копия, и мерка `B24` мерила бы
            # своё правило, а не то, по которому собирается корпус.
            take, _before, _after, _fixed = build_corpus.accept_recalibration(
                st['ecal'], st['accepted'], st['r662'], ecal, acc, r662)
            if not take:
                continue
            st.update(ecal=ecal, accepted=acc, r662=r662, mode=mode + '/grp')
            moved += 1
        if not moved:
            break
    return state


def below_anchor_miss(st):
    """⚖ МЕРКА ПРИЁМКИ: промах по линиям, лежащим НИЖЕ самой нижней опоры.

    Собственная невязка подгонки для этого не годится по построению: она
    считается по опорам, а вопрос ровно в том, что делается ТАМ, ГДЕ ОПОР НЕТ.

    ⛔ **Окно поиска обязано быть ШИРОКИМ, и это не мелочь.** Первый вариант
    мерки искал штатным допуском (2.5 ПШПВ) — и на самом больном спектре
    (`G1S16_Ba133_P5`, промах 13 кэВ на линии 81 кэВ) не находил линию ВОВСЕ,
    после чего «не нашлась» записывалось нулём промаха. Мерка читалась нулём
    ровно там, где дефект был худшим. Ищем допуском 6 ПШПВ и ненайденные
    считаем ОТДЕЛЬНО, нулём промаха их не объявляя.

    ⚠ Ненайденная линия сама по себе уликой не является: ниже опоры лежат и
    линии, которых в спектре просто нет (у `G1S16_Cd109_P5` опора — K-40 1460.8,
    и «ниже» оказывается вся таблица). Поэтому мерка — промах по НАЙДЕННЫМ.

    Возвращает (число найденных, наибольший промах в кэВ, он же в долях ПШПВ,
    число ненайденных).
    """
    pairs = st['accepted']
    if not pairs:
        return 0, 0.0, 0.0, 0
    sp, cal = st['sp'], st['ecal']
    res_a = st['r662'] * np.sqrt(662.0)
    e_lo = min(a['e_ref'] for a in pairs)

    ent = dict(st['entry'])
    ent['wanted'] = build_corpus.wanted_lines(st['entry'])
    build_corpus.calibrate.sample_lines = build_corpus.sample_lines
    lines = build_corpus.calibrate.curate(
        ent, lambda e: res_a * np.sqrt(max(float(e), 5.0)), min_purity=0.45)
    low = [ln for ln in lines if ln[0] < e_lo - 1e-9]
    if not low:
        return 0, 0.0, 0.0, 0
    found = corpus_calib.match_lines(sp.counts, cal, low, res_a, tol_fwhm=6.0,
                                     width_lo=0.3, width_hi=3.0)
    worst_kev = worst_fwhm = 0.0
    for a in found:
        d = float(cal.energy(a['ch']) - a['e_ref'])
        f = d / max(res_a * np.sqrt(max(a['e_ref'], 5.0)), 1e-9)
        if abs(f) > abs(worst_fwhm):
            worst_kev, worst_fwhm = d, f
    return len(found), worst_kev, worst_fwhm, len(low) - len(found)


LOW_BAND_KEV = 200.0


def low_band_miss(st, band=LOW_BAND_KEV):
    """⚖ ГЛАВНАЯ МЕРКА: промах шкалы ВНИЗУ, безразличный к выбору опор.

    `below_anchor_miss` меряет только там, где опор нет, и потому НЕСРАВНИМА
    между вариантами запрета: варианты меняют сам набор опор, а с ним и
    население мерки. Здесь берутся ВСЕ курированные линии ниже `band` кэВ —
    опорные в том числе, — и каждая ищется широким окном. Число одно и то же
    для любого варианта, сравнивать можно.

    Возвращает (найдено линий, наибольший промах в кэВ, он же в долях ПШПВ,
    сумма |промаха| в долях ПШПВ).
    """
    pairs = st['accepted']
    if not pairs:
        return 0, 0.0, 0.0, 0.0
    sp, cal = st['sp'], st['ecal']
    res_a = st['r662'] * np.sqrt(662.0)

    ent = dict(st['entry'])
    ent['wanted'] = build_corpus.wanted_lines(st['entry'])
    build_corpus.calibrate.sample_lines = build_corpus.sample_lines
    lines = build_corpus.calibrate.curate(
        ent, lambda e: res_a * np.sqrt(max(float(e), 5.0)), min_purity=0.45)
    low = [ln for ln in lines if ln[0] <= band]
    if not low:
        return 0, 0.0, 0.0, 0.0
    found = corpus_calib.match_lines(sp.counts, cal, low, res_a, tol_fwhm=6.0,
                                     width_lo=0.3, width_hi=3.0)
    worst_kev = worst = 0.0
    total = 0.0
    for a in found:
        d = float(cal.energy(a['ch']) - a['e_ref'])
        f = d / max(res_a * np.sqrt(max(a['e_ref'], 5.0)), 1e-9)
        total += abs(f)
        if abs(f) > abs(worst):
            worst_kev, worst = d, f
    return len(found), worst_kev, worst, total


def loo_low(st):
    """Выбросить САМУЮ НИЖНЮЮ опору, перефитить, вернуть промах по ней."""
    pairs = st['accepted']
    if len(pairs) < 3:
        return None
    sp, res_a = st['sp'], st['r662'] * np.sqrt(662.0)
    order = sorted(pairs, key=lambda a: a['e_ref'])
    drop, keep = order[0], order[1:]
    stored = corpus_calib.Ecal(sp.ecal, sp.n)
    tag, cal, _ = corpus_calib.choose(stored, keep, res_a, sp.n,
                                      force=bool(st['entry'].get('recal')))
    miss = float(cal.energy(drop['ch']) - drop['e_ref'])
    fwhm = max(res_a * np.sqrt(max(drop['e_ref'], 5.0)), 1e-9)
    return miss, miss / fwhm, tag, drop['e_ref']


def main():
    only = None
    out_csv = None
    dump = None
    two_pass = '--pass1' not in sys.argv[1:]
    for a in sys.argv[1:]:
        if a.startswith('--only='):
            only = set(a.split('=', 1)[1].split(','))
        elif a.startswith('--csv='):
            out_csv = a.split('=', 1)[1]
        elif a.startswith('--extrap='):
            v = a.split('=', 1)[1]
            corpus_calib.EXTRAP_EXCESS_FWHM = None if v in ('', 'off') else float(v)
        elif a.startswith('--scope='):
            corpus_calib.EXTRAP_SCOPE = a.split('=', 1)[1]
        elif a.startswith('--dump='):
            dump = a.split('=', 1)[1]
        elif a.startswith('--ecal-accept='):
            build_corpus.ECAL_ACCEPT = a.split('=', 1)[1]
    print('запрет экстраполяции (`B24`): %s, на кого: %s'
          % ('выключен' if corpus_calib.EXTRAP_EXCESS_FWHM is None
             else '%.2f ПШПВ избытка' % corpus_calib.EXTRAP_EXCESS_FWHM,
             corpus_calib.EXTRAP_SCOPE))
    print('приёмка второго прохода (`V12`): %s' % build_corpus.ECAL_ACCEPT)

    entries = [e for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON
               if only is None or e['key'] in only]
    print('проходов: %d, спектров: %d' % (2 if two_pass else 1, len(entries)))
    state = build_state(entries, two_pass=two_pass)

    rows = []
    for key, st in state.items():
        pairs = st['accepted']
        if not pairs:
            print('%-24s опорных линий нет (%s)' % (key, st['mode']))
            continue
        cal, sp = st['ecal'], st['sp']
        res_a = st['r662'] * np.sqrt(662.0)
        stored = corpus_calib.Ecal(sp.ecal, sp.n)
        ch_lo = min(a['ch'] for a in pairs)
        e_lo = min(a['e_ref'] for a in pairs)
        ch_work = working_low(sp.counts)

        if ch_work < ch_lo:
            grid = np.arange(ch_work, ch_lo, dtype=float)
            d = cal.energy(grid) - stored.energy(grid)
            i = int(np.argmax(np.abs(d)))
            gap_kev = float(d[i])
            gap_at = float(cal.energy(grid[i]))
            gap_fwhm = gap_kev / max(res_a * np.sqrt(max(abs(gap_at), 5.0)), 1e-9)
        else:
            gap_kev = gap_fwhm = gap_at = 0.0

        loo = loo_low(st)
        n_low, low_kev, low_fwhm, n_miss = below_anchor_miss(st)
        excess = corpus_calib.extrapolation_excess(cal, stored, pairs, res_a)
        b_n, b_kev, b_fwhm, b_sum = low_band_miss(st)
        rows.append(dict(
            n_low=n_low, n_miss=n_miss,
            low_kev=round(low_kev, 2), low_fwhm=round(low_fwhm, 2),
            excess=round(float(excess), 2),
            band_n=b_n, band_kev=round(b_kev, 2), band_fwhm=round(b_fwhm, 2),
            band_sum=round(b_sum, 3),
            key=key, det=st['det'], mode=st['mode'], lines=len(pairs),
            e_lo=round(e_lo, 1), ch_lo=int(ch_lo), ch_work=int(ch_work),
            e_work_kev=round(float(cal.energy(float(ch_work))), 1),
            lever_ch=int(max(0, ch_lo - ch_work)),
            lever_frac=round(max(0.0, ch_lo - ch_work) / max(sp.n, 1), 3),
            gap_kev=round(gap_kev, 2), gap_fwhm=round(gap_fwhm, 2),
            gap_at_kev=round(gap_at, 1),
            loo_e=round(loo[3], 1) if loo else '',
            loo_kev=round(loo[0], 2) if loo else '',
            loo_fwhm=round(loo[1], 2) if loo else '',
            loo_mode=loo[2] if loo else ''))

    rows.sort(key=lambda r: -abs(r['low_fwhm']))
    print()
    print('%-24s %-18s %3s %7s %7s %4s %9s %10s %9s %9s'
          % ('спектр', 'режим', 'лн', 'E_опор', 'E_низ', 'нжл', 'ниже,кэВ',
             'ниже/ПШПВ', 'зазор/ПШ', 'LOO/ПШПВ'))
    for r in rows:
        print('%-24s %-18s %3d %7.1f %7.1f %4d %9s %10s %9s %9s'
              % (r['key'], r['mode'][:18], r['lines'], r['e_lo'], r['e_work_kev'],
                 r['n_low'], r['low_kev'], r['low_fwhm'], r['gap_fwhm'],
                 r['loo_fwhm']))

    print()
    print('спектров разобрано: %d' % len(rows))
    band = [abs(r['band_fwhm']) for r in rows if r['band_n']]
    print('⚖ ГЛАВНАЯ МЕРКА: промах по линиям НИЖЕ %.0f кэВ (население не зависит '
          'от выбора опор)' % LOW_BAND_KEV)
    print('   спектров с такими линиями %d, линий найдено %d'
          % (len(band), sum(r['band_n'] for r in rows)))
    if band:
        print('   наибольший промах: медиана %.3f ПШПВ, три четверти %.3f, максимум %.3f'
              % (float(np.median(band)), float(np.percentile(band, 75)), max(band)))
        print('   СУММА |промаха| по всем линиям: %.2f ПШПВ'
              % sum(r['band_sum'] for r in rows))
        for lim in (0.25, 0.5, 1.0):
            print('   спектров с промахом больше %.2f ПШПВ: %d'
                  % (lim, sum(1 for x in band if x > lim)))
    low = [abs(r['low_fwhm']) for r in rows if r['n_low']]
    print('⚖ МЕРКА: промах по линиям НИЖЕ нижней опоры — таких спектров %d '
          'из %d' % (len(low), len(rows)))
    if low:
        print('   медиана %.2f ПШПВ, три четверти %.2f, максимум %.2f'
              % (float(np.median(low)), float(np.percentile(low, 75)), max(low)))
        for lim in (0.5, 1.0, 2.0):
            print('   промах больше %.1f ПШПВ: %d' % (lim, sum(1 for x in low if x > lim)))
        print('   СУММА |промаха| в долях ПШПВ: %.2f' % sum(low))
    for lim in (0.5, 1.0, 2.0, 4.0):
        n = sum(1 for r in rows if abs(r['gap_fwhm']) > lim)
        print('  зазор с поставочной ниже опор больше %.1f ПШПВ: %d' % (lim, n))
    loos = [abs(r['loo_fwhm']) for r in rows if r['loo_fwhm'] != '']
    if loos:
        print('  развёртка «выбрось нижнюю опору», %d спектров: медиана %.2f ПШПВ, '
              'три четверти %.2f, максимум %.2f'
              % (len(loos), float(np.median(loos)),
                 float(np.percentile(loos, 75)), max(loos)))
        for lim in (0.5, 1.0, 2.0):
            print('    промах больше %.1f ПШПВ: %d'
                  % (lim, sum(1 for x in loos if x > lim)))

    if dump:
        # Слепок варианта для НЕЗАВИСИМОЙ мерки (`ecal_compare.py`): кривая и
        # ВСЕ курированные линии, найденные широким окном. Канал линии от
        # варианта почти не зависит — пик стоит там, где стоит, — поэтому по
        # слепкам всех вариантов строится согласный канал, и дальше каждый
        # вариант оценивается ТОЛЬКО своей кривой. Мерка, которая ищет линию
        # заново, дважды прочиталась нулём там, где линия не нашлась вовсе.
        import json
        out = {}
        for key, st in state.items():
            if not st['accepted']:
                continue
            res_a = st['r662'] * np.sqrt(662.0)
            ent = dict(st['entry'])
            ent['wanted'] = build_corpus.wanted_lines(st['entry'])
            build_corpus.calibrate.sample_lines = build_corpus.sample_lines
            lines = build_corpus.calibrate.curate(
                ent, lambda e: res_a * np.sqrt(max(float(e), 5.0)),
                min_purity=0.45)
            found = corpus_calib.match_lines(st['sp'].counts, st['ecal'], lines,
                                             res_a, tol_fwhm=6.0,
                                             width_lo=0.3, width_hi=3.0)
            out[key] = dict(
                det=st['det'], mode=st['mode'], res_a=res_a, n=st['sp'].n,
                coef=[float(c) for c in st['ecal'].coef],
                stored=[float(c) for c in st['sp'].ecal],
                anchors=sorted(round(float(a['e_ref']), 2) for a in st['accepted']),
                found={('%.2f' % a['e_ref']): float(a['ch']) for a in found})
        with io.open(dump, 'w', encoding='utf-8') as h:
            json.dump(out, h, ensure_ascii=False, indent=1)
        print('слепок варианта: %s (%d спектров)' % (dump, len(out)))

    if out_csv and rows:
        with open(out_csv, 'w', newline='', encoding='utf-8') as h:
            w = csv.DictWriter(h, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print('\nтаблица: %s' % out_csv)


if __name__ == '__main__':
    main()
