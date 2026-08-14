# -*- coding: utf-8 -*-
"""Невязка ПШПВ группы G1S: один ли это прибор или два (остаток B5).

Зачем. Модель разрешения строится ОДНА НА ГРУППУ `det`. 15.08.2026 в группу
`G1S` втянуто 76 поверочных эталонов ЛСРМ, и понятная часть корпуса просела
94.1/4.26 → 98.9/5.05 при НЕИЗМЕННОМ составе: модель усреднила спектры, снятые
РАЗНЫМИ экземплярами прибора (заголовки поверок называют №0086-16 и №0247-24).
Прежде чем делить группу, надо померить — расходятся ли их ширины на самом деле.

Что делает. Для каждого спектра группы берётся РАБОЧАЯ КОПИЯ из corpus/spectra
(калибровка в ней уже окончательная), заново меряются ширины опорных линий тем
же кодом, что и в конвейере (`build_corpus.calibrate_one` → `corpus_calib`), и
точки (E, ПШПВ, вес) раскладываются по подгруппам:

    2016  — эталоны поверки 2016 года (ключи G1S16_*)
    2024  — эталоны поверки 2024 года (ключи G1S24_*)
    vibe  — прежние двенадцать (ключи G1S_*)

Потом на каждой подгруппе и на их объединении сидит та же модель
`fit_resolution_kev` (ПШПВ² = c1·E + c2·E²), и печатается невязка каждой
подгруппы под СВОЕЙ моделью и под ОБЩЕЙ.

⚠ Двенадцать прежних G1S — побайтные ДУБЛИКАТЫ двенадцати эталонов (проверено
по каналам и калибровке). Ключ `--nodup` выбрасывает подгруппу `vibe` из
подгонки, чтобы одна и та же линия не входила в неё дважды.

Запуск:  python g1s_split_check.py [--nodup] [--csv=путь]
Ничего не пишет в корпус.
"""
import os
import sys
import csv
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import build_corpus                                  # noqa: E402
import corpus_calib                                  # noqa: E402
import corpus_def                                    # noqa: E402
from spectrum import Spectrum                        # noqa: E402

SPECTRA = os.path.join(os.path.dirname(HERE), 'corpus', 'spectra')
ENERGIES = (60.0, 300.0, 662.0, 1461.0, 2615.0)


def subgroup(key):
    if key.startswith('G1S16_'):
        return '2016'
    if key.startswith('G1S24_'):
        return '2024'
    return 'vibe'


def collect():
    """[(подгруппа, ключ, E, ПШПВ_кэВ, вес)] по всем спектрам группы G1S."""
    pts = []
    entries = [e for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON
               if e['det'].startswith('G1S')]
    for e in entries:
        path = os.path.join(SPECTRA, e['key'] + '.xml')
        if not os.path.isfile(path):
            print('НЕТ рабочей копии: %s' % e['key'])
            continue
        sp = Spectrum(path)
        ecal, accepted, _r662, _mode = build_corpus.calibrate_one(sp, e)
        for a in accepted:
            if a.get('purity', 1.0) < 0.85:
                continue
            pts.append((subgroup(e['key']), e['key'], a['e_ref'],
                        a['fwhm'] * abs(ecal.dEdch(a['ch'])),
                        min(a['sig'], 100.0) * a.get('purity', 1.0)))
    return pts


def clean(pts):
    """Тот же отсев выбросов, что в build_corpus.resolution_points."""
    if len(pts) < 3:
        return pts
    red = np.array([p[3] / np.sqrt(max(p[2], 1.0)) for p in pts])
    med = float(np.median(red))
    keep = [p for p, r in zip(pts, red) if 0.6 * med <= r <= 1.7 * med]
    return keep if len(keep) >= 3 else pts


def fit(pts):
    return corpus_calib.fit_resolution_kev([(p[2], p[3], p[4]) for p in pts])


def residual(pts, coef):
    """Взвешенная СКО относительной невязки и средний сдвиг, в процентах."""
    rf = corpus_calib.resolution_fn(coef)
    d = np.array([(p[3] - rf(p[2])) / max(rf(p[2]), 1e-9) for p in pts])
    w = np.sqrt(np.array([p[4] for p in pts]))
    mean = float((w * d).sum() / w.sum())
    rms = float(np.sqrt((w * d ** 2).sum() / w.sum()))
    return 100 * mean, 100 * rms


