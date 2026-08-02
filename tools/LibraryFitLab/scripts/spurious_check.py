# -*- coding: utf-8 -*-
"""Проверка гипотезы о ВХОДНОМ гейте: эмпирическая нулевая вместо пуассоновской.

Расчёт ДО реализации, тем же порядком, каким проверялось вето по набору
(re_curve_check.py). Две прежние постановки вето провалились именно на этой
стадии и стоили одного скрипта вместо прогона корпуса.

## Что проверяется

Все прежние критерии на линию — Fisher z, ΔD, shape — считают значимость
относительно ОДНОЙ И ТОЙ ЖЕ подложки, и знаменатель у них пуассоновский. Отсюда
измеренный провал: при номинальном уровне z >= 4 (alpha ~ 3e-5) обманки
проходят в 64% случаев. Расхождение в четыре порядка означает, что неверен не
порог, а нулевая гипотеза: «амплитуда равна нулю ПРИ ПРАВИЛЬНОМ континууме», а
континуум неправильный.

В физике высоких энергий у этого есть имя — spurious signal: в модель
«сигнал + фон» подставляют заведомо бессигнальный участок и вытащенный оттуда
«сигнал» объявляют систематикой. Здесь это делается на месте: тот же профиль
табличной ширины меряется в K смещённых позициях по соседству, где линии нет, и
разброс этих площадей и есть локальная ошибка модели континуума — в этой
энергии, на этом спектре, на этом детекторе.

    z_rob = A / sqrt(sigma_пуассон^2 + s^2)

s оценивается робастно (MAD): часть смещений всё равно сядет на линии, которых
нет в проверяемом наборе, и среднее это бы испортило.

## Почему это должно самокалиброваться

Отчёт Verter73 к PR #32 показал две вещи, которых на нашем корпусе видно не
было: на германии выключенный нами shape даёт вдвенадцатеро меньше фантомов,
чем вето, а порог вето 1.25 — функция разрешения, а не константа. Обе — про то,
что единой рабочей точки на все классы детекторов нет. Разброс смещений растёт
ровно там, где континуум описан плохо, поэтому z_rob не требует порога на класс.

Запуск:  python spurious_check.py [--per-det] [--offsets=N]
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

from chains import chain_lines, CHAINS                  # noqa: E402

I_REF = 1.0           # линии сета: от этой интенсивности на распад родителя, %
I_STRONG = 1.0        # знаменатель recall — те же сильные линии, что и везде
BASE_ORDER = 2        # порядок локальной подложки
WIN_SIGMA = 3.5
FLANK_SIGMA = 2.0
MERGE_FWHM = 0.6      # слитые ближе этого считаются за одну линию

# Смещения в полуширинах. Ближе 2 FWHM — крылья самой линии, дальше 8 — уже
# другой участок континуума, и оценка перестаёт быть локальной.
_R_IN = float(os.environ.get('SC_RIN', '3.5'))
_R_OUT = float(os.environ.get('SC_ROUT', '9.0'))
_STEP = 0.5
_ladder = [_R_IN + _STEP * i for i in range(int((_R_OUT - _R_IN) / _STEP) + 1)]
OFFSETS = tuple(-x for x in reversed(_ladder)) + tuple(_ladder)

# Маска не нужна. Первая версия выбрасывала смещения ближе 1.5 FWHM к любой
# линии набора — и теряла 4305 смещений из 7030, потому что рядом с линией
# цепочки на сцинтилляторе пустого места просто нет: без оценки оставались
# 62% линий, а на подвыборке выводов не делают. Загрязнение чужой линией
# ОДНОСТОРОННЕЕ — оно двигает площадь только вверх, — поэтому масштаб берётся
# по нижней половине выборки, и маскировать ничего не требуется.
MIN_OFFSETS = 6

CHAIN_MODE = {'Th-232': ('Th-232', 'pos'), 'Th-228': ('Th-228', 'pos'),
              'Ra-226': ('Ra-226', 'pos'),
              'U-238': ('U-238', 'pos'), 'U-238u': ('U-238', 'head'),
              'U-235': ('U-235', 'pos')}
U238_HEAD = {'238U', '234TH', '234PAm1', '234PA', '234U', '230TH'}


# Тег манифеста -> цепочка в sets_manifest. Манифест помечает голову ряда как
# `U-238u`, а сеты знают только `U-238`: прямой поиск терял ШЕСТЬ урановых
# спектров (все стёкла, GS4000_U, HPGE_Uranium) — то есть именно оборванные
# ряды, самый жёсткий случай для вета по отсутствиям.
CHAIN_TAG = {'U-238u': 'U-238'}


def set_chain(tag):
    return CHAIN_TAG.get(tag, tag)


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
    """(площадь, пуассоновская погрешность) гауссианы табличной ширины.

    Подложка — полином BASE_ORDER, подогнанный ТОЛЬКО по крыльям окна и никак
    не связанный ни со SNIP, ни с фоном прибора. Амплитуда линейна, поэтому
    оценка всегда возвращает число с честной погрешностью и не упирается в
    границу области, как координатный спуск, зажатый нулём снизу.
    """
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


def spurious_scales(counts, energy, e0, fwhm):
    """Разброс площадей в смещённых позициях = локальная ошибка модели фона.

    Возвращает несколько оценок масштаба, чтобы выбрать по результату, а не по
    вкусу. Загрязнение чужими линиями одностороннее (вверх), поэтому оценки,
    опирающиеся на нижнюю половину, устойчивы к нему при любой доле.

      mad   — обычный MAD: эталон, ломается при загрязнении свыше половины;
      lmad  — MAD по отклонениям ВНИЗ от медианы;
      q16   — расстояние от медианы до 16-го процентиля (для нормальной ~сигма);
      iqr   — межквартильный размах / 1.349.
    """
    areas = []
    for k in OFFSETS:
        e = e0 + k * fwhm
        if e <= energy[0] or e >= energy[-1]:
            continue
        r = net_area(counts, energy, e, fwhm)
        if r is not None:
            areas.append(r[0])
    if len(areas) < MIN_OFFSETS:
        return None
    a = np.array(areas)
    med = float(np.median(a))
    lower = a[a <= med]
    out = {'mad': 1.4826 * float(np.median(np.abs(a - med)))}
    out['lmad'] = 1.4826 * float(np.median(np.abs(lower - med))) if lower.size else 0.0
    out['q16'] = med - float(np.percentile(a, 16))
    out['iqr'] = (float(np.percentile(a, 75)) - float(np.percentile(a, 25))) / 1.349
    return {k: max(v, 0.0) for k, v in out.items()}


def chain_rows(chain, mode, lo, hi):
    rows = [r for r in chain_lines(CHAINS[chain]) if lo <= r['energy'] <= hi]
    if mode == 'head':
        rows = [r for r in rows if r['nucid'] in U238_HEAD]
    return rows


def merged_strong(rows, res, lo, hi):
    """Сильные табличные линии, слитые в пределах MERGE_FWHM — знаменатель recall."""
    strong = sorted([r['energy'] for r in rows if r['i_chain'] >= I_STRONG])
    out = []
    for e in strong:
        if out and abs(e - out[-1]) <= MERGE_FWHM * fwhm_kev(res, e):
            continue
        out.append(e)
    return [e for e in out if lo <= e <= hi]


def load_sets():
    """Линии сетов и обманок из sets_manifest.json (его пишет gate_study --run)."""
    path = os.path.join(HERE, 'sets_manifest.json')
    if not os.path.exists(path):
        sys.exit('нет sets_manifest.json — сначала gate_study.py --run')
    real, decoy = {}, {}
    for m in json.load(open(path, encoding='utf-8')):
        key = (m['det'], m['chain'])
        lines = [(l['e'], l['i']) for l in m['lines']]
        if m['kind'] == 'decoy':
            decoy[key] = [(l['e'], l['i']) for l in m['lines'] if l.get('decoy')]
            decoy.setdefault(key + ('all',), lines)
        else:
            real[key] = lines
    return real, decoy


def curve(values, label, total):
    """Доля прошедших порог — по сетке порогов."""
    out = []
    for t in (2.0, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0):
        out.append(sum(1 for v in values if v >= t))
    return out


def roc(pairs):
    """pairs = [(значение, 'real'|'decoy')] -> функция recall -> доля фантомов."""
    pairs = sorted(pairs, key=lambda t: -t[0])
    nr = sum(1 for _, k in pairs if k == 'real')
    nd = len(pairs) - nr
    pts, r, d = [(0.0, 0.0)], 0, 0
    for _v, k in pairs:
        if k == 'real':
            r += 1
        else:
            d += 1
        pts.append((r / max(nr, 1), d / max(nd, 1)))
    return pts


def at_recall(pts, target):
    best = None
    for rec, dec in pts:
        if rec >= target and (best is None or dec < best):
            best = dec
            break
    return best


def main():
    per_det = '--per-det' in sys.argv
    manifest = read_csv(os.path.join(CORPUS, 'manifest.csv'))
    dets = {d['det']: d for d in read_csv(os.path.join(CORPUS, 'detectors.csv'))}
    _real_sets, decoy_sets = load_sets()

    CRIT = ('z', 'mad', 'lmad', 'q16', 'iqr')
    pairs = {c: [] for c in CRIT}
    per = {}

    for row in manifest:
        key, det = row['key'], row['det']
        chains = [c for c in re.split(r'[;|]', row.get('chains') or '') if c]
        d = dets.get(det)
        if not chains or not d:
            continue
        res = (float(d['res_c0']), float(d['res_c1']), float(d['res_c2']))
        lo, hi = float(d['e_lo']), float(d['e_hi'])
        try:
            counts, energy = load_spectrum(key)
        except Exception:
            continue
        for chain in chains:
            if chain not in CHAIN_MODE:
                continue
            name, mode = CHAIN_MODE[chain]
            strong = merged_strong(chain_rows(name, mode, lo, hi), res, lo, hi)
            dec = [e for e, _ in decoy_sets.get((det, set_chain(chain)), [])]
            if not strong or not dec:
                continue
            for kind, items in (('real', strong), ('decoy', dec)):
                for e0 in items:
                    if not (lo <= e0 <= hi):
                        continue
                    w = fwhm_kev(res, e0)
                    r = net_area(counts, energy, e0, w)
                    if r is None:
                        continue
                    a, sp = r
                    sc = spurious_scales(counts, energy, e0, w)
                    if sc is None:
                        continue
                    v = {'z': a / max(sp, 1e-9)}
                    for c in ('mad', 'lmad', 'q16', 'iqr'):
                        v[c] = a / max(float(np.sqrt(sp * sp + sc[c] ** 2)), 1e-9)
                    for c in CRIT:
                        pairs[c].append((v[c], kind))
                        per.setdefault(det, {q: [] for q in CRIT})[c].append((v[c], kind))

    nr = sum(1 for _v, k in pairs['z'] if k == 'real')
    nd = len(pairs['z']) - nr
    print('линий: настоящих %d, обманок %d' % (nr, nd))
    print()
    TARGETS = (0.25, 0.30, 0.40, 0.50, 0.60, 0.65)
    print('доля принятых обманок при ОДИНАКОВОМ recall')
    print('%-6s' % 'крит.' + ''.join('%9s' % ('%.0f%%' % (t * 100)) for t in TARGETS))
    print('-' * (6 + 9 * len(TARGETS)))
    for c in CRIT:
        pts = roc(pairs[c])
        cells = []
        for t in TARGETS:
            v = at_recall(pts, t)
            cells.append('%8.1f%%' % (100.0 * v) if v is not None else '       -')
        print('%-6s' % c + ''.join(cells))

    if per_det:
        print()
        print('на recall 50%%: обманки z / lmad')
        for det in sorted(per):
            a = at_recall(roc(per[det]['z']), 0.5)
            b = at_recall(roc(per[det]['lmad']), 0.5)
            n = sum(1 for _v, k in per[det]['z'] if k == 'real')
            if a is None or b is None:
                continue
            print('%-12s n=%-4d %6.1f%%  %6.1f%%' % (det, n, 100.0 * a, 100.0 * b))


main()
