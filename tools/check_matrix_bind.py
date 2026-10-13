#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож «матрица кладётся в анализатор ТОЛЬКО через `FsaMatrixBinding.Bind`» (`T263`, П193 01.10.2026).

Зачем. `FsaMatrixBinding.Bind` кладёт в `FsaAnalyzer` вместе с матрицей всё, что
идёт с нею: вещество кристалла (суммы по свету, `S20`), таблицу Q_k угловых
корреляций сумм-пиков (`AngularQk`, `N14`/`AMBER46`) и обстановку
(`BackscatterWithMatrix` для сцен в домике, `AMBER12`). Прямое
`analyzer.ResponseMatrix = matrix` оставляет `AngularQk = null` — угловые
корреляции молча выключены, ключ `CascadeSumAngular` ничего не меняет, — и не
ставит `BackscatterWithMatrix`. Найдено П191 30.09.2026: `--set=CascadeSumAngular=false`
не менял сумм-пик Y-88 (2861.1 = 2861.1), пока проба не перешла на `Bind`; таких
проб было 20 (21 место). Приложение (`FsaAnalysisSession`) и `CorpusFsaProbe` шли
через `Bind` — числа тех проб не были числами приложения.

Что проверяется: во всех `*.cs` под `BecquerelMonitor/` и `tools/` (без `bin/`,
`obj/`, `build_*`, `packages/`) нет присваивания свойству `ResponseMatrix`
(`ResponseMatrix = …` — и оператором, и в инициализаторе объекта), кроме самого
`FsaMatrixBinding.cs`. Строчные комментарии (`//`, `///`) не судятся.

Положительный контроль — НА КАЖДОМ прогоне, не только по ключу: три подсаженные
формы (оператор, инициализатор, сокращённое имя анализатора) обязаны ловиться,
`FsaMatrixBinding.Bind(...)` и сравнение `ResponseMatrix == null` — не ловиться.
Контроль провален — код 1, что бы ни показало дерево.

Синтетической матрице без геометрии — `FsaMatrixBinding.Bind(analyzer, null, matrix)`.

Запуск:
    python tools/check_matrix_bind.py

Код возврата: 0 — прямых присваиваний нет и контроль сошёлся; 1 — есть (названы
файл:строка) или контроль провален.
"""
from __future__ import print_function
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
SCAN = ('BecquerelMonitor', 'tools')
SKIP_DIRS = ('bin', 'obj', 'packages', '.git', '.vs')
ALLOWED = {os.path.normpath('BecquerelMonitor/FullSpectrumAnalysis/FsaMatrixBinding.cs')}
PATTERN = re.compile(r'\bResponseMatrix\s*=(?![=>])')


def strip_comment(line):
    u"""Отрезать строчный комментарий (`//`); строки-описания `///` уходят целиком."""
    i = line.find('//')
    return line if i < 0 else line[:i]


def hits_in(text):
    u"""Номера строк с присваиванием `ResponseMatrix = …` вне комментария."""
    found = []
    for number, line in enumerate(text.splitlines(), 1):
        if PATTERN.search(strip_comment(line)):
            found.append((number, line.strip()))
    return found


def scan_tree():
    hits = []
    count = 0
    for top in SCAN:
        for folder, dirs, files in os.walk(os.path.join(ROOT, top)):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith('build_')
                       and not d.startswith('wd_')]
            for name in files:
                if not name.endswith('.cs'):
                    continue
                path = os.path.join(folder, name)
                rel = os.path.normpath(os.path.relpath(path, ROOT))
                count += 1
                if rel in ALLOWED:
                    continue
                with io.open(path, 'r', encoding='utf-8-sig', errors='replace') as handle:
                    for number, line in hits_in(handle.read()):
                        hits.append((rel.replace(os.sep, '/'), number, line))
    return count, hits


def control():
    u"""Положительный контроль: подсаженные формы ловятся, законные — нет."""
    caught = [
        u'            analyzer.ResponseMatrix = matrix;',
        u'            var analyzer = new FsaAnalyzer { ResponseMatrix = matrix, ScintillatorMaterial = m };',
        u'                ResponseMatrix = matrix,',
        u'                an.ResponseMatrix=loaded;',
    ]
    clean = [
        u'            FsaMatrixBinding.Bind(analyzer, geometry, matrix);',
        u'            if (analyzer.ResponseMatrix == null) return;',
        u'            // analyzer.ResponseMatrix = matrix;  — так НЕЛЬЗЯ (T263)',
        u'            /// <c>analyzer.ResponseMatrix = matrix</c> — пример в описании',
        u'            ResponseMatrix Matrix => this.matrix;',
    ]
    missed = [s for s in caught if not hits_in(s)]
    false = [s for s in clean if hits_in(s)]
    return missed, false


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    missed, false = control()
    ok_control = not missed and not false
    print(u'положительный контроль: подсаженных форм %d, поймано %d; законных %d, ложных %d — %s'
          % (4, 4 - len(missed), 5, len(false), u'СОШЛОСЬ' if ok_control else u'ПРОВАЛЕН'))
    for s in missed:
        print(u'  НЕ ПОЙМАНО: ' + s.strip())
    for s in false:
        print(u'  ЛОЖНО ПОЙМАНО: ' + s.strip())

    count, hits = scan_tree()
    if hits:
        print(u'⛔ ПРЯМОЕ `ResponseMatrix = …` МИМО `FsaMatrixBinding.Bind` (%d мест; файлов .cs просмотрено %d):'
              % (len(hits), count))
        for rel, number, line in hits:
            print(u'  %s:%d: %s' % (rel, number, line))
        print(u'исправить: FsaMatrixBinding.Bind(analyzer, геометрия-или-null, matrix) — с нею едут Q_k и обстановка (T263)')
    else:
        print(u'прямых присваиваний ResponseMatrix нет (файлов .cs просмотрено %d; разрешено одно место — FsaMatrixBinding.cs)'
              % count)
    return 0 if ok_control and not hits else 1


if __name__ == '__main__':
    sys.exit(main())
