# -*- coding: utf-8 -*-
"""Осуществима ли проверка согласованности набора по кривой относительной эффективности.

Расчёт ДО реализации гейта, на полном корпусе.

Идея. Если все линии цепочки в вековом равновесии делят одну активность, то
S_i / I_i пропорционально эффективности регистрации на этой энергии, и точки
обязаны лечь на одну гладкую кривую (Рейлли, гл. 8: RE(E) ∝ C(E)/BR, важна только
форма). У сета-обманки интенсивности табличные, а площади набраны из того, что
случайно оказалось на сдвинутой энергии, — согласоваться с кривой они не обязаны.

Первая версия этой проверки фитила кривую по ВСЕМ линиям набора и мерила chi2/dof.
Результат был бессмысленным (медиана 1172 у настоящих цепочек против 1944 у
обманок), и причина оказалась не в гипотезе:

* линейная подложка на крутом комптоновском континууме давала ОТРИЦАТЕЛЬНЫЕ
  площади у половины линий — 39.9, 409, 463, 511, 675 кэВ у ASN16_Th232;
* логарифм выбрасывал такие точки вовсе, и у обманки оставались только те
  позиции, где что-то случайно нашлось, с огромными погрешностями — отсюда
  chi2/dof = 0.2 у обманки ASN8_8192;
* пуассоновская погрешность при 10^8 отсчётов — доли процента, так что любое
  систематическое отклонение в 10 % даёт chi2/dof в сотни. Тест проверял «точна
  ли модель», а не «согласован ли набор».

Здесь конструкция другая, и она ближе к тому, чем гейт мог бы быть в
производстве:

  1. Подложка квадратичная, окно 3.5 sigma, крылья от 2.0 sigma — на этом
     площади перестают быть отрицательными.
  2. Кривая строится по ОПОРНЫМ линиям: сильным (I >= I_REF_STRONG), одиночным
     и уверенно измеренным. Их площади надёжны.
  3. Слабые линии ПРОВЕРЯЮТСЯ против кривой: отношение измеренной площади к
     предсказанной A*I*eps(E). Настоящая линия должна дать около единицы,
     фантом — что угодно.
  4. Обманка проверяется той же кривой цепочки: в производстве кривую строить
     будет не на чем (у обманки настоящий только якорь), и это само по себе
     ответ, но здесь нужен именно контроль разделяющей способности.

Запуск: python re_curve_check.py [--per-spectrum]
"""
import csv
import json
import os
import sys
from collections import defaultdict

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)
CORPUS = os.path.join(LAB, 'corpus')

sys.path.insert(0, HERE)
import xml.etree.ElementTree as ET                      # noqa: E402
from chains import chain_lines, CHAINS                  # noqa: E402

I_REF_WEAK = 1.0      # проверяемые линии: от этой интенсивности на распад родителя, %
I_REF_STRONG = 10.0   # опорные линии, по которым строится кривая
MIN_Z_REF = 20.0      # и только уверенно измеренные
PURITY = 0.80         # доля своей интенсивности в группе внутри 1 FWHM
BASE_ORDER = 2        # порядок локальной подложки
WIN_SIGMA = 3.5
FLANK_SIGMA = 2.0
CONSISTENT = (1.0 / 3.0, 3.0)   # во сколько раз площадь может разойтись с предсказанием

U238_HEAD = {'238U', '234TH', '234PAm1', '234PA', '234U', '230TH'}
CHAIN_MODE = {'Th-232': ('Th-232', 'pos'), 'Ra-226': ('Ra-226', 'pos'),
              'U-238': ('U-238', 'pos'), 'U-238u': ('U-238', 'head'),
              'U-235': ('U-235', 'pos')}


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
    """(площадь, погрешность) гауссианы табличной ширины над локальной подложкой."""
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


def chain_rows(chain, mode, lo, hi):
    rows = [r for r in chain_lines(CHAINS[chain]) if lo <= r['energy'] <= hi]
    if mode == 'head':
        rows = [r for r in rows if r['nucid'] in U238_HEAD]
    return rows


def purity_of(row, rows, res):
    w = fwhm_kev(res, row['energy'])
    near = [q for q in rows if abs(q['energy'] - row['energy']) <= w]
    total = sum(q['i_chain'] for q in near)
    return row['i_chain'] / total if total > 0 else 0.0


def fit_curve(e, ratio):
    """ln(S/I) = polynom(ln E). Порядок по числу точек, но не выше второго."""
    order = 2 if len(e) >= 5 else 1
    if len(e) < order + 2:
        return None
    x, y = np.log(e), np.log(ratio)
    coef = np.polyfit(x, y, order)
    resid = y - np.polyval(coef, x)
    return coef, float(np.sqrt((resid ** 2).mean())), order


def decoy_lines():
    path = os.path.join(HERE, 'sets_manifest.json')
    if not os.path.exists(path):
        return {}
    out = {}
    for m in json.load(open(path, encoding='utf-8')):
        if m['kind'] == 'decoy':
            out[(m['det'], m['chain'])] = [(l['e'], l['i']) for l in m['lines']
                                           if l.get('decoy')]
    return out