def show(coef, title):
    rf = corpus_calib.resolution_fn(coef)
    print('  %-22s ' % title + '  '.join(
        '%d:%.2f%%' % (e, 100 * rf(e) / e) for e in ENERGIES))


def main():
    nodup = '--nodup' in sys.argv
    out_csv = None
    for a in sys.argv[1:]:
        if a.startswith('--csv='):
            out_csv = a.split('=', 1)[1]

    pts = collect()
    if out_csv:
        with open(out_csv, 'w', encoding='utf-8-sig', newline='') as fh:
            w = csv.writer(fh)
            w.writerow(['group', 'key', 'e_kev', 'fwhm_kev', 'weight'])
            for p in pts:
                w.writerow([p[0], p[1], round(p[2], 2), round(p[3], 3), round(p[4], 2)])
        print('точки: %s (%d строк)' % (out_csv, len(pts)))

    groups = ('2016', '2024', 'vibe')
    by = {g: clean([p for p in pts if p[0] == g]) for g in groups}
    for g in groups:
        n_sp = len({p[1] for p in by[g]})
        print('%-6s точек %4d  спектров %3d' % (g, len(by[g]), n_sp))

    used = ['2016', '2024'] if nodup else list(groups)
    joint = clean([p for p in pts if p[0] in used])
    print('\nмодели (относительная ПШПВ на энергии):')
    coef_joint = fit(joint)
    show(coef_joint, 'общая (%s)' % '+'.join(used))
    coef = {}
    for g in groups:
        if not by[g]:
            continue
        coef[g] = fit(by[g])
        show(coef[g], 'только %s' % g)

    print('\nневязка подгруппы, %% (сдвиг / СКО):')
    print('  %-6s %-20s %-20s' % ('', 'под ОБЩЕЙ моделью', 'под СВОЕЙ моделью'))
    for g in groups:
        if not by[g]:
            continue
        m1, s1 = residual(by[g], coef_joint)
        m2, s2 = residual(by[g], coef[g])
        print('  %-6s %+7.2f / %6.2f       %+7.2f / %6.2f' % (g, m1, s1, m2, s2))

    # Стоит ли делить: насколько модель одной подгруппы промахивается по другой
    if '2016' in coef and '2024' in coef:
        rf16, rf24 = (corpus_calib.resolution_fn(coef['2016']),
                      corpus_calib.resolution_fn(coef['2024']))
        print('\nмодель 2016 против модели 2024, %% разницы ПШПВ:')
        print('  ' + '  '.join('%d:%+.1f%%' % (e, 100 * (rf16(e) / rf24(e) - 1))
                               for e in ENERGIES))

    per_spectrum(pts)
    per_line(pts)
    paired(pts)


def per_spectrum(pts):
    """Приведённая ширина a = медиана ПШПВ/sqrt(E) по каждому спектру.

    Если приборы разные, спектры обязаны разложиться в две кучи. Если дело в
    загрузке (наложения уширяют пик), a пойдёт за скоростью счёта, а не за годом.
    """
    print('\nприведённая ширина спектра a = медиана ПШПВ/sqrt(E), кэВ^0.5:')
    per = {}
    for g, key, e, f, w in pts:
        per.setdefault((g, key), []).append(f / np.sqrt(max(e, 1.0)))
    vals = {}
    for (g, key), lst in per.items():
        if len(lst) < 3:
            continue
        vals.setdefault(g, []).append((float(np.median(lst)), key, len(lst)))
    if '--per' in sys.argv:
        flat = sorted(v + (g,) for g, lst in vals.items() for v in lst)
        print('  %-28s %-6s %6s %5s %10s' % ('спектр', 'год', 'a', 'линий', 'имп/с'))
        for a, key, n, g in flat:
            sp = Spectrum(os.path.join(SPECTRA, key + '.xml'))
            cps = sp.counts.sum() / max(sp.live, 1e-9)
            print('  %-28s %-6s %6.3f %5d %10.1f' % (key, g, a, n, cps))
    for g in sorted(vals):
        a = np.array([v[0] for v in vals[g]])
        print('  %-6s n=%2d  медиана %.3f  квартили %.3f..%.3f  min..max %.3f..%.3f'
              % (g, len(a), np.median(a), np.percentile(a, 25), np.percentile(a, 75),
                 a.min(), a.max()))
    # сколько спектров 2016 лежит выше медианы 2024 и наоборот — перекрытие куч
    if '2016' in vals and '2024' in vals:
        a16 = np.array([v[0] for v in vals['2016']])
        a24 = np.array([v[0] for v in vals['2024']])
        thr = 0.5 * (np.median(a16) + np.median(a24))
        print('  порог между медианами %.3f: 2016 выше него %d/%d, 2024 ниже %d/%d'
              % (thr, int((a16 > thr).sum()), len(a16),
                 int((a24 < thr).sum()), len(a24)))


