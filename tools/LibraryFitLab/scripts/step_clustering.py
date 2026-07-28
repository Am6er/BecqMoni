# -*- coding: utf-8 -*-
"""Скучиваются ли выжившие фантомы у комптоновской ступеньки сильных линий.

## Зачем

Verter73 в PR #32 заметил, что h_step у сцинтиллятора вдесятеро больше, чем у
германия (0.03 против 0.003), и предложил внести его в бюджет неопределённости.
В знаменатель значимости он не годится — все компоненты Type-B пропорциональны
амплитуде и при нулевом сигнале обращаются в ноль, то есть в порог принятия
решения по построению не входят. Но само наблюдение ценное, и место у него
другое: это про ФОРМУ.

В нашей модели комптоновской ступеньки нет. `PeakShapeModel` умеет
асимметричные экспоненциальные хвосты (`ExpGaussExpLeftTail` / `RightTail`), а
ступеньки под пиком не знает; `BuildFixedBackground` складывает огибающую SNIP
и фон прибора, и это всё. SNIP с окном в несколько полуширин срезает ступеньку
поперёк, остаточная структура остаётся под пиком и СЛЕВА от него, и
библиотечной линии, туда попавшей, фит присуждает положительную площадь —
просто потому, что в модели нет компоненты, которая забрала бы её себе.

## Что проверяется

Если это так, то принятые линии сетов-обманок должны скучиваться с
НИЗКОЭНЕРГЕТИЧЕСКОЙ стороны от сильных настоящих линий. Нулевая гипотеза:
распределены равномерно, и тогда ступенька к нашим фантомам отношения не имеет,
а идея закрывается за один прогон — как закрылось ограничение кривой.

Мера — знаковое расстояние от принятой линии обманки до ближайшей сильной линии
настоящей цепочки, в полуширинах. Отрицательное значит «слева», то есть на
ступеньке. Сравниваются: доля слева против доли справа, и распределение по
модулю расстояния.

Контроль обязателен: сами позиции обманок расставлены сдвигом на 2-4 FWHM от
настоящих линий, поэтому у ПРЕДЪЯВЛЕННЫХ линий уже есть своя геометрия. Считаем
асимметрию отдельно у предъявленных и у принятых; интересна разница, а не
асимметрия принятых сама по себе.

Запуск:  python step_clustering.py [--per-det]
"""
import os
import sys
import csv
import re
import json
import glob

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(os.path.dirname(HERE), 'corpus')
OUT = os.path.join(HERE, 'out_gate')
sys.path.insert(0, HERE)

from chains import chain_lines, CHAINS                  # noqa: E402

I_STRONG = 5.0        # «сильная» линия цепочки: от этой интенсивности, %
MAX_DIST_FWHM = 8.0   # дальше ближайшая линия уже не при чём

CHAIN_MODE = {'Th-232': ('Th-232', 'pos'), 'Th-228': ('Th-228', 'pos'),
              'Ra-226': ('Ra-226', 'pos'),
              'U-238': ('U-238', 'pos'), 'U-238u': ('U-238', 'head'),
              'U-235': ('U-235', 'pos')}
U238_HEAD = {'238U', '234TH', '234PAm1', '234PA', '234U', '230TH'}


def read_csv(path, encoding='utf-8-sig'):
    with open(path, encoding=encoding, newline='') as fh:
        return list(csv.DictReader(fh))


def fwhm_kev(res, e):
    return float(np.sqrt(max(res[0] + res[1] * e + res[2] * e * e, 1e-6)))


def strong_lines(chain, lo, hi):
    name, mode = CHAIN_MODE[chain]
    rows = [r for r in chain_lines(CHAINS[name]) if lo <= r['energy'] <= hi]
    if mode == 'head':
        rows = [r for r in rows if r['nucid'] in U238_HEAD]
    return sorted(r['energy'] for r in rows if r['i_chain'] >= I_STRONG)


def signed_distance(e, anchors, w):
    """Ближайшая сильная линия: (расстояние в FWHM со знаком) или None.

    Знак минус — линия ЛЕВЕЕ сильной, то есть на её комптоновской ступеньке.
    """
    if not anchors:
        return None
    a = np.asarray(anchors)
    i = int(np.argmin(np.abs(a - e)))
    d = (e - a[i]) / w
    return d if abs(d) <= MAX_DIST_FWHM else None


def load_accepted():
    """Принятые библиотечные линии обманок из CSV последнего прогона gate_study.

    Группа детектора берётся из имени файла (`<DET>_<метка>_peaks.csv`) —
    колонки det в CSV нет. Позиция берётся из `nuclide_energy`, табличной
    энергии линии сета: она точно совпадает с манифестом, а `energy` — это
    подогнанный центроид, и он смещён.
    """
    # Группа определяется по СПИСКУ из detectors.csv, длиннейшим совпадающим
    # префиксом. split('_')[0] терял девять групп из двадцати трёх — всю
    # лестницу ASN8, оба варианта HPGe, CZT_TECD, LABR_BRIL, — и вывод об
    # асимметрии комптоновской ступеньки был получен на неполных данных.
    known = sorted((r['det'] for r in read_csv(os.path.join(CORPUS, 'detectors.csv'))),
                   key=len, reverse=True)
    out = {}
    for path in glob.glob(os.path.join(OUT, '*_peaks.csv')):
        base = os.path.basename(path)
        det = next((d for d in known if base.startswith(d + '_')), None)
        if det is None:
            continue
        for row in read_csv(path):
            name = row.get('set') or ''
            if '~decoy' not in name:
                continue
            if (row.get('origin') or '') != 'Library':
                continue                    # пик финдера, а не линия сета
            try:
                e = float(row.get('nuclide_energy') or row.get('energy'))
            except (TypeError, ValueError):
                continue
            out.setdefault((det, row.get('spectrum') or '', name), []).append((e, 'Library'))
    return out