def main():
    per_spectrum = '--per-spectrum' in sys.argv
    dets = {r['det']: r for r in read_csv(os.path.join(CORPUS, 'detectors.csv'))}
    decoys = decoy_lines()

    stat = defaultdict(lambda: dict(nref=[], scatter=[], real_ok=0, real_n=0,
                                    decoy_ok=0, decoy_n=0, nocurve=0))
    detail = []
    for row in read_csv(os.path.join(CORPUS, 'manifest.csv')):
        det = dets.get(row['det'])
        if det is None:
            continue
        res = [float(det['res_c0']), float(det['res_c1']), float(det['res_c2'])]
        lo, hi = float(det['e_lo']), float(det['e_hi'])
        counts, energy = load_spectrum(row['key'])

        for tag in (row['chains'] or '').split(';'):
            if tag not in CHAIN_MODE:
                continue
            chain, mode = CHAIN_MODE[tag]
            rows = chain_rows(chain, mode, lo, hi)

            measured = {}
            for r in rows:
                if r['i_chain'] < I_REF_WEAK or purity_of(r, rows, res) < PURITY:
                    continue
                m = net_area(counts, energy, r['energy'], fwhm_kev(res, r['energy']))
                if m is None:
                    continue
                measured[r['energy']] = (r, m[0], m[1])

            ref = [(r, s, sg) for r, s, sg in measured.values()
                   if r['i_chain'] >= I_REF_STRONG and s > 0 and s / sg >= MIN_Z_REF]
            if len(ref) < 3:
                stat[row['det']]['nocurve'] += 1
                continue
            fit = fit_curve(np.array([r['energy'] for r, _, _ in ref]),
                            np.array([s / r['i_chain'] for r, s, _ in ref]))
            if fit is None:
                stat[row['det']]['nocurve'] += 1
                continue
            coef, scatter, order = fit
            stat[row['det']]['nref'].append(len(ref))
            stat[row['det']]['scatter'].append(scatter)

            # --- слабые линии настоящего набора против кривой ---
            real_ok = real_n = 0
            for r, s, sg in measured.values():
                if r['i_chain'] >= I_REF_STRONG:
                    continue
                predicted = r['i_chain'] * np.exp(np.polyval(coef, np.log(r['energy'])))
                real_n += 1
                if predicted > 0 and CONSISTENT[0] <= s / predicted <= CONSISTENT[1]:
                    real_ok += 1

            # --- линии обманки против той же кривой ---
            decoy_ok = decoy_n = 0
            for e0, inten in decoys.get((row['det'], chain), []):
                if not (lo <= e0 <= hi) or inten <= 0:
                    continue
                m = net_area(counts, energy, e0, fwhm_kev(res, e0))
                if m is None:
                    continue
                predicted = inten * np.exp(np.polyval(coef, np.log(e0)))
                decoy_n += 1
                if predicted > 0 and CONSISTENT[0] <= m[0] / predicted <= CONSISTENT[1]:
                    decoy_ok += 1

            b = stat[row['det']]
            b['real_ok'] += real_ok
            b['real_n'] += real_n
            b['decoy_ok'] += decoy_ok
            b['decoy_n'] += decoy_n
            detail.append((row['key'], chain, len(ref), scatter, order,
                           real_ok, real_n, decoy_ok, decoy_n))

    if per_spectrum:
        print('%-20s %-8s %4s %8s %12s %12s' % (
            'спектр', 'цепочка', 'оп', 'разброс', 'слабые наст', 'обманка'))
        print('-' * 70)
        for key, chain, nref, sc, order, ro, rn, do, dn in sorted(detail):
            print('%-20s %-8s %4d %7.1f%% %6d/%-5d %6d/%-5d' % (
                key, chain, nref, 100 * (np.exp(sc) - 1), ro, rn, do, dn))
        print()

    print('%-10s %6s %9s %14s %14s %8s' % (
        'детектор', 'наборов', 'опорных', 'слабые наст.', 'обманка', 'нет кривой'))
    print('-' * 68)
    tot = dict(real_ok=0, real_n=0, decoy_ok=0, decoy_n=0, nocurve=0, scatter=[])
    for det in sorted(stat):
        b = stat[det]
        if not b['nref']:
            print('%-10s %6d %9s %14s %14s %8d' % (det, 0, '-', '-', '-', b['nocurve']))
            tot['nocurve'] += b['nocurve']
            continue
        rr = '%5.1f%% (%d)' % (100.0 * b['real_ok'] / b['real_n'], b['real_n']) \
            if b['real_n'] else '   -'
        dr = '%5.1f%% (%d)' % (100.0 * b['decoy_ok'] / b['decoy_n'], b['decoy_n']) \
            if b['decoy_n'] else '   -'
        print('%-10s %6d %9.1f %14s %14s %8d' % (
            det, len(b['nref']), np.mean(b['nref']), rr, dr, b['nocurve']))
        for k in ('real_ok', 'real_n', 'decoy_ok', 'decoy_n', 'nocurve'):
            tot[k] += b[k]
        tot['scatter'].extend(b['scatter'])
    print('-' * 68)
    print('%-10s %6d %9s %14s %14s %8d' % (
        'ВСЕГО', len(tot['scatter']), '',
        '%5.1f%% (%d)' % (100.0 * tot['real_ok'] / tot['real_n'], tot['real_n'])
        if tot['real_n'] else '-',
        '%5.1f%% (%d)' % (100.0 * tot['decoy_ok'] / tot['decoy_n'], tot['decoy_n'])
        if tot['decoy_n'] else '-', tot['nocurve']))
    if tot['scatter']:
        print()
        print('разброс опорных линий вокруг кривой: медиана %.1f %%, худший %.1f %%' % (
            100 * (np.exp(np.median(tot['scatter'])) - 1),
            100 * (np.exp(max(tot['scatter'])) - 1)))


if __name__ == '__main__':
    main()
