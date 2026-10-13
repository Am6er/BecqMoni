# -*- coding: utf-8 -*-
r"""П141 (23.09.2026) — СВОДКА ПРИЁМКИ: измеренное против ОБЪЯВЛЕННОГО у базы rev32.

Читает сводки `score.py` полосы и логи прогонов и печатает одну таблицу: что
измерено, что объявлено, разность и вердикт. Ничего не правит и никуда не пишет,
кроме своего вывода.

    python cmp_declared.py [--art=D:\BqMoni_Claude\p141\art]

⛔ ЧТО СРАВНИМО, А ЧТО НЕТ (измерено по коду `master` 23.09.2026):

  * `Σχ²` и `медиана` — графа `chi2ndf` = МЕТРИКА РЕШАТЕЛЯ (веса по модели,
    Пирсон, `A310`). Правки 22.09.2026 её линейку НЕ меняли — сравнимо
    с объявленным напрямую;
  * `recall`, `фантомов`, `подавлен`, `найдена`/`применена` — сравнимо;
  * ⛔ `model residual` (ε) и графа `chi2ndf_pois` — НЕСРАВНИМЫ: `S180` (П134) и
    `S182` (П136) сменили им веса с неймановских `1/max(N,1)` на пирсоновские
    `1/(μ̂+ξ²C²)` и сняли полку. Это смена ЛИНЕЙКИ у двух печатных чисел, а не
    улучшение разбора («приговоры разбора ключом не двигаются» —
    `FsaAnalyzer.ReportModelWeights`). Ждать падения ε и `chi2ndf_pois`.

Разделитель дробной части — точка.
"""
import io
import os
import re
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

# Объявленное базой rev32 (21.09.2026, день): README корпуса, шапка TODO.md,
# памятка `corpus-base-current.md` — три дословные копии одной таблицы.
# `err` — ОЖИДАЕМОЕ число отказов разбора: у непонятной части это гейт геометрии
# `A277` («нет геометрии — нет FSA», цена принята Amber 10.09.2026), то есть
# «спектров части минус разобранные», а НЕ поломка. У понятной части ждём ноль.
DECLARED = {
    ('full', 'known'):   {'n': 92, 'chi2': 475.9, 'med': 2.37, 'recall': 100, 'ph': 0, 'sup': 0, 'mx': 92, 'err': 0},
    ('full', 'unknown'): {'n': 36, 'chi2': 48.1, 'med': 24.03, 'recall': 4, 'ph': 0, 'sup': 0, 'mx': 0, 'err': 34},
    ('mini', 'known'):   {'n': 45, 'chi2': 322.7, 'med': 3.63, 'recall': 100, 'ph': 0, 'sup': 0, 'mx': 45, 'err': 0},
    ('mini', 'unknown'): {'n': 14, 'chi2': 20.1, 'med': 20.13, 'recall': 4, 'ph': 0, 'sup': 0, 'mx': 0, 'err': 13},
}

RE_TOTAL = re.compile(r'^итого\s+(\d+)\s+(\d+)%\s+(\d+)\s+(\d+)')
RE_SUM = re.compile(r'^\s+(\d+)\s+sum chi2/ndf\s+([\d.]+)\s+медиана\s+([\d.]+)')
RE_RES = re.compile(r'^\s+(\d+)\s+model residual медиана\s+([\d.]+)')
RE_PART = re.compile(r'^(known|unknown)\s+(\d+)\s+(\d+)\s+(\d+)\s+([\d.]+)\s+([\d.]+)\s+(\d+)\s+(\d+)\s+(\d+)')


def read_score(path):
    """Числа из сводки `score.py`: спектров, recall, фантомов, подавлен, Σχ², медиана, ε."""
    got = {}
    if not os.path.isfile(path):
        return None
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = RE_TOTAL.match(line)
        if m:
            got['n'] = int(m.group(1))
            got['recall'] = int(m.group(2))
            got['ph'] = int(m.group(3))
            got['sup'] = int(m.group(4))
            continue
        m = RE_SUM.match(line)
        if m:
            got['n_chi'] = int(m.group(1))
            got['chi2'] = float(m.group(2))
            got['med'] = float(m.group(3))
            continue
        m = RE_RES.match(line)
        if m:
            got['res'] = float(m.group(2))
    return got or None


def read_run(path):
    """Графы `найдена`/`применена` из лога прогона (итог по частям корпуса)."""
    got = {}
    if not os.path.isfile(path):
        return got
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = RE_PART.match(line)
        if m:
            got[m.group(1)] = {
                'n': int(m.group(2)), 'found': int(m.group(3)), 'applied': int(m.group(4)),
                'chi2': float(m.group(5)), 'med': float(m.group(6)), 'err': int(m.group(7)),
            }
    return got


def fmt(v, digits=2):
    return ('%.' + str(digits) + 'f') % v if v is not None else '—'