def main():
    per_det = '--per-det' in sys.argv
    dets = {d['det']: d for d in read_csv(os.path.join(CORPUS, 'detectors.csv'))}
    sets = {}
    for m in json.load(open(os.path.join(HERE, 'sets_manifest.json'), encoding='utf-8')):
        if m['kind'] == 'decoy':
            sets[(m['det'], m['set_name'])] = m

    accepted = load_accepted()
    if not accepted:
        sys.exit('нет out_gate/*_peaks.csv — сначала gate_study.py --run')

    stat = {'предъявлено': [], 'принято': []}
    per = {}

    for (det, _spec, set_name), lines in accepted.items():
        m = sets.get((det, set_name))
        d = dets.get(det)
        if m is None or d is None:
            continue
        res = (float(d['res_c0']), float(d['res_c1']), float(d['res_c2']))
        lo, hi = float(d['e_lo']), float(d['e_hi'])
        anchors = strong_lines(m['chain'], lo, hi)
        if not anchors:
            continue

        shown = [float(l['e']) for l in m['lines'] if l.get('decoy')]
        for e in shown:
            dd = signed_distance(e, anchors, fwhm_kev(res, e))
            if dd is not None:
                stat['предъявлено'].append(dd)
                per.setdefault(det, {'предъявлено': [], 'принято': []})['предъявлено'].append(dd)

        taken = set()
        for e, _origin in lines:
            w = fwhm_kev(res, e)
            # линия из CSV — принятая; соотносим её с предъявленной обманкой
            near = [q for q in shown if abs(q - e) <= 0.05 * max(w, 1e-6) + 1e-6]
            if not near:
                near = [q for q in shown if abs(q - e) <= 0.5 * w]
            if not near:
                continue
            q = min(near, key=lambda t: abs(t - e))
            if q in taken:
                continue
            taken.add(q)
            dd = signed_distance(q, anchors, w)
            if dd is not None:
                stat['принято'].append(dd)
                per.setdefault(det, {'предъявлено': [], 'принято': []})['принято'].append(dd)

    print('линий обманок с ближайшей сильной линией в пределах %.0f FWHM:' % MAX_DIST_FWHM)
    print('  предъявлено %d, принято %d' % (len(stat['предъявлено']), len(stat['принято'])))
    if not stat['принято']:
        sys.exit('принятых не нашлось — проверить сопоставление CSV и манифеста')
    print()
    print('%-14s %7s %8s %8s %9s %9s' % ('', 'N', 'слева', 'справа', 'медиана', '|медиана|'))
    for k in ('предъявлено', 'принято'):
        a = np.asarray(stat[k])
        left = float((a < 0).mean())
        print('%-14s %7d %7.1f%% %7.1f%% %9.2f %9.2f'
              % (k, len(a), 100.0 * left, 100.0 * (1.0 - left),
                 float(np.median(a)), float(np.median(np.abs(a)))))

    a_shown = np.asarray(stat['предъявлено'])
    a_taken = np.asarray(stat['принято'])
    left_shown = float((a_shown < 0).mean())
    left_taken = float((a_taken < 0).mean())
    print()
    print('асимметрия принятых сверх предъявленных: %+.1f п.п.'
          % (100.0 * (left_taken - left_shown)))

    # доля принятых по полосам расстояния: ступенька живёт в 1-3 FWHM слева
    print()
    print('доля ПРИНЯТЫХ среди предъявленных, по полосам расстояния (FWHM):')
    edges = [-8, -5, -3, -2, -1, 0, 1, 2, 3, 5, 8]
    print('%-14s %8s %8s %8s' % ('полоса', 'предъяв', 'принято', 'доля'))
    for i in range(len(edges) - 1):
        lo_e, hi_e = edges[i], edges[i + 1]
        ns = int(((a_shown >= lo_e) & (a_shown < hi_e)).sum())
        nt = int(((a_taken >= lo_e) & (a_taken < hi_e)).sum())
        if ns == 0:
            continue
        print('%-14s %8d %8d %7.1f%%' % ('[%+d, %+d)' % (lo_e, hi_e), ns, nt,
                                          100.0 * nt / ns))

    if per_det:
        print()
        print('%-12s %8s %8s %10s' % ('детектор', 'слева пр', 'слева пн', 'разница'))
        for det in sorted(per):
            sh = np.asarray(per[det]['предъявлено'])
            tk = np.asarray(per[det]['принято'])
            if len(sh) < 10 or len(tk) < 5:
                continue
            ls, lt = float((sh < 0).mean()), float((tk < 0).mean())
            print('%-12s %7.1f%% %7.1f%% %9.1f п.п.'
                  % (det, 100.0 * ls, 100.0 * lt, 100.0 * (lt - ls)))


main()
