# -*- coding: utf-8 -*-
"""Отсутствие линий как улика против набора. Расчёт до правки фиттера.

## Зачем

Вето по согласованности спрашивает только про ПРИНЯТЫЕ линии: легли ли они на
общую кривую. Про то, чего в наборе нет, не спрашивает никто, и это половина
доступной информации, которой мы не пользуемся.

У настоящей цепочки в вековом равновесии отсутствие информативнее присутствия.
Если 2614 кэВ есть с площадью A, то 583 кэВ ОБЯЗАНА быть с площадью, которую
кривая предсказывает, и её отсутствие — улика. У набора-обманки линии смещены на
пустые энергии; фит принимает те, что случайно сели на структуру, а остальные
проваливаются. Сейчас эти провалы игнорируются, и набор из четырёх случайно
совпавших линий, легших на правдоподобную кривую, проходит целиком — там и живёт
остаток фантомов.

## Как считается

По принятым линиям (Fisher z >= 4) строится кривая ln(S/I) = polynom(ln E) — та
же, что у вета. По ней для КАЖДОЙ линии набора предсказывается площадь
S = I * exp(curve(ln E)). Линия считается пропущенной без оправдания, если

    предсказано заметно выше критического уровня, а измерено ничего

Критический уровень берётся по Currie от нулевого сигнала: k * sigma_0, где
sigma_0 — пуассоновская погрешность чистой площади в этом окне. То есть вопрос
ставится так, как его ставит ISO 11929, только в обратную сторону: не «видна ли
линия», а «должна ли она была быть видна».

Статистика на набор — доля необъяснённых пропусков среди тех линий, которые
кривая предсказывает выше порога обнаружения.

Сравнение — при ОДИНАКОВОЙ доле пропущенных настоящих наборов. Покрытие
печатается всегда.

Запуск:  python absence_check.py [--per-det]
"""
import os
import sys
import csv
import re
import json
import xml.etree.ElementTree as ET

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(os.path.dirname(HERE), 'corpus')
sys.path.insert(0, HERE)

BASE_ORDER = 2
WIN_SIGMA = 3.5
FLANK_SIGMA = 2.0
ACCEPT_Z = 4.0        # порог принятия линии, как у Fisher z в production
CRIT_K = float(os.environ.get('AC_CRIT', '3.0'))          # во сколько сигм предсказание должно превышать ноль,
                      # чтобы отсутствие линии считалось уликой
MIN_LINES = 4         # ниже вето не строит кривую


def read_csv(path, encoding='utf-8-sig'):
    with open(path, encoding=encoding, newline='') as fh:
        return list(csv.DictReader(fh))


def fwhm_kev(res, e):
    return float(np.sqrt(max(res[0] + res[1] * e + res[2] * e * e, 1e-6)))


def load_spectrum(key):
    rd = ET.parse(os.path.join(CORPUS, 'spectra', key + '.xml')).getroot() \
        .find('ResultDataList/ResultData')
    es = rd.find('EnergySpectrum')
    counts = np.array([int(d.text) for d in es.findall('Spectrum/DataPoint')], dtype=float)
    ecal = [float(x.text) for x in es.findall('EnergyCalibration/Coefficients/Coefficient')]
    ch = np.arange(len(counts), dtype=float)
    return counts, sum(c * ch ** i for i, c in enumerate(ecal))


def net_area(counts, energy, e0, fwhm):
    sigma = fwhm / 2.3548
    win = (energy >= e0 - WIN_SIGMA * sigma) & (energy <= e0 + WIN_SIGMA * sigma)
    if win.sum() < 9:
        return None
    x, y = energy[win], counts[win]
    g = np.exp(-0.5 * ((x - e0) / sigma) ** 2)
    flank = np.abs(x - e0) > FLANK_SIGMA * sigma
    if flank.sum() < BASE_ORDER + 3:
        return None
    try:
        base = np.polyval(np.polyfit(x[flank] - e0, y[flank], BASE_ORDER), x - e0)
    except Exception:
        return None
    gg = float((g * g).sum())
    if gg <= 1e-9:
        return None
    amp = float(((y - base) * g).sum()) / gg
    var = float(((np.maximum(y, 0.0) + np.maximum(base, 0.0)) * g * g).sum()) / gg ** 2
    scale = sigma * np.sqrt(2.0 * np.pi)
    return amp * scale, np.sqrt(max(var, 1e-12)) * scale


def load_sets():
    path = os.path.join(HERE, 'sets_manifest.json')
    if not os.path.exists(path):
        sys.exit('нет sets_manifest.json — сначала gate_study.py --run')
    out = {}
    for m in json.load(open(path, encoding='utf-8')):
        out.setdefault((m['det'], m['chain'], m['kind']), []).append(m)
    return out


def at_rate(pairs, target, higher_is_worse=True):
    """Доля прошедших обманок при доле пропущенных настоящих не ниже target."""
    pairs = sorted(pairs, key=lambda t: t[0] if higher_is_worse else -t[0])
    nr = sum(1 for _v, k in pairs if k == 'real')
    nd = len(pairs) - nr
    r = d = 0
    for _v, k in pairs:
        if k == 'real':
            r += 1
        else:
            d += 1
        if nr and r / nr >= target:
            return d / max(nd, 1)
    return None


