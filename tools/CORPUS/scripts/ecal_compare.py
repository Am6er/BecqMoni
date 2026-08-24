# -*- coding: utf-8 -*-
"""`B24`: сравнение вариантов энергокалибровки мерой, НЕЗАВИСИМОЙ от поиска.

⛔ **Зачем отдельный счётчик, а не колонка в пробе.** Мерка, которая для каждого
варианта ищет линию заново, слепа ровно там, где дефект: при уехавшей шкале
линия не находится вовсе, и «не нашлась» неотличимо от «нашлась точно». В
`ecal_extrapolation.py` на это наступили ДВАЖДЫ — сперва со штатным допуском
поиска, потом с широким.

Здесь канал линии определяется ОДИН РАЗ и по данным: из слепков всех вариантов
берётся согласное положение пика (медиана, если варианты сходятся в пределах
полуширины). Дальше каждый вариант оценивается ТОЛЬКО СВОЕЙ КРИВОЙ на этом
неподвижном наборе (спектр, линия, канал) — спрятаться, не найдя линию, он не
может по построению.

Запуск:

    python tools/CORPUS/scripts/ecal_compare.py слепок1.json слепок2.json ...
"""
import io
import json
import sys

import numpy as np

BAND_KEV = 200.0            # «низ шкалы» — полоса, ради которой заведена `B24`


def energy(coef, ch):
    return sum(c * ch ** i for i, c in enumerate(coef))


def consensus(dumps):
    """Неподвижный набор (спектр, линия) -> канал, согласный у вариантов.

    Линия берётся, если её нашли не меньше половины вариантов и разброс канала
    меньше полуширины: пик стоит там, где стоит, и если варианты о его канале
    спорят — это не опора для мерки, а отдельная находка.
    """
    keys = set()
    for d in dumps.values():
        keys |= set(d)
    out = {}
    dropped = 0
    for key in sorted(keys):
        per_line = {}
        res_a = None
        for d in dumps.values():
            st = d.get(key)
            if not st:
                continue
            res_a = st['res_a']
            for e_s, ch in st['found'].items():
                per_line.setdefault(e_s, []).append(ch)
        if res_a is None:
            continue
        good = {}
        for e_s, chs in per_line.items():
            if len(chs) * 2 < len(dumps):
                continue
            e_ref = float(e_s)
            # полуширина в каналах: через среднюю крутизну кривых вариантов
            spread = float(np.max(chs) - np.min(chs))
            fwhm_kev = res_a * np.sqrt(max(e_ref, 5.0))
            slopes = []
            for d in dumps.values():
                st = d.get(key)
                if st:
                    c = st['coef']
                    ch0 = float(np.median(chs))
                    slopes.append(abs(sum(i * cc * ch0 ** (i - 1)
                                          for i, cc in enumerate(c) if i >= 1)))
            slope = float(np.median(slopes)) if slopes else 1.0
            if spread > 0.5 * fwhm_kev / max(slope, 1e-9):
                dropped += 1
                continue
            good[e_ref] = float(np.median(chs))
        if good:
            out[key] = dict(res_a=res_a, lines=good)
    return out, dropped


def score(dump, cons, band=BAND_KEV):
    n = 0
    total = 0.0
    worst = 0.0
    worst_at = ''
    per_spec = []
    for key, c in cons.items():
        st = dump.get(key)
        if not st:
            continue
        res_a = c['res_a']
        sworst = 0.0
        for e_ref, ch in sorted(c['lines'].items()):
            if e_ref > band:
                continue
            d = energy(st['coef'], ch) - e_ref
            f = d / max(res_a * np.sqrt(max(e_ref, 5.0)), 1e-9)
            n += 1
            total += abs(f)
            if abs(f) > abs(sworst):
                sworst = f
            if abs(f) > abs(worst):
                worst, worst_at = f, '%s @ %.1f кэВ' % (key, e_ref)
        if sworst:
            per_spec.append((abs(sworst), key, sworst))
    return n, total, worst, worst_at, per_spec


def main():
    paths = [a for a in sys.argv[1:] if not a.startswith('--')]
    if not paths:
        print(__doc__)
        return
    dumps = {}
    for p in paths:
        with io.open(p, encoding='utf-8') as h:
            dumps[p] = json.load(h)
    cons, dropped = consensus(dumps)
    n_lines = sum(len([e for e in c['lines'] if e <= BAND_KEV])
                  for c in cons.values())
    print('НЕПОДВИЖНЫЙ НАБОР: %d спектров, %d линий ниже %.0f кэВ '
          '(отброшено спорных: %d)' % (len(cons), n_lines, BAND_KEV, dropped))
    print()
    print('%-34s %5s %9s %9s %9s %6s %6s'
          % ('вариант', 'линий', 'Σ|промах|', 'медиана', 'максимум', '>0.25', '>0.50'))
    table = {}
    for p in paths:
        n, total, worst, worst_at, per = score(dumps[p], cons)
        med = float(np.median([x[0] for x in per])) if per else 0.0
        table[p] = (n, total, worst, worst_at, per)
        print('%-34s %5d %9.2f %9.3f %9.3f %6d %6d'
              % (p.split('/')[-1].replace('.json', ''), n, total, med, abs(worst),
                 sum(1 for x in per if x[0] > 0.25), sum(1 for x in per if x[0] > 0.5)))
        print('%-34s   худший: %s' % ('', worst_at))

    base = paths[0]
    print()
    print('ПОСПЕКТРОВО против «%s» (порог 0.05 ПШПВ):'
          % base.split('/')[-1].replace('.json', ''))
    b = {k: v for _, k, v in table[base][4]}
    for p in paths[1:]:
        cur = {k: v for _, k, v in table[p][4]}
        better = sorted(k for k in cur if abs(cur[k]) < abs(b.get(k, 0.0)) - 0.05)
        worse = sorted(k for k in cur if abs(cur[k]) > abs(b.get(k, 0.0)) + 0.05)
        print('  %s: лучше %d, хуже %d'
              % (p.split('/')[-1].replace('.json', ''), len(better), len(worse)))
        for k in worse:
            print('      ХУЖЕ  %-24s %6.2f -> %6.2f  (%s -> %s)'
                  % (k, b.get(k, 0.0), cur[k],
                     dumps[base][k]['mode'], dumps[p][k]['mode']))
        for k in better:
            print('      лучше %-24s %6.2f -> %6.2f  (%s -> %s)'
                  % (k, b.get(k, 0.0), cur[k],
                     dumps[base][k]['mode'], dumps[p][k]['mode']))


if __name__ == '__main__':
    main()
