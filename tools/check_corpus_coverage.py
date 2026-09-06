#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""ЧИТАТЕЛЬ ОХВАТА КОРПУСНЫХ МЕРОК (`T76`).

Зачем он есть. Мерки, живущие на `tools/CORPUS/scripts/_corpus_raw`, молча
теряли семь спектров непонятной части, и «по всему корпусу» тихо означало
122 спектра из 129. 26.08.2026 у них завёлся сторож охвата
(`gaussfit_check.Coverage`) с кодом возврата 3 — и ни одного вызывающего:
`rebuild_corpus.py` этих мерок в конвейере не держит, `check_all.py` о них не
знал, то есть признак отказа был, а ПОТРЕБИТЕЛЯ у него не было. Это
повторяющаяся ошибка дерева, и лечится она ровно одним — читателем, которого
зовут на каждой приёмке.

Читатель судит ДВЕ вещи, и обе — не «красиво ли написано», а «сломается ли
число молча»:

**1. Коды возврата мерок.** Мерки зовутся с ключом `--coverage-only`: он
останавливает их сразу после стадии ЧТЕНИЯ, то есть ровно там, где считается
`hard=True`-стадия охвата. Второй копии правила чтения при этом не заводится —
это тот же цикл и тот же разбор файла, просто без фитов. Код 3 у любой из них
означает «посчитано не то, что объявлено», и здесь он становится отказом
приёмки.

⚠ **Что этот сторож НЕ проверяет.** Коды 4 (чистота ложного набора, `V16`) и
5 (признак вершины, `V17`) у `gate_blind_check.py` приходят из работы, которую
`--coverage-only` не делает; их читает только полный прогон мерки. Здесь об
этом сказано вслух, а не умолчано.

**2. Не завелась ли мерка БЕЗ охвата.** Всякий скрипт в
`tools/CORPUS/scripts`, который читает `_corpus_raw` или строит состояние
`ecal_extrapolation.build_state`, обязан считать охват (`Coverage`) — иначе он
повторит `T76` заново. Освобождённые перечислены поимённо ниже, у каждого —
причина и строка реестра; список освобождённых печатается каждый прогон, чтобы
он не превратился в глушилку.

    python tools/check_corpus_coverage.py            # быстро (~2 с)
    python tools/check_corpus_coverage.py --deep     # + две долгие мерки (~2.5 мин)
    python tools/check_corpus_coverage.py --raw=КАТАЛОГ   # проверить сторожа плохим входом

Коды возврата:
  0 — охват объяснён у всех позванных мерок, мерок без охвата нет;
  1 — отказ (мерка вернула не 0, либо завелась мерка без охвата);
  2 — не найден файл мерки или каталог скриптов.