def main():
    per_det = '--per-det' in sys.argv
    manifest = read_csv(os.path.join(CORPUS, 'manifest.csv'))
    dets = {d['det']: d for d in read_csv(os.path.join(CORPUS, 'detectors.csv'))}
    sets = load_sets()

    miss = []          # (доля необъяснённых пропусков, kind)
    scat = []          # (разброс вокруг кривой, kind) — база для сравнения
    comb = []          # (разброс * (1 + доля пропусков), kind)
    defl = []          # (средняя недостача в сигмах, kind)
    combd = []         # (разброс * (1 + недостача), kind)
    per = {}
    n_inst = n_short = n_nopred = 0

    for row in manifest:
        det = row['det']
        chains = [c for c in re.split(r'[;|]', row.get('chains') or '') if c]
        d = dets.get(det)
        if not chains or not d:
            continue
        res = (float(d['res_c0']), float(d['res_c1']), float(d['res_c2']))
        lo, hi = float(d['e_lo']), float(d['e_hi'])
        try:
            counts, energy = load_spectrum(row['key'])
        except Exception:
            continue

        for chain in chains:
            for kind in ('real', 'decoy'):
                for m in sets.get((det, chain, kind), []):
                    n_inst += 1
                    meas = []          # (E, I, площадь, сигма, принята)
                    for line in m['lines']:
                        e0, inten = float(line['e']), float(line['i'])
                        if not (lo <= e0 <= hi) or inten <= 0:
                            continue
                        r = net_area(counts, energy, e0, fwhm_kev(res, e0))
                        if r is None:
                            continue
                        a, sp = r
                        meas.append((e0, inten, a, sp, a > 0 and a / max(sp, 1e-9) >= ACCEPT_Z))

                    acc = [t for t in meas if t[4]]
                    if len(acc) < MIN_LINES:
                        n_short += 1
                        continue

                    x = np.log([t[0] for t in acc])
                    y = np.log([t[2] / t[1] for t in acc])
                    order = 2 if len(x) >= 5 else 1
                    A = np.vander(x, order + 1, increasing=True)
                    coef, _r, _rk, _s = np.linalg.lstsq(A, y, rcond=None)
                    resid = y - A.dot(coef)
                    scatter = float(np.exp(np.sqrt((resid ** 2).mean())) - 1.0)

                    # предсказание по кривой для ВСЕХ линий набора
                    expected = 0
                    unexplained = 0
                    deficit = 0.0
                    for e0, inten, a, sp, accepted in meas:
                        pred = inten * float(np.exp(np.polyval(coef[::-1], np.log(e0))))
                        if pred < CRIT_K * sp:
                            continue            # линия и не должна была быть видна
                        expected += 1
                        if not accepted:
                            unexplained += 1
                        # насколько недостача велика В СИГМАХ: пропущенная сильная
                        # линия — улика тяжелее, чем пропущенная едва заметная
                        deficit += max(0.0, (pred - a) / max(sp, 1e-9))
                    if expected == 0:
                        n_nopred += 1
                        continue
                    frac = unexplained / expected
                    defz = deficit / expected

                    miss.append((frac, kind))
                    scat.append((scatter, kind))
                    comb.append((scatter * (1.0 + frac), kind))
                    defl.append((defz, kind))
                    combd.append((scatter * (1.0 + defz), kind))
                    slot = per.setdefault(det, {'miss': [], 'scat': [], 'comb': [], 'combd': []})
                    slot['miss'].append((frac, kind))
                    slot['scat'].append((scatter, kind))
                    slot['comb'].append((scatter * (1.0 + frac), kind))
                    slot['combd'].append((scatter * (1.0 + defz), kind))

    nr = sum(1 for _v, k in scat if k == 'real')
    nd = len(scat) - nr
    print('экземпляров набора: %d | короче %d принятых линий: %d | нечего предсказывать: %d'
          % (n_inst, MIN_LINES, n_short, n_nopred))
    print('в сравнении: настоящих %d, обманок %d' % (nr, nd))
    print()

    for name, data in (('доля пропусков', miss), ('разброс (вето)', scat),
                       ('недостача, сигмы', defl), ('разброс x пропуски', comb),
                       ('разброс x недостача', combd)):
        vr = [v for v, k in data if k == 'real']
        vd = [v for v, k in data if k == 'decoy']
        print('%-20s медиана: настоящие %6.2f  обманки %6.2f'
              % (name, float(np.median(vr)) if vr else float('nan'),
                 float(np.median(vd)) if vd else float('nan')))
    print()

    TARGETS = (0.60, 0.70, 0.80, 0.90, 0.95)
    print('доля ПРОШЕДШИХ обманок при одинаковой доле пропущенных настоящих наборов')
    print('%-20s' % 'критерий' + ''.join('%9s' % ('%.0f%%' % (t * 100)) for t in TARGETS))
    print('-' * (20 + 9 * len(TARGETS)))
    for name, data in (('разброс (вето)', scat), ('доля пропусков', miss),
                       ('недостача, сигмы', defl),
                       ('разброс x пропуски', comb), ('разброс x недостача', combd)):
        cells = []
        for t in TARGETS:
            r = at_rate(data, t)
            cells.append('%8.1f%%' % (100.0 * r) if r is not None else '       -')
        print('%-20s' % name + ''.join(cells))

    dump = os.environ.get('AC_DUMP')
    if dump:
        with open(dump, 'w', encoding='utf-8', newline='') as fh:
            w = csv.writer(fh)
            w.writerow(['scatter', 'missfrac', 'kind'])
            for (sc, k), (mf, _k2) in zip(scat, miss):
                w.writerow(['%.6f' % sc, '%.6f' % mf, k])

    if per_det:
        print()
        print('%-12s %5s %11s %11s' % ('детектор', 'наб.', 'вето обм.', 'x пропуски'))
        for det in sorted(per):
            a = at_rate(per[det]['scat'], 0.80)
            b = at_rate(per[det]['comb'], 0.80)
            n = sum(1 for _v, k in per[det]['scat'] if k == 'real')
            if a is None or b is None:
                continue
            print('%-12s %5d %10.1f%% %10.1f%%' % (det, n, 100.0 * a, 100.0 * b))


main()
