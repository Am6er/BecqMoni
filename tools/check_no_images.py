#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож «никакие скриншоты не коммитить» (распоряжение Amber 17.09.2026).

Amber 17.09.2026, консоль, дословно: «Запиши в правило: никакие скриншоты не
комитить!» и «Найти все картинки в handover и удалить и удалить их из
коммитов!». Поводом стал коммит `304f9822` с девятью PNG приёмки экраном; при
ревизии в HEAD нашлось 240 картинок под `handover/` (15.9 МБ), сняты одним
коммитом 17.09.2026. Правило — `CLAUDE.md` «Conventions» (первый пункт) и
`.claude/skills/todo-work/SKILL.md` §4.

Что проверяется: в ИНДЕКСЕ git (`git ls-files`, то есть то, что уйдёт в
следующий коммит) нет ни одного файла с расширением картинки под `handover/`
и `tools/`. Картинки приложения (`BecquerelMonitor/Resources/*.png`, иконки,
`packages/**`) — не свидетельства, а ресурсы, и сторожа не касаются.

Что НЕ ловится, честно: картинка под чужим расширением (`.dat`) и картинка,
вставленная base64 в `.md`. Читатель — распорядитель при коммите; `.gitignore`
на те же расширения под `handover/` — вторая линия.

Запуск:
    python tools/check_no_images.py            # проверить индекс
    python tools/check_no_images.py --selftest # положительный контроль

Код возврата: 0 — картинок нет; 1 — есть (или контроль провален);
2 — git не отвечает.
"""
from __future__ import print_function
import io
import os
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
EXT = ('.png', '.jpg', '.jpeg', '.bmp', '.gif', '.svg', '.emf', '.wmf', '.webp',
       '.tif', '.tiff', '.ico')
DIRS = ('handover/', 'tools/')


def tracked_images(extra=()):
    try:
        out = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT)
    except Exception as ex:  # git не отвечает
        print(u'git ls-files: %s' % ex)
        sys.exit(2)
    names = [n for n in out.decode('utf-8', 'replace').split('\0') if n] + list(extra)
    return sorted(n for n in names
                  if n.startswith(DIRS) and n.lower().endswith(EXT))


def report(hits):
    if hits:
        print(u'⛔ КАРТИНКИ В ИНДЕКСЕ GIT (%d) — распоряжение Amber 17.09.2026: никакие скриншоты не коммитить'
              % len(hits))
        for n in hits:
            print(u'  ' + n)
        print(u'снять: git rm --cached -- <файл> (и удалить с диска), .gitignore уже держит эти расширения под handover/')
        return 1
    print(u'картинок под %s в индексе git нет' % ', '.join(DIRS))
    return 0


def selftest():
    u"""Подсаженное имя обязано ловиться, чистый индекс — проходить."""
    clean = tracked_images()
    planted = tracked_images(extra=[u'handover/p00-selftest/shot.PNG'])
    ok = (not clean) and (planted == [u'handover/p00-selftest/shot.PNG'])
    print(u'selftest: чистый индекс -> %d находок; подсадка -> %d находок; %s'
          % (len(clean), len(planted), u'СОШЛОСЬ' if ok else u'ПРОВАЛЕН'))
    return 0 if ok else 1


if __name__ == '__main__':
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if '--selftest' in sys.argv[1:]:
        sys.exit(selftest())
    sys.exit(report(tracked_images()))
