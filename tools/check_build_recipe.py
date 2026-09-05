#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож рецепта сборки: находит КАЖДУЮ копию вызова MSBuild по
`BecquerelMonitor.csproj` и говорит, несёт ли она `/p:GenerateManifests=false`.

Зачем. Ключ НЕ лишний (`T75`): без него шаг манифеста ClickOnce перечисляет и
хеширует три `*.sqlite` как содержимое приложения, и сборка падает `MSB3171`,
стоит соседнему процессу держать одну из баз на запись или запретить доступ.
25.08.2026 волна из восьми агентов дала восемь отказов подряд. Рецепт при этом
живёт НЕ в одном месте, и копии отстают поодиночке и молча (`T81`).

⚠ Отсюда же и предел этого сторожа: он ЛЕЧИТ СЛЕДСТВИЕ. Настоящее лекарство —
один скрипт сборки, который зовут все; пока его нет, копии придётся сверять.

Что считается копией: строка с `MSBuild.exe` или с переменной `$msbuild`, у
которой в пределах восьми следующих строк упомянут `BecquerelMonitor.csproj`.
Ключ ищется в том же окне — рецепт пишут в несколько строк через продолжение.

  python tools/check_build_recipe.py [<корень или файл> ...]

Без доводов обходится `tools`. ⛔ Полное дерево (`.`) даёт и ЖУРНАЛЫ: копия
рецепта в журнале за август 2026 — это запись о том, как тогда собирали, и
правка её была бы подделкой записи. Судить журналы этим сторожем не следует.

Коды возврата:
  0 — копий без ключа нет;
  1 — есть копия без ключа (перечень поимённо: файл, строка);
  2 — нечего обходить.
"""

import io
import os
import re
import sys

RE_START = re.compile(r'MSBuild\.exe|\$msbuild\b', re.IGNORECASE)
RE_PROJ = re.compile(r'BecquerelMonitor\.csproj', re.IGNORECASE)
RE_KEY = re.compile(r'/p:GenerateManifests\s*=\s*false', re.IGNORECASE)

WINDOW = 8
EXTS = ('.md', '.ps1', '.py', '.cs', '.cmd', '.bat', '.txt', '.json')
SKIP_DIRS = ('.git', '.vs', 'bin', 'obj', 'packages', '__pycache__', 'node_modules')
DEFAULT_ROOTS = ['tools']


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def walk(roots):
    for root in roots:
        if os.path.isfile(root):
            yield root
            continue
        if not os.path.isdir(root):
            sys.stderr.write(u'ОТКАЗ: нет такого пути: %s\n' % root)
            continue
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for fn in filenames:
                if os.path.splitext(fn)[1].lower() in EXTS:
                    yield os.path.join(dirpath, fn)


def main(argv):
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    roots = argv[1:] or [os.path.join(repo, r) for r in DEFAULT_ROOTS]

    # ⚠ Сторож не судит САМ СЕБЯ: в его описании рецепт назван словами, и без
    # этой оговорки он честно находит «копию без ключа» в собственной шапке.
    myself = os.path.abspath(__file__)

    good, bad = [], []
    seen = 0
    for path in sorted(set(walk(roots))):
        if os.path.abspath(path) == myself:
            continue
        seen += 1
        try:
            # ⚠ newline='' — файлы дерева и LF, и CRLF; молча нормализовать нельзя
            lines = io.open(path, 'r', encoding='utf-8-sig',
                            errors='replace', newline='').read().splitlines()
        except (IOError, OSError) as exc:
            sys.stderr.write(u'ОТКАЗ: не читается %s: %s\n' % (path, exc))
            return 2
        i = 0
        while i < len(lines):
            if RE_START.search(lines[i]):
                window = lines[i:i + WINDOW]
                if any(RE_PROJ.search(w) for w in window):
                    hit = (path, i + 1, lines[i].strip()[:110])
                    (good if any(RE_KEY.search(w) for w in window) else bad).append(hit)
                    i += WINDOW
                    continue
            i += 1

    if not seen:
        sys.stderr.write(u'ОТКАЗ: обходить нечего: %s\n' % roots)
        return 2

    print(u'обойдено файлов: %d' % seen)
    print(u'копий рецепта найдено: %d (с ключом %d, БЕЗ ключа %d)'
          % (len(good) + len(bad), len(good), len(bad)))
    if bad:
        print(u'')
        print(u'ОТКАЗ: копий без /p:GenerateManifests=false — %d' % len(bad))
        for path, no, text in bad:
            print(u'  %s:%d | %s' % (os.path.relpath(path, repo).replace('\\', '/'), no, text))
    else:
        print(u'копий без ключа: 0')
    if good:
        print(u'')
        print(u'с ключом (для сверки):')
        for path, no, _ in good:
            print(u'  %s:%d' % (os.path.relpath(path, repo).replace('\\', '/'), no))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
