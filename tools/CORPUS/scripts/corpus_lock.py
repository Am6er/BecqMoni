# -*- coding: utf-8 -*-
u"""Взаимный замок пересборки и приёмки корпуса (`A75`).

⛔ **Зачем.** Пересборка переписывает те же 129 файлов `corpus/spectra/<ключ>.xml`,
которые приёмка в это время читает, и ни одна из сторон об этом не знала.
`check_corpus.py` открывает спектр в четырёх местах, `res_apply.rewrite` пишет его
через `tree.write`, `build_corpus.write_copy` — тоже. Запущенные вместе, они дают
отказ на СЛУЧАЙНОМ спектре, и выглядит он не как «файл занят», а как
`OSError 22 Invalid argument` — то есть следующий пойдёт искать порчу данных, а не
встречный процесс. Ровно так 02.09.2026 и вышло: пересборка отказала дважды подряд
на разных файлах и разных шагах, и час ушёл на поиск несуществующей порчи.

⚠ **Тот отказ к этой причине НЕ привязан** — тайминги её сняли (`A74`). Замок стоит
здесь не как объяснение прошлого отказа, а потому что уговор между сессиями
третьего не остановит: в дереве работают до шести заходов разом.

## Что именно он делает

Два файла-метки в `corpus/`, оба вне git (`.gitignore` их не видит по имени с
точкой — они попадают под общее правило каталога):

* `.lock-write` — держит пересборка (шаги 1–3), пока пишет спектры;
* `.lock-read` — держит приёмка, пока читает.

Каждая сторона перед работой смотрит на ЧУЖОЙ замок. Отказ — с кодом 3 и внятной
причиной: кто держит, с какого времени и что делать.

## ⛔ Замок судится ЖИВОСТЬЮ ДЕРЖАТЕЛЯ, а не временем файла

«Свежий файл ≠ правильный файл» — и обратное тоже: старый замок не обязательно
мёртвый, а свежий не обязательно живой. Оборванный заход (их тут хватает — за один
вечер предел сессии убил шестерых агентов из восьми) оставляет метку навсегда, и
замок по времени превратился бы в вечный отказ, который все научатся обходить
ключом. Поэтому держатель проверяется по PID: на Windows — `OpenProcess`, иначе
`os.kill(pid, 0)`. Мёртвый замок снимается молча и работа продолжается, но об этом
ПЕЧАТАЕТСЯ — иначе тихое снятие скроет, что заход оборвался.

⚠ Замок НЕ защищает от третьего, который пишет спектры мимо этих трёх скриптов.
Такого сегодня нет, но если появится — он обязан взять `.lock-write` тем же
`hold()`, а не обойти его.
"""
import atexit
import io
import json
import os
import sys
import time

WRITE = u'write'
READ = u'read'

#: Кто чему мешает: держателю ключа нельзя работать при живом замке из списка.
CONFLICTS = {WRITE: [READ, WRITE], READ: [WRITE]}

_TAKEN = []


def _path(root, kind):
    return os.path.join(root, u'.lock-%s' % kind)


def _alive(pid):
    u"""Жив ли процесс. Ошибка проверки — считаем живым: замок надёжнее ложного
    отказа, чем ложного пропуска."""
    if pid is None:
        return True
    try:
        if os.name == 'nt':
            import ctypes
            SYNCHRONIZE = 0x00100000
            h = ctypes.windll.kernel32.OpenProcess(SYNCHRONIZE, False, int(pid))
            if h:
                ctypes.windll.kernel32.CloseHandle(h)
                return True
            return False
        os.kill(int(pid), 0)
        return True
    except OSError:
        return False
    except Exception:
        return True


def _read(path):
    try:
        with io.open(path, encoding='utf-8') as f:
            return json.load(f)
    except Exception:
        return None


def check(root, kind, out=None):
    u"""Свободно ли для работы вида `kind`. Возвращает текст отказа или None."""
    out = out or sys.stderr
    for other in CONFLICTS[kind]:
        path = _path(root, other)
        if not os.path.exists(path):
            continue
        held = _read(path)
        if held is None:
            out.write(u'⚠ замок %s нечитаем, снимаю\n' % os.path.basename(path))
            _drop(path)
            continue
        if not _alive(held.get('pid')):
            out.write(u'⚠ замок %s остался от МЁРТВОГО процесса (pid %s, взят %s) '
                      u'— снимаю и иду дальше.\n   Заход, который его брал, '
                      u'оборвался; проверьте, доделал ли он своё.\n'
                      % (os.path.basename(path), held.get('pid'), held.get('started')))
            _drop(path)
            continue
        return (u'⛔ ОТКАЗ: корпус занят. Замок %s держит живой процесс pid %s '
                u'(%s), взят %s.\n'
                u'   Пересборка пишет те же файлы `corpus/spectra/*.xml`, которые '
                u'читает приёмка, и вместе они дают отказ на СЛУЧАЙНОМ спектре — '
                u'выглядящий как порча данных (`OSError 22`), а не как встречный '
                u'процесс (`A75`).\n'
                u'   Дождитесь его конца. Если процесса на деле нет — удалите %s.'
                % (os.path.basename(path), held.get('pid'), held.get('what'),
                   held.get('started'), path))
    return None


def take(root, kind, what):
    u"""Взять замок. Снимается сам при выходе процесса."""
    path = _path(root, kind)
    body = {u'pid': os.getpid(), u'what': what, u'kind': kind,
            u'started': time.strftime(u'%Y-%m-%d %H:%M:%S')}
    with io.open(path, 'w', encoding='utf-8') as f:
        f.write(json.dumps(body, ensure_ascii=False, indent=1))
    _TAKEN.append(path)
    return path


def _drop(path):
    try:
        os.remove(path)
    except OSError:
        pass


def release(path=None):
    for p in ([path] if path else list(_TAKEN)):
        _drop(p)
        if p in _TAKEN:
            _TAKEN.remove(p)


atexit.register(release)


def guard(root, kind, what, out=None):
    u"""Проверить и взять. Возвращает текст отказа (тогда замок НЕ взят) или None.

    Приёмка и пересборка зовут это первой строкой `main()`: отказ обязан прийти
    ДО первого чтения или записи, иначе смысла в нём нет.
    """
    refusal = check(root, kind, out)
    if refusal is not None:
        return refusal
    take(root, kind, what)
    return None