def main(argv):
    art = r'D:\BqMoni_Claude\p141\art'
    for a in argv:
        if a.startswith('--art='):
            art = a[len('--art='):]

    print('СВОДКА ПРИЁМКИ П141 — измеренное на `master` против ОБЪЯВЛЕННОГО базой rev32 (21.09.2026, день)')
    print('склад матриц ТОТ ЖЕ (живой, физика 22, формат 9), корпус ТОТ ЖЕ; сменился только РАЗБОР.')
    print('')
    head = ('%-6s %-8s %6s %10s %10s %9s %7s %7s %6s %5s %5s %-14s %-11s'
            % ('база', 'часть', 'спектр', 'Σχ² изм.', 'Σχ² объяв', 'Δ %', 'мед.изм', 'мед.об', 'recall', 'фант', 'подв', 'матрица н/п', 'отказ и/о'))
    print(head)
    print('-' * len(head))
    verdicts = []
    missing = 0
    for base in ('full', 'mini'):
        runlog = os.path.join(art, 'run_%s.log' % base)
        runs = read_run(runlog)
        for part in ('known', 'unknown'):
            got = read_score(os.path.join(art, 'score_%s_%s.txt' % (base, part)))
            dec = DECLARED[(base, part)]
            if got is None:
                print('%-6s %-8s   СВОДКИ НЕТ: %s' % (base, part, os.path.join(art, 'score_%s_%s.txt' % (base, part))))
                verdicts.append('%s/%s — сводки нет' % (base, part))
                missing += 1
                continue
            chi2 = got.get('chi2')
            d = (chi2 - dec['chi2']) / dec['chi2'] * 100.0 if chi2 is not None else None
            r = runs.get(part, {})
            mx = '%d / %d' % (r.get('found', -1), r.get('applied', -1)) if r else '—'
            err = ('%d / %d' % (r.get('err', -1), dec['err'])) if r else '—'
            print('%-6s %-8s %6d %10s %10s %9s %7s %7s %5d%% %5d %5d %-14s %-11s'
                  % (base, part, got.get('n', -1), fmt(chi2, 1), fmt(dec['chi2'], 1), fmt(d, 3),
                     fmt(got.get('med')), fmt(dec['med']), got.get('recall', -1),
                     got.get('ph', -1), got.get('sup', -1), mx, err))
            # вердикт по существу
            bad = []
            if got.get('n') != dec['n']:
                bad.append('спектров %d вместо %d — СОСТАВ ЧАСТИ ИЗМЕНИЛСЯ, числа несравнимы' % (got.get('n', -1), dec['n']))
            if got.get('recall') != dec['recall']:
                bad.append('recall %d %% вместо %d %%' % (got.get('recall', -1), dec['recall']))
            if got.get('ph') != dec['ph']:
                bad.append('фантомов %d вместо %d' % (got.get('ph', -1), dec['ph']))
            if got.get('sup') != dec['sup']:
                bad.append('подавлен %d вместо %d' % (got.get('sup', -1), dec['sup']))
            if r and part == 'known' and (r.get('found') != dec['mx'] or r.get('applied') != dec['mx']):
                bad.append('матрица %d найдена / %d применена вместо %d / %d' % (r.get('found', -1), r.get('applied', -1), dec['mx'], dec['mx']))
            if r and r.get('err', 0) != dec['err']:
                bad.append('отказов разбора %d, ждём %d (у непонятной части это гейт геометрии `A277`, у понятной — ноль)'
                           % (r.get('err', -1), dec['err']))
            if d is not None and abs(d) >= 1.0:
                bad.append('Σχ² сдвинулся на %.2f %% — разбирать поимённо лестницей' % d)
            verdicts.append(('%s/%s: ' % (base, part)) + ('; '.join(bad) if bad else 'сошлось с объявленным'))
    print('')
    print('⛔ НЕСРАВНИМОЕ (сменилась ЛИНЕЙКА, `S180`+`S182`): ε model residual и графа `chi2ndf_pois`')
    for base in ('full', 'mini'):
        for part in ('known', 'unknown'):
            got = read_score(os.path.join(art, 'score_%s_%s.txt' % (base, part)))
            if got and 'res' in got:
                print('   %s/%s: ε медиана %s %% (у rev32 печаталась неймановскими весами — не сверять)'
                      % (base, part, fmt(got['res'], 1)))
    print('')
    print('ВЕРДИКТЫ ПО СТРОКАМ:')
    for v in verdicts:
        print('   ' + v)
    print('')
    print('⛔ Это РЕПЕТИЦИЯ ЦЕНЫ правок разбора, а не переобъявление базы: `out_rev32_*` не тронуты,')
    print('   объявление в трёх местах не менялось, витрина не переобъявлялась. Переобъявлять — только по слову Amber.')
    if missing:
        print('')
        print('⛔ ОТКАЗ СВОДКИ: не найдено сводок score.py — %d из 4' % missing)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
