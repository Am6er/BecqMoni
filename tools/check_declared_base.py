#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож объявленной базы корпуса (`T248`): каталог, НАЗВАННЫЙ в объявлении,
обязан давать ОБЪЯВЛЕННЫЕ числа.

Зачем. 06.09.2026 объявление говорило «малая `out_mini`», а каталог
`tools/pie/out_mini` содержал прогон 04.09.2026 — предыдущую, СНЯТУЮ базу.
Пересчёт по нему давал 556.0 / 5.91 / 100 % и 205.3 / 4.95 / 90 % против
объявленных 556.1 / 5.93 / 98 % и 205.9 / 4.95 / 93 %. Отказа не бывало
никакого: числа правдоподобны, разница в десятых, подмену поколения видно
только по дате файлов. По форме это ровно `B20` — признак был, потребителя у
него не было.

Разовая уборка каталога тут не лечит: завтра базу объявят из очередного
`out_pNN`, и всё повторится. Поэтому здесь читатель, а не уборка.

Чем судит. ДВА НЕЗАВИСИМЫХ источника, и в этом весь смысл:

  объявленные числа  <-  таблица «ДЕЙСТВУЮЩАЯ БАЗА» в tools/CORPUS/README.md
  измеренные числа   <-  tools/pie/score.py по CSV-файлам самого каталога

Своей копии чисел сторож НЕ ХРАНИТ. Третье место с числами — то же протухание,
что и всякий перечень, живущий отдельно от источника (`T127`: 14 номеров из 24
разошлись за считанные дни). Таблица в шапке `TODO.md` — дословная копия той же
таблицы, и объявление само говорит «числа живут в одном месте»: README.

Как читается объявление. В графе «база» имя каталога стоит в обратных кавычках
(«малая `out_mini`»); каталог ищется как tools/pie/<имя>. Строка, у которой
каталог НЕ НАЗВАН, проверке не подлежит — она печатается предупреждением, и это
само по себе находка: такое объявление нечем сверить.

Род базы задаёт список спектров, и берётся он тоже из объявления:
  «малая»  -> score.py --only=tools/CORPUS/corpus/mini.csv (тот же список, что
              задаёт прогон в run_mini.ps1: двум спискам разойтись нечем);
  «полная» -> без --only, судится весь манифест своей части.
Часть корпуса: «понятная» -> --part=known, «непонятная» -> --part=unknown.
Режим (`spline`/`snip`) определяется по именам файлов В САМОМ каталоге, а не
задаётся здесь: иначе сторож судил бы не тот прогон, что лежит.

Запуск:

  python tools/check_declared_base.py                 приговор по объявлению
  python tools/check_declared_base.py --base=мал      только малая база
  python tools/check_declared_base.py --base=мал --dir=tools/pie/out_p23_mini1
                                                      подсунуть свой каталог

Ключ `--dir` существует ради ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ, и он же показывает, что
сверка не циркулярна: объявленные числа при подмене каталога НЕ МЕНЯЮТСЯ (они
из README), а измеренные меняются, и два каталога дают ПРОТИВОПОЛОЖНЫЕ
приговоры. Сравнивай сторож величину саму с собой — оба плеча были бы зелены.

Коды возврата:
  0 — каждая проверяемая строка объявления сошлась со своим каталогом;
  1 — хоть одно расхождение (названо поимённо: величина, объявлено, измерено);
  2 — сторожу нечем судить (нет объявления, нет каталога, нет score.py,
      неизвестный род базы, score.py отказал).