def paired(pts):
    """ОДИН источник в ОДНОЙ геометрии, снятый в оба года: попарное сравнение.

    Это единственное честное сравнение приборов: разброс приведённой ширины
    внутри года велик (у одного и того же Th-232 в Дента-120 два измерения 2024
    расходятся на 10 %), и разница медиан по всей подгруппе тонет в нём. Пара
    «тот же источник, та же геометрия, другой год» убирает и состав, и геометрию,
    и остаётся только прибор.

    Ключ эталона устроен как G1S<год>_<источник>_<геометрия>[_2|_3]; пара
    строится по (источник, геометрия) после отсечения года и порядкового хвоста.
    Разные сосуды (Дента-100 в 2016, Дента-120 в 2024) в пару НЕ сходятся —
    и не должны.
    """
    print('\nпары «тот же источник, та же геометрия, другой год»:')
    per = {}
    for g, key, e, f, w in pts:
        if g == 'vibe':
            continue
        per.setdefault((g, key), []).append(f / np.sqrt(max(e, 1.0)))
    tags = {}
    for (g, key), lst in per.items():
        if len(lst) < 3:
            continue
        tag = key.split('_', 1)[1]
        if tag.endswith('_2') or tag.endswith('_3'):
            tag = tag.rsplit('_', 1)[0]
        tags.setdefault(tag, {}).setdefault(g, []).append(float(np.median(lst)))
    rows = []
    for tag in sorted(tags):
        d = tags[tag]
        if '2016' not in d or '2024' not in d:
            continue
        a16, a24 = float(np.median(d['2016'])), float(np.median(d['2024']))
        rows.append((tag, a16, len(d['2016']), a24, len(d['2024']),
                     100 * (a24 / a16 - 1)))
    for tag, a16, n16, a24, n24, dr in rows:
        print('  %-16s 2016 %.3f (n=%d)   2024 %.3f (n=%d)   2024 шире на %+6.1f %%'
              % (tag, a16, n16, a24, n24, dr))
    if rows:
        d = np.array([r[5] for r in rows])
        pos = int((d > 0).sum())
        print('  пар %d: медиана %+.1f %%, среднее %+.1f %%; знак «2024 шире» у %d из %d'
              % (len(d), np.median(d), d.mean(), pos, len(d)))
        # знаковый критерий: вероятность такого перекоса при равных приборах
        from math import comb
        n = len(d)
        k = max(pos, n - pos)
        p = 2.0 * sum(comb(n, i) for i in range(k, n + 1)) / 2.0 ** n
        print('  знаковый критерий (приборы равны): p = %.4f' % min(p, 1.0))


def per_line(pts):
    """Одни и те же энергии, снятые в оба года: попарное отношение ширин."""
    print('\nодни и те же линии в оба года (медианы ПШПВ, кэВ):')
    by_e = {}
    for g, key, e, f, w in pts:
        if g == 'vibe':
            continue
        by_e.setdefault(round(e, 0), {}).setdefault(g, []).append(f)
    rows = []
    for e in sorted(by_e):
        d = by_e[e]
        if '2016' not in d or '2024' not in d or len(d['2016']) < 3 or len(d['2024']) < 3:
            continue
        m16, m24 = float(np.median(d['2016'])), float(np.median(d['2024']))
        rows.append((e, m16, len(d['2016']), m24, len(d['2024']), 100 * (m16 / m24 - 1)))
    for e, m16, n16, m24, n24, dr in rows:
        print('  %7.1f кэВ   2016 %6.2f (n=%2d)   2024 %6.2f (n=%2d)   %+6.1f %%'
              % (e, m16, n16, m24, n24, dr))
    if rows:
        d = np.array([r[5] for r in rows])
        print('  по %d общим линиям: медиана %+.1f %%, среднее %+.1f %%, разброс %.1f %%'
              % (len(d), np.median(d), d.mean(), d.std(ddof=1)))


if __name__ == '__main__':
    main()