"""

import argparse
import os
import re
import subprocess
import sys

#: Мерки: (файл, ключи, долгая ли). Долгие идут только по `--deep` — сторож,
#: который держит приёмку три минуты, звать перестанут, и он снова станет
#: признаком без читателя.
MEASURES = [
    (u'gaussfit_check.py', [u'--coverage-only'], False),
    (u'gate_blind_check.py', [u'--coverage-only'], True),
    (u'ecal_accept_check.py', [u'--coverage-only'], True),
]

#: Скрипты, которые живут на сырье/`build_state`, но охвата НЕ считают.
#: ⛔ Это не «разрешено», а «числится за строкой реестра»: без строки запись
#: сюда не ставится, иначе список станет способом обойти сторожа.
EXEMPT = {
    u'build_corpus.py':
        u'не мерка: стадия 1, которая _corpus_raw и СОЗДАЁТ',
    u'ecal_extrapolation.py':
        u'мерка без охвата — открытая строка `V15` (там же пересчёт приёмки `V12`)',
    u'calib_null_check.py':
        u'мерка на `build_state` без охвата — строка `T76`, остаток',
    u'calib_quality_f56.py':
        u'мерка на `build_state` без охвата — строка `T76`, остаток',
    u'calib_sweep_f56.py':
        u'мерка на `build_state` без охвата — строка `T76`, остаток',
}

#: По чему судим «живёт на сырье»: чтение своих копий либо стадия 1+2а.
LIVES_ON_RAW = re.compile(u'_corpus_raw|build_state')
#: По чему судим «охват считается».
HAS_COVERAGE = re.compile(u'Coverage')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def scripts_dir(repo):
    return os.path.join(repo, 'tools', 'CORPUS', 'scripts')


def call_measure(repo, name, argv):
    u"""Позвать мерку и ВЕРНУТЬ ЕЁ КОД. Печатается обращение, вывод и код."""
    path = os.path.join(scripts_dir(repo), name)
    rel = 'tools/CORPUS/scripts/%s' % name
    if not os.path.isfile(path):
        print(u'⛔ мерка не найдена на диске: %s' % rel)
        return None
    print(u'→ зову: python %s %s' % (rel, u' '.join(argv)))
    env = dict(os.environ)
    env['PYTHONIOENCODING'] = 'utf-8'
    env['PYTHONUTF8'] = '1'
    proc = subprocess.run([sys.executable, path] + list(argv), cwd=repo, env=env,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    out = proc.stdout.decode('utf-8', 'replace')
    for line in out.splitlines():
        print(u'    ' + line)
    print(u'%s %s: код %d' % (u'✅' if proc.returncode == 0 else u'⛔',
                              rel, proc.returncode))
    print(u'')
    return proc.returncode


def audit_scripts(repo):
    u"""Мерки без охвата. -> (список нарушителей, список освобождённых)."""
    d = scripts_dir(repo)
    bad, exempt_seen = [], []
    for name in sorted(os.listdir(d)):
        if not name.endswith('.py'):
            continue
        path = os.path.join(d, name)
        try:
            with open(path, 'rb') as f:
                text = f.read().decode('utf-8', 'replace')
        except OSError as ex:
            bad.append((name, u'не прочитан: %s' % ex))
            continue
        if not LIVES_ON_RAW.search(text):
            continue
        if HAS_COVERAGE.search(text):
            continue
        if name in EXEMPT:
            exempt_seen.append((name, EXEMPT[name]))
            continue
        bad.append((name, u'читает сырьё/`build_state`, а охвата (`Coverage`) не считает'))
    return bad, exempt_seen


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--deep', action='store_true',
                    help=u'звать и долгие мерки (~2.5 мин)')
    ap.add_argument('--raw', default=None,
                    help=u'каталог сырья вместо _corpus_raw — вход для проверки '
                         u'самого сторожа плохим входом')
    args = ap.parse_args(argv)

    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if not os.path.isdir(scripts_dir(repo)):
        print(u'ОТКАЗ: нет каталога %s' % scripts_dir(repo))
        return 2

    print(u'=== 1. КОДЫ ВОЗВРАТА МЕРОК ОХВАТА ===')
    called, red, missing = [], [], []
    for name, keys, slow in MEASURES:
        if slow and not args.deep:
            print(u'  пропущена %s — долгая (~мин); звать ключом --deep' % name)
            continue
        keys = list(keys)
        if args.raw and name == u'gaussfit_check.py':
            keys.append(u'--raw=%s' % args.raw)
        rc = call_measure(repo, name, keys)
        if rc is None:
            missing.append(name)
            continue
        called.append((name, rc))
        if rc != 0:
            red.append((name, rc))

    print(u'⚠ коды 4 (`V16`) и 5 (`V17`) у gate_blind_check.py этим сторожем НЕ '
          u'читаются: их даёт только полный прогон мерки, без --coverage-only.')

    print(u'')
    print(u'=== 2. МЕРКИ НА СЫРЬЕ БЕЗ ОХВАТА ===')
    bad, exempt_seen = audit_scripts(repo)
    for name, why in exempt_seen:
        print(u'  освобождён %-24s — %s' % (name, why))
    if not exempt_seen:
        print(u'  освобождённых нет')
    for name, why in bad:
        print(u'⛔ %s: %s' % (name, why))
    print(u'  нарушителей: %d' % len(bad))

    print(u'')
    print(u'=== сводка ===')
    for name, rc in called:
        print(u'  %s %-24s код %d' % (u'✅' if rc == 0 else u'⛔', name, rc))
    if missing:
        print(u'ОСТАНОВ: мерка не найдена на диске — %s' % u', '.join(missing))
        return 2
    if red or bad:
        print(u'ОСТАНОВ: охват корпусных мерок сломан — мерок с ненулевым кодом %d, '
              u'мерок без охвата %d. Числа таких мерок корпус НЕ описывают.'
              % (len(red), len(bad)))
        return 1
    print(u'ОХВАТ ЦЕЛ: позвано мерок %d, все дали 0; мерок на сырье без охвата нет.'
          % len(called))
    return 0


if __name__ == '__main__':
    sys.exit(main())
