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

Без доводов обходится ВСЁ дерево от корня репозитория. Что при этом НЕ судится —
по правилу, не по подгону (`T221`); каждое исключение печатается с числом:

  * «журнал — запись, а не инструкция» (`T219`): всё в `handover/` и файлы
    `handover-*.md` где угодно, КРОМЕ исполняемого (`.ps1 .py .cmd .bat .cs`) —
    скрипт полосы сборку зовёт по-настоящему и судится. Копия рецепта в журнале
    за август 2026 — это запись о том, как тогда собирали (ключ найден 25.08.2026,
    позже них), и правка её была бы подделкой записи. Такие копии считаются и
    печатаются к глазам («без ключа — историчных N»), но приговор не меняют.
  * «вне git — за Amber»: файл, который git ИГНОРИРУЕТ (`git check-ignore`) —
    `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json` — личный или
    машинный, полосой не правится (решение Amber 05.09.2026 по `AGENTS.md`).
    Копия без ключа в нём печатается ПРЕДУПРЕЖДЕНИЕМ поимённо, чтобы Amber её
    видела, но код возврата не меняет. Без git под рукой правило снимается и
    судится всё — об этом печатается.

Положительный контроль: подброшенный `.ps1` с рецептом без ключа вне `handover/`
даёт код 1; тот же текст в `handover/*.md` — запись, код 0 с числом историчных.

Коды возврата:
  0 — копий без ключа среди СУДИМЫХ файлов нет;
  1 — есть копия без ключа (перечень поимённо: файл, строка);
  2 — нечего обходить.
"""

import io
import os
import re
import subprocess
import sys

RE_START = re.compile(r'MSBuild\.exe|\$msbuild\b', re.IGNORECASE)
RE_PROJ = re.compile(r'BecquerelMonitor\.csproj', re.IGNORECASE)
RE_KEY = re.compile(r'/p:GenerateManifests\s*=\s*false', re.IGNORECASE)

WINDOW = 8
EXTS = ('.md', '.ps1', '.py', '.cs', '.cmd', '.bat', '.txt', '.json')
RUNNABLE = ('.ps1', '.py', '.cmd', '.bat', '.cs')
SKIP_DIRS = ('.git', '.vs', 'bin', 'obj', 'packages', '__pycache__', 'node_modules')
RE_JOURNAL_NAME = re.compile(r'^handover-.*\.md$', re.IGNORECASE)


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


def rel_of(repo, path):
    return os.path.relpath(path, repo).replace('\\', '/')


def is_journal(rel):
    u"""Запись, а не инструкция: `handover/**` и `handover-*.md`, кроме исполняемого."""
    low = rel.lower()
    if os.path.splitext(low)[1] in RUNNABLE:
        return False
    return low.startswith('handover/') or bool(RE_JOURNAL_NAME.match(os.path.basename(low)))


def git_ignored(repo, rels):
    u"""Множество путей (относительных, с `/`), которые git игнорирует.
    Возвращает None, если git недоступен — тогда правило «вне git» не применяется."""
    inside = [r for r in rels if not r.startswith('../') and not os.path.isabs(r)]
    if not inside:
        return set()
    try:
        proc = subprocess.run(
            ['git', 'check-ignore', '-z', '--stdin'],
            cwd=repo, input=u'\0'.join(inside).encode('utf-8'),
            stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    except (OSError, ValueError):
        return None
    # 0 — что-то игнорируется, 1 — ничего, 128 и прочее — git не в духе
    if proc.returncode not in (0, 1):
        return None
    return set(p for p in proc.stdout.decode('utf-8', 'replace').split(u'\0') if p)


def find_copies(lines):
    u"""Список (номер строки, текст, есть ли ключ) по копиям рецепта в файле."""
    out = []
    i = 0
    while i < len(lines):
        if RE_START.search(lines[i]):
            window = lines[i:i + WINDOW]
            if any(RE_PROJ.search(w) for w in window):
                out.append((i + 1, lines[i].strip()[:110],
                            any(RE_KEY.search(w) for w in window)))
                i += WINDOW
                continue
        i += 1
    return out


def main(argv):
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    roots = argv[1:] or [repo]

    # ⚠ Сторож не судит САМ СЕБЯ: в его описании рецепт назван словами, и без
    # этой оговорки он честно находит «копию без ключа» в собственной шапке.
    myself = os.path.abspath(__file__)

    files = [p for p in sorted(set(walk(roots))) if os.path.abspath(p) != myself]
    if not files:
        sys.stderr.write(u'ОТКАЗ: обходить нечего: %s\n' % roots)
        return 2

    rels = [rel_of(repo, p) for p in files]
    ignored = git_ignored(repo, rels)
    if ignored is None:
        print(u'⚠ git недоступен: правило «вне git — за Amber» не применяется, судится всё')
        ignored = set()

    good, bad = [], []           # судимые копии
    hist_good, hist_bad = [], [] # копии в журналах-записях
    own_good, own_bad = [], []   # копии в файлах вне git (за Amber)
    n_journals = 0
    for path, rel in zip(files, rels):
        try:
            # ⚠ newline='' — файлы дерева и LF, и CRLF; молча нормализовать нельзя
            lines = io.open(path, 'r', encoding='utf-8-sig',
                            errors='replace', newline='').read().splitlines()
        except (IOError, OSError) as exc:
            sys.stderr.write(u'ОТКАЗ: не читается %s: %s\n' % (rel, exc))
            return 2
        journal = is_journal(rel)
        if journal:
            n_journals += 1
        for no, text, has_key in find_copies(lines):
            hit = (rel, no, text)
            if journal:
                (hist_good if has_key else hist_bad).append(hit)
            elif rel in ignored:
                (own_good if has_key else own_bad).append(hit)
            else:
                (good if has_key else bad).append(hit)

    total = len(good) + len(bad) + len(hist_good) + len(hist_bad) + len(own_good) + len(own_bad)
    print(u'обойдено файлов: %d (из них журналов-записей %d, вне git %d)'
          % (len(files), n_journals, len(ignored)))
    print(u'копий рецепта найдено: %d; судимых %d (с ключом %d, БЕЗ ключа %d)'
          % (total, len(good) + len(bad), len(good), len(bad)))
    print(u'не судим по правилу «журнал — запись» (T219): %d копий, из них без ключа — историчных %d'
          % (len(hist_good) + len(hist_bad), len(hist_bad)))
    for rel, no, _ in hist_bad:
        print(u'    запись: %s:%d' % (rel, no))
    print(u'не судим по правилу «вне git — за Amber»: %d копий, из них без ключа %d'
          % (len(own_good) + len(own_bad), len(own_bad)))
    for rel, no, _ in own_bad:
        print(u'    ⚠ ПРЕДУПРЕЖДЕНИЕ (за Amber): %s:%d — без ключа' % (rel, no))

    if bad:
        print(u'')
        print(u'ОТКАЗ: копий без /p:GenerateManifests=false — %d' % len(bad))
        for rel, no, text in bad:
            print(u'  %s:%d | %s' % (rel, no, text))
    else:
        print(u'копий без ключа среди судимых: 0')
    if good:
        print(u'')
        print(u'с ключом (для сверки):')
        for rel, no, _ in good:
            print(u'  %s:%d' % (rel, no))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