Печать держится в пределах cp1251: консоль здесь cp1251, и знак вне неё
превращается в «?» — код возврата этого не ловит вовсе.
"""

import argparse
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DECL = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
SCORE = os.path.join(ROOT, 'tools', 'pie', 'score.py')
RUNS = os.path.join(ROOT, 'tools', 'pie')
MINI = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'mini.csv')

SECTION = u'ДЕЙСТВУЮЩАЯ БАЗА'

#: графа «часть» объявления -> ключ `--part` у score.py.
PARTS = {u'понятная': 'known', u'непонятная': 'unknown'}

#: подстрока графы «база» -> нужен ли `--only` и какой.
#: Список малой базы берётся из ТОГО ЖЕ mini.csv, что задаёт её прогон.
KINDS = ((u'мал', MINI), (u'полн', None))

#: разбор итоговых строк score.py. Числа печатаются точкой и без группировки.
RE_TOTAL = re.compile(u'^итого\\s+(\\d+)\\s+(\\d+)%\\s+(\\d+)\\s+(\\d+)')
RE_CHI2 = re.compile(u'sum chi2/ndf\\s+([0-9.]+)\\s+медиана\\s+([0-9.]+)')


def _console():
    """Не глушить печать на консоли, которая не всё умеет: приговор важнее вида.

    Кодировку НЕ подменяем нарочно. Подмена на utf-8 сделала бы проверку
    читаемости под cp1251 бессмысленной: знаков «?» не появилось бы никогда,
    а в cp1251-консоли текст стал бы нечитаемым другим способом.
    """
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors='replace')
        except (AttributeError, ValueError):
            pass


def die(msg):
    print(u'ОТКАЗ СТОРОЖА: %s' % msg)
    sys.exit(2)


def read_section(path):
    """Строки раздела объявления: от его заголовка до следующего `## `."""
    if not os.path.isfile(path):
        die(u'нет файла объявления: %s' % path)
    with open(path, encoding='utf-8') as fh:
        lines = fh.read().splitlines()
    start = None
    for i, line in enumerate(lines):
        if line.startswith('## ') and SECTION in line:
            start = i
            break
    if start is None:
        die(u'в %s нет раздела «%s»' % (path, SECTION))
    end = len(lines)
    for i in range(start + 1, len(lines)):
        if lines[i].startswith('## '):
            end = i
            break
    return lines[start:end]


def cell_number(text):
    """Число из графы таблицы: `**556.1**`, `98 %`, `—` (не объявлено)."""
    clean = text.replace('*', '').replace('%', '').replace(u'\u00a0', ' ').strip()
    if not clean or clean in (u'—', u'-', u'?'):
        return None
    try:
        return float(clean)
    except ValueError:
        return None


def parse_table(lines):
    """Строки таблицы объявления как словари. Своих чисел сторож не заводит."""
    rows = []
    for line in lines:
        if not line.startswith('|'):
            continue
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        if len(cells) < 8:
            continue
        if set(cells[0].replace('*', '').strip()) <= set('-: '):
            continue                              # разделитель шапки
        part = cells[1].replace('*', '').strip()
        if part not in PARTS:
            continue                              # шапка таблицы
        rows.append({
            'base': cells[0],
            'part': part,
            'spectra': cell_number(cells[2]),
            'chi2': cell_number(cells[3]),
            'median': cell_number(cells[4]),
            'recall': cell_number(cells[5]),
            'phantoms': cell_number(cells[6]),
            'suppressed': cell_number(cells[7]),
        })
    return rows


def base_dir(base_cell):
    """Каталог, НАЗВАННЫЙ в графе «база» (обратные кавычки), либо None.

    Годится и голое имя (`out_mini` -> tools/pie/out_mini), и путь от корня
    дерева (`tools/pie/out_p23_mini1`): объявление вольно назвать каталог и
    так, и так, а расходиться с ним сторож не имеет права.
    """
    for found in re.findall('`([^`]+)`', base_cell):
        name = found.strip().strip('/\\')
        if not name:
            continue
        if '/' in name or '\\' in name:
            return os.path.join(ROOT, *re.split(r'[\\/]+', name))
        return os.path.join(RUNS, name)
    return None


def base_kind(base_cell):
    plain = base_cell.replace('*', '').lower()
    for mark, only in KINDS:
        if mark in plain:
            return mark, only
    return None, None


def detect_mode(path):
    """Режим разбора — по тому, ЧТО ЛЕЖИТ в каталоге, а не по умолчанию здесь."""
    modes = set()
    for name in os.listdir(path):
        hit = re.match('^.+_(spline|snip)_components\\.csv$', name)
        if hit:
            modes.add(hit.group(1))
    if not modes:
        return None, u'в каталоге нет ни одного `*_<режим>_components.csv`'
    if len(modes) > 1:
        return None, (u'в каталоге смешаны режимы: %s' % u', '.join(sorted(modes)))
    return sorted(modes)[0], None


def run_score(path, mode, part, only):
    argv = [sys.executable, SCORE, '--mode=' + mode, '--out-dir=' + path,
            '--part=' + part, '--members']
    if only:
        argv.append('--only=' + only)
    env = dict(os.environ)
    env['PYTHONIOENCODING'] = 'utf-8'
    env['PYTHONUTF8'] = '1'
    proc = subprocess.run(argv, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          env=env)
    text = proc.stdout.decode('utf-8', 'replace')
    if proc.returncode != 0:
        die(u'score.py отказал (код %d) на каталоге %s:\n%s'
            % (proc.returncode, path, text[-2000:]))
    got = {}
    for line in text.splitlines():
        hit = RE_TOTAL.match(line.strip())
        if hit:
            got['spectra'] = float(hit.group(1))
            got['recall'] = float(hit.group(2))
            got['phantoms'] = float(hit.group(3))
            got['suppressed'] = float(hit.group(4))
        hit = RE_CHI2.search(line)
        if hit:
            got['chi2'] = float(hit.group(1))
            got['median'] = float(hit.group(2))
    missing = [k for k in ('spectra', 'recall', 'chi2', 'median') if k not in got]
    if missing:
        die(u'в выводе score.py по %s нет величин: %s'
            % (path, u', '.join(missing)))
    return got, ' '.join(argv[1:])


#: величина -> (подпись, знаков после точки). Объявление печатает столько же.
FIELDS = (
    ('spectra', u'спектров', 0),
    ('chi2', u'sum chi2/ndf', 1),
    ('median', u'медиана', 2),
    ('recall', u'recall, %', 0),
    ('phantoms', u'фантомов', 0),
    ('suppressed', u'подавлен', 0),
)


def compare(row, got):
    """Расхождения строки. Сверяется с точностью, с какой объявлено."""
    bad = []
    for key, title, digits in FIELDS:
        want = row[key]
        if want is None:
            continue                              # клетка не заполнена
        have = got[key]
        if round(want, digits) != round(have, digits):
            bad.append((title, want, have, digits))
    return bad


def fmt(value, digits):
    return ('%.' + str(digits) + 'f') % value     # точка, без группировки


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--decl', default=DECL,
                    help=u'файл объявления (по умолчанию tools/CORPUS/README.md)')
    ap.add_argument('--base', default=None,
                    help=u'судить только базу, чья графа содержит эту подстроку')
    ap.add_argument('--dir', default=None,
                    help=u'каталог вместо названного в объявлении '
                         u'(положительный контроль; нужен --base)')
    args = ap.parse_args()

    if not os.path.isfile(SCORE):
        die(u'нет счётчика: %s' % SCORE)

    rows = parse_table(read_section(args.decl))
    if not rows:
        die(u'в разделе «%s» файла %s не разобрано ни одной строки таблицы'
            % (SECTION, args.decl))

    if args.base:
        rows = [r for r in rows if args.base.lower() in r['base'].replace('*', '').lower()]
        if not rows:
            die(u'в объявлении нет базы с подстрокой «%s»' % args.base)
    if args.dir:
        if not args.base:
            die(u'--dir без --base: непонятно, какой базе подменять каталог')
        kinds = set(base_kind(r['base'])[0] for r in rows)
        if len(kinds) != 1:
            die(u'--base=«%s» выбрал %d разных баз: подмена каталога двусмысленна'
                % (args.base, len(kinds)))

    print(u'ОБЪЯВЛЕНИЕ: %s, раздел «%s»'
          % (os.path.relpath(args.decl, ROOT), SECTION))
    print(u'ИЗМЕРЕНИЕ:  tools/pie/score.py по файлам самого каталога')
    print(u'строк объявления взято: %d' % len(rows))
    print()

    checked = failed = skipped = 0
    verdicts = []
    for row in rows:
        title = u'%s / %s' % (row['base'].replace('*', '').strip(), row['part'])
        mark, only = base_kind(row['base'])
        if mark is None:
            die(u'неизвестен род базы в графе «%s»: ждались «малая» или «полная»'
                % row['base'])
        named = base_dir(row['base'])
        if args.dir:
            path = args.dir if os.path.isabs(args.dir) else os.path.join(ROOT, args.dir)
            source = u'подан ключом --dir'
        elif named:
            path = named
            source = u'назван объявлением'
        else:
            skipped += 1
            print(u'[ -- ] %s' % title)
            print(u'      ВНИМАНИЕ: каталог в объявлении НЕ НАЗВАН - сверить нечем.')
            print(u'      Объявление, не называющее каталога, проверке не подлежит:')
            print(u'      имя каталога ставится в графу «база» обратными кавычками.')
            print()
            continue

        if not os.path.isdir(path):
            die(u'%s: каталог %s (%s) не найден'
                % (title, os.path.relpath(path, ROOT), source))
        mode, err = detect_mode(path)
        if mode is None:
            die(u'%s: %s (%s)' % (title, err, os.path.relpath(path, ROOT)))

        got, cmd = run_score(path, mode, PARTS[row['part']], only)
        bad = compare(row, got)
        checked += 1
        if bad:
            failed += 1
            print(u'[ОТКАЗ] %s' % title)
        else:
            print(u'[ ОК ] %s' % title)
        print(u'      каталог: %s (%s)' % (os.path.relpath(path, ROOT), source))
        print(u'      счёт:    python %s' % cmd)
        if bad:
            print(u'      РАСХОЖДЕНИЯ (%d):' % len(bad))
            for title2, want, have, digits in bad:
                print(u'        %-14s объявлено %s, измерено %s'
                      % (title2, fmt(want, digits), fmt(have, digits)))
            verdicts.append((title, bad, os.path.relpath(path, ROOT)))
        else:
            shown = u', '.join(
                u'%s %s' % (t, fmt(got[k], d)) for k, t, d in FIELDS
                if row[k] is not None)
            print(u'      сошлось: %s' % shown)
        print()

    print(u'ИТОГО: строк сверено %d, расхождений %d, без каталога %d'
          % (checked, failed, skipped))
    if failed:
        print()
        print(u'ОСТАНОВ: каталог из объявления даёт НЕ ОБЪЯВЛЕННЫЕ числа.')
        print(u'Это не описка в таблице, а подмена ПОКОЛЕНИЯ прогона: числа')
        print(u'правдоподобны, разница в десятых, и глазом её не видно.')
        print(u'Чинится одним из двух движений, и оба - за распорядителем:')
        print(u'  1) переснять базу В названный каталог')
        print(u'     (& tools\\CORPUS\\scripts\\run_mini.ps1 -Out <корень>\\tools\\pie\\out_mini);')
        print(u'  2) назвать в объявлении тот каталог, где объявленные числа лежат.')
        for title, bad, path in verdicts:
            print(u'  %s -> %s: %s' % (title, path,
                                       u'; '.join(t for t, _w, _h, _d in bad)))
        return 1
    if checked == 0:
        print()
        print(u'ВНИМАНИЕ: сверено НОЛЬ строк - объявление не называет ни одного')
        print(u'каталога. Сторож при этом молчит, и молчание тут ничего не значит.')
    return 0


if __name__ == '__main__':
    _console()
    sys.exit(main())
