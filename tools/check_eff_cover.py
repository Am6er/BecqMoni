#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож охвата кривых эффективности (`E37`, остаток): обёртка над пробой
`EffCoverProbe`.

## Зачем обёртка, а не проба прямо в списке

Читатель сторожей `tools/check_all.py` зовёт только питоновские
`tools/check_<имя>.py` (и, с 10.09.2026, такие же из подкаталогов — `SUBDIR`).
`EffCoverProbe` написана на C# и живёт в каталоге проб, то есть попасть в
приёмку сама не может. 10.09.2026 распорядитель попробовал вписать её в список
как есть и ЗАПИСЬ СНЯЛ: читатель не нашёл бы файла и завалил бы каждую приёмку.
Эта обёртка — недостающее звено: находит собранную пробу, зовёт её и ОТДАЁТ ЕЁ
КОД ВОЗВРАТА как свой.

## Что судит сама проба

Каждый спектр корпуса: узел `<Efficiency><Curve>` против `Min_Range`/`Max_Range`
конфигурации прибора, на которую спектр ссылается. Цена непокрытия молчаливая —
край кривой держится КОНСТАНТОЙ, и всякой линии ниже первой точки выдаётся
эффективность первой точки (у `AS80_lu_front` это было 0.01012 вместо 0.04483
на 60 кэВ, вчетверо). Разбор — в строке `E37` и в журнале полосы П18.

## ⛔ Отказ при ненайденной пробе — НАРОЧНО, и это не придирка

Обёртка, которая при отсутствующей пробе отвечает «сошлось», — сторож,
проходящий ВСЕГДА, то есть признак отказа без потребителя во второй степени:
и сам сторож ничего бы не судил, и никто бы об этом не узнал. Поэтому нет
пробы — код 3 с указанием, чем её собрать.

  python tools/check_eff_cover.py [--exe=<путь к EffCoverProbe.exe>]
                                  [--spectra=<каталог>] [--devices=<каталог>]
                                  [--selftest]

Где ищется проба, по старшинству:
  1. ключ `--exe=`;
  2. переменная среды `BQ_EFFCOVER_EXE`;
  3. `tools/effmaker/probes/build/EffCoverProbe.exe` — ШТАТНЫЙ каталог, тот
     самый, который `build_all.ps1` заполняет своим умолчанием `-Out`.
     Другие `build_*` (каталоги полос) НЕ перебираются нарочно: выбор «самого
     свежего из нескольких» — это судить по времени файла, а не по тому, чем
     он собран, и на этом дерево уже обжигалось.

Коды возврата:
  0 — все кривые покрывают рабочую полосу своего прибора;
  1 — есть непокрытые (проба назвала их поимённо);
  2 — проба не нашла каталогов спектров или приборов, либо не поняла ключа;
  3 — САМОЙ ПРОБЫ НЕТ: не собрана либо лежит не там (сказано, чем собрать);
  4 — проба не запустилась или упала (её вывод напечатан).
"""

import os
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROBE_REL = os.path.join(u'tools', u'effmaker', u'probes', u'build',
                         u'EffCoverProbe.exe')
BUILD_HINT = (u'   собрать: pwsh tools\\effmaker\\probes\\build_all.ps1 '
              u'-Bin BecquerelMonitor\\bin\\Debug_Codex')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def find_probe(explicit):
    u"""Путь к пробе по старшинству источников; None — не нашлась.

    ⚠ Путь возвращается АБСОЛЮТНЫМ: пробу зовут с `cwd` в корне дерева, а
    Windows ищет сам исполняемый файл от каталога ВЫЗЫВАЮЩЕГО процесса, не
    от `cwd` ребёнка. Относительный путь давал `[WinError 2]` — то есть код 4
    «проба не запустилась» вместо честного приговора.
    """
    for candidate in (explicit, os.environ.get('BQ_EFFCOVER_EXE')):
        if candidate:
            full = os.path.abspath(candidate)
            return full if os.path.isfile(full) else None

    default = os.path.join(REPO, PROBE_REL)
    return default if os.path.isfile(default) else None


def run(exe, passthrough):
    u"""Позвать пробу ИЗ КОРНЯ ДЕРЕВА: её умолчания путей отсчитываются оттуда."""
    cmd = [exe] + list(passthrough)
    try:
        proc = subprocess.run(cmd, cwd=REPO,
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    except OSError as e:
        print(u'⛔ проба не запустилась: %s' % e)
        return 4

    out = proc.stdout.decode('utf-8', 'replace')
    for line in out.splitlines():
        print(u'    ' + line)

    # ⚠ Код возврата пробы отдаётся СВОИМ, без перевода: у неё уже есть
    # договор (0 покрыто / 1 непокрыто / 2 нет каталогов), и второй перевод
    # означал бы вторую копию правила, которой некуда сойтись с первой.
    if proc.returncode not in (0, 1, 2):
        print(u'⛔ проба вернула неизвестный код %d' % proc.returncode)
        return 4

    return proc.returncode


def selftest():
    u"""⛔ Проверка САМОГО отказа: несуществующая проба обязана дать 3.

    Без неё «сторож зелен» и «сторожа некому звать» неразличимы — ровно та
    болезнь, ради которой обёртка и написана.
    """
    missing = os.path.join(REPO, u'tools', u'effmaker', u'probes',
                           u'build', u'EffCoverProbe.НЕТ-ТАКОЙ.exe')
    if find_probe(missing) is not None:
        print(u'⛔ САМОПРОВЕРКА ПРОВАЛЕНА: несуществующий путь признан пробой')
        return 1

    print(u'✅ самопроверка: несуществующая проба не признаётся найденной')
    return 0


def main(argv):
    explicit = None
    passthrough = []
    for a in argv:
        if a.startswith(u'--exe='):
            explicit = a[6:]
        elif a == u'--selftest':
            return selftest()
        elif a.startswith(u'--spectra=') or a.startswith(u'--devices=') or a == u'--quiet':
            passthrough.append(a)
        else:
            print(u'не знаю ключа: %s' % a)
            return 2

    exe = find_probe(explicit)
    if exe is None:
        where = explicit or os.environ.get('BQ_EFFCOVER_EXE') or PROBE_REL
        print(u'⛔ ПРОБЫ НЕТ: %s' % where)
        print(u'   Сторож судить нечем, и молчать об этом нельзя — обёртка,')
        print(u'   отвечающая «сошлось» без пробы, проходит ВСЕГДА.')
        print(BUILD_HINT)
        return 3

    print(u'проба: %s' % exe)
    return run(exe, passthrough)


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
