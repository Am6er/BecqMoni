# -*- coding: utf-8 -*-
"""Внешняя кривая эффективности вместо подогнанной по набору. Расчёт до кода.

## Зачем

Оба вета по набору опираются на кривую `ln(S/I) = polynom(ln E)`, ПОДОГНАННУЮ
по тем же линиям, которые они судят. Отсюда два измеренных провала:

* поимённое исключение выбросов подняло фантомы с 7.1 до 49.9 %, потому что
  удаление худшей невязки оптимизирует ровно ту статистику, по которой идёт
  вердикт: улика правится под вердикт;
* предсказание площади в вето по отсутствиям опирается на ту же самую кривую,
  то есть отсутствие проверяется тем, что построено по присутствующим.

PACE у Canberra делает внешне то же и работает — разница в том, что там
ожидаемые площади считаются по НЕЗАВИСИМО откалиброванной кривой. Verter73
прислал такую: аттестация ЛСРМ того же экземпляра NaI 63×63 (группа `G1S`),
95 точек, пять геометрий, получена вне корпусных измерений
(`data/eff_curve_g1s.csv`).

## Что считается

Единица — экземпляр набора (спектр x сет), как в production. Для каждого:

  вариант «своя»    — кривая подгоняется по принятым линиям (как сейчас);
  вариант «внешняя» — форма кривой ФИКСИРОВАНА аттестацией, свободна только
                      активность: один масштаб на набор. Разброс считается
                      вокруг неё.

Форма и есть то, что проверяет вето; масштаб набор всё равно не знает, поэтому
свободный множитель законен и разницы 1999/2016 по абсолютной эффективности он
поглощает. Что он НЕ поглощает — разницу матриц (ро 0.6 против 1.6), она меняет
форму на низких энергиях; маринелли поэтому помечен отдельно.

Проверяются три вещи:

1. разделяет ли внешняя кривая лучше своей;
2. становится ли поимённое исключение выбросов корректным приёмом — при
   фиксированной форме удаление точки её не переформовывает;
3. улучшается ли предсказание в вето по отсутствиям.

Запуск:  python external_eff_check.py
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
DATA = os.path.join(os.path.dirname(HERE), 'data')
sys.path.insert(0, HERE)

BASE_ORDER = 2
WIN_SIGMA = 3.5
FLANK_SIGMA = 2.0
ACCEPT_Z = 4.0
MIN_LINES = 4
CRIT_K = 5.0          # порог видимости для вета по отсутствиям

# Геометрия корпусного спектра -> геометрия в аттестации.
GEOMETRY = {
    'G1S_Th232_Denta': 'Дента-120мл',
    'G1S_Ra226_Denta': 'Дента-120мл',
    'G1S_K40_Denta': 'Дента-120мл',
    'G1S_Th232_Marinelli': 'Маринелли',
    'G1S_Th232_Petri': 'Петри-60',
    'G1S_Ra226_Petri': 'Петри-60',
    'G1S_Th228_25cm': 'Точечная-25см',
    'G1S_Eu152_25cm': 'Точечная-25см',
    'G1S_Co60_25cm': 'Точечная-25см',
    'G1S_Ba133_25cm': 'Точечная-25см',
    'G1S_Th228_5cm': 'Точечная-5см',
    'G1S_Eu152_5cm': 'Точечная-5см',
}
# Корпусные маринелли — записи набора 1999 года (ро ~0.6), а кривая построена по
# серии 2016 года (ро 1.6). Свободный масштаб разницу активностей поглощает,
# разницу матриц — нет. Помечаем, чтобы отделить в отчёте.
SUSPECT = {'G1S_Th232_Marinelli'}


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


def load_external():
    """geometry -> (lnE, ln eps) для интерполяции формы."""
    path = os.path.join(DATA, 'eff_curve_g1s.csv')
    if not os.path.exists(path):
        sys.exit('нет data/eff_curve_g1s.csv')
    by = {}
    for r in read_csv(path, encoding='utf-8'):
        by.setdefault(r['geometry'], []).append(
            (np.log(float(r['E_keV'])), np.log(float(r['eps']))))
    out = {}
    for g, pts in by.items():
        pts.sort()
        out[g] = (np.array([p[0] for p in pts]), np.array([p[1] for p in pts]))
    return out


def ext_ln_eps(curve, lnE):
    """ln eps(E) линейной интерполяцией в логарифмах. Вне узлов — NaN.

    Экстраполировать нельзя, и это стоило отдельного прогона. У геометрии
    «Дента» кривая начинается с 238.6 кэВ, а первые два узла отстоят на 3 кэВ
    при разнице эффективности 30 % — наклон между ними шумовой. Продолжение по
    нему до 129 кэВ дало ln eps = -14.8 вместо -2.7, один такой выброс раздул
    разброс набора с 0.5 до 31, и внешняя кривая выглядела негодной. Линии вне
    покрытия надо ИСКЛЮЧАТЬ и считать, сколько исключено.
    """
    x, y = curve
    if lnE < x[0] or lnE > x[-1]:
        return float('nan')
    return float(np.interp(lnE, x, y))


def load_sets():
    path = os.path.join(HERE, 'sets_manifest.json')
    if not os.path.exists(path):
        sys.exit('нет sets_manifest.json')
    out = {}
    for m in json.load(open(path, encoding='utf-8')):
        out.setdefault((m['det'], m['chain'], m['kind']), []).append(m)
    return out


def at_rate(pairs, target):
    pairs = sorted(pairs, key=lambda t: t[0])
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


def scatter_own(x, y):
    order = 2 if len(x) >= 5 else 1
    A = np.vander(x, order + 1, increasing=True)
    coef, _r, _rk, _s = np.linalg.lstsq(A, y, rcond=None)
    resid = y - A.dot(coef)
    return float(np.exp(np.sqrt((resid ** 2).mean())) - 1.0), coef, order


def scatter_ext(x, y, curve):
    """Форма фиксирована, свободен только масштаб: ln A = mean(y - ln eps).

    Возвращает (разброс, lnA, model, сколько линий вне покрытия). Точки вне
    диапазона аттестации в подгонку не входят.
    """
    model = np.array([ext_ln_eps(curve, v) for v in x])
    ok = np.isfinite(model)
    out = int((~ok).sum())
    if ok.sum() < MIN_LINES:
        return None, None, model, out
    lnA = float(np.mean(y[ok] - model[ok]))
    resid = y[ok] - (model[ok] + lnA)
    return float(np.exp(np.sqrt((resid ** 2).mean())) - 1.0), lnA, model, out


def trim_curve(x, y, limit, own, curve, grubbs=2.5, max_frac=0.34):
    """Поимённое исключение выбросов. Возвращает (уложился, сколько выброшено)."""
    idx = list(range(len(x)))
    allowed = int(len(x) * max_frac)
    for dropped in range(allowed + 1):
        xs = np.array([x[i] for i in idx])
        ys = np.array([y[i] for i in idx])
        if len(xs) < MIN_LINES:
            return False, dropped
        if own:
            sc, coef, order = scatter_own(xs, ys)
            A = np.vander(xs, order + 1, increasing=True)
            resid = np.abs(ys - A.dot(coef))
        else:
            sc, lnA, model, _out = scatter_ext(xs, ys, curve)
            if sc is None:
                return False, dropped
            resid = np.abs(np.nan_to_num(ys - (model + lnA), nan=0.0))
        if sc <= limit:
            return True, dropped
        if dropped >= allowed:
            return False, dropped
        w = int(np.argmax(resid))
        rest = np.delete(resid, w)
        if len(rest) < 2 or resid[w] < grubbs * float(np.sqrt((rest ** 2).mean())):
            return False, dropped
        idx.pop(w)
    return False, allowed


def main():
    manifest = {r['key']: r for r in read_csv(os.path.join(CORPUS, 'manifest.csv'))}
    dets = {d['det']: d for d in read_csv(os.path.join(CORPUS, 'detectors.csv'))}
    sets = load_sets()
    ext = load_external()

    own_pairs, ext_pairs = [], []
    miss_own, miss_ext = [], []
    trim_stat = {'своя': [0, 0], 'внешняя': [0, 0]}      # [обманок уложено, настоящих уложено]
    n_inst = n_short = n_uncovered = n_nocurve = 0
    suspects = []

    d = dets['G1S']
    res = (float(d['res_c0']), float(d['res_c1']), float(d['res_c2']))
    lo, hi = float(d['e_lo']), float(d['e_hi'])

    for key, geom in sorted(GEOMETRY.items()):
        row = manifest.get(key)
        if row is None:
            continue
        chains = [c for c in re.split(r'[;|]', row.get('chains') or '') if c]
        if not chains:
            continue
        curve = ext.get(geom)
        if curve is None:
            continue
        counts, energy = load_spectrum(key)

        for chain in chains:
            for kind in ('real', 'decoy'):
                for m in sets.get(('G1S', chain, kind), []):
                    n_inst += 1
                    meas = []
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
                    sc_own, coef, order = scatter_own(x, y)
                    sc_ext, lnA, model, n_out = scatter_ext(x, y, curve)
                    n_uncovered += n_out
                    own_pairs.append((sc_own, kind))
                    if sc_ext is None:
                        n_nocurve += 1
                        continue
                    ext_pairs.append((sc_ext, kind))
                    if key in SUSPECT:
                        suspects.append((key, kind, sc_own, sc_ext))

                    # вето по отсутствиям на обеих кривых
                    for tag, use_own, store in (('own', True, miss_own), ('ext', False, miss_ext)):
                        expected = missing = 0
                        for e0, inten, a, sp, accepted in meas:
                            lnE = np.log(e0)
                            if use_own:
                                mdl = sum(coef[k] * lnE ** k for k in range(order + 1))
                            else:
                                mdl = ext_ln_eps(curve, lnE) + lnA
                                if not np.isfinite(mdl):
                                    continue        # энергия вне покрытия аттестации
                            pred = inten * float(np.exp(mdl))
                            if not np.isfinite(pred) or pred < CRIT_K * sp:
                                continue
                            expected += 1
                            if not accepted:
                                missing += 1
                        if expected:
                            store.append((missing / expected, kind))

                    # поимённое исключение выбросов
                    for tag, use_own in (('своя', True), ('внешняя', False)):
                        ok, _dropped = trim_curve(x, y, 1.25, use_own, curve)
                        if ok:
                            trim_stat[tag][0 if kind == 'decoy' else 1] += 1

    nr = sum(1 for _v, k in own_pairs if k == 'real')
    nd = len(own_pairs) - nr
    print('экземпляров набора: %d, коротких: %d' % (n_inst, n_short))
    print('линий вне покрытия аттестации: %d; наборов без кривой: %d' % (n_uncovered, n_nocurve))
    print('в сравнении: настоящих %d, обманок %d (только группа G1S)' % (nr, nd))
    print()

    T = (0.60, 0.70, 0.80, 0.90, 1.00)
    print('доля прошедших обманок при одинаковой доле пропущенных настоящих')
    print('%-22s' % 'кривая' + ''.join('%9s' % ('%.0f%%' % (t * 100)) for t in T))
    print('-' * (22 + 9 * len(T)))
    for name, data in (('разброс, своя', own_pairs), ('разброс, внешняя', ext_pairs),
                       ('пропуски, своя', miss_own), ('пропуски, внешняя', miss_ext)):
        cells = []
        for t in T:
            v = at_rate(data, t)
            cells.append('%8.1f%%' % (100.0 * v) if v is not None else '       -')
        print('%-22s' % name + ''.join(cells))

    print()
    for name, data in (('разброс, своя', own_pairs), ('разброс, внешняя', ext_pairs)):
        vr = [v for v, k in data if k == 'real']
        vd = [v for v, k in data if k == 'decoy']
        print('%-22s медиана: настоящие %6.2f  обманки %6.2f' %
              (name, float(np.median(vr)) if vr else float('nan'),
               float(np.median(vd)) if vd else float('nan')))

    print()
    print('поимённое исключение выбросов: сколько наборов удалось «уложить» в порог 1.25')
    print('%-12s %14s %14s' % ('кривая', 'настоящих', 'ОБМАНОК'))
    for tag in ('своя', 'внешняя'):
        dec, real = trim_stat[tag]
        print('%-12s %10d/%-3d %10d/%-3d' % (tag, real, nr, dec, nd))
    print('(чем больше обманок «укладывается», тем приём вреднее)')

    if suspects:
        print()
        print('маринелли 1999 против кривой 2016 (матрица другая):')
        for k, kind, a, b in suspects:
            print('  %-24s %-6s своя %.2f  внешняя %.2f' % (k, kind, a, b))


main()
