#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ОБРЫВА РЯДА ПРИ ПУСТОМ ОТРЕЗКЕ: `FsaChainCutEmptySegmentProbeA309` (`AMBER309`).

## Зачем

Точечный Th-228 с верными подписями набирал порог 30 % целиком, подцепочка не искалась, Ac-228 шёл в состав, и
активность падала до половины паспорта (`handover/handover-2026-11-05-fixlib.md`, раздел 8). Правка в
`FsaCompositionInference.HasEmptySegment`: ряд, набравший порог, всё равно судится на обрыв, если у него есть отрезок
с ожидаемыми ≥ 4 группами и совпавшими < 15 %. Проба без читателя — признак отказа без потребителя (`T220`), этот сторож
её читатель.

## Что судит проба (сторож лишь зовёт её и читает приговор)

Тем же кодом, что приложение, на поставочной библиотеке каталога проб:

  1. источники Th-228 (5 спектров): Th-232 принят и оборван без Ac-228;
  2. равновесный Th-232 (6 спектров, в их числе `RC103_Th232WT20`, самая низкая доля Ac-228): ряд целиком.

## Приговор

Проба СВЕЖА (`T226`, функцией соседа `check_fsa_report_view.freshness`), код 0, последняя строка «ВСЕ СОШЛИСЬ», ни одной
строки «⛔», и строк «ok» не меньше 11. Расхождение любого из признаков — код 1. Нет пробы или каталог протух — код 3,
нет спектров — код 2.

## Положительный контроль — `--selftest`

  (а) пустой каталог проб — код 3;
  (б) проба с `--spoil=expect` (ожидание первой строки перевёрнуто) — код 1;
  (в) штатный прогон — код 0;
  (г) нет каталога спектров — код 2;
  (д) порча настоящего вывода: снята итоговая строка, «ok» стал «⛔», выкинута строка, чужой код — судья отказывает.

Мутант кода «снова `continue`» даёт код 1 на этой же пробе: замер в журнале, в самопроверку не входит (нужна пересборка).

  python tools/check_chain_cut.py [--probes=<каталог проб>] [--corpus=<каталог corpus>]
                                  [--skip-freshness] [--verbose] [--selftest]

Коды: 0 — сошлось; 1 — разошлось; 2 — стенд; 3 — нет пробы или каталог протух.
"""

import argparse
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

PROBES_DEFAULT = os.path.join(REPO, u'tools', u'effmaker', u'probes', u'build')
CORPUS_DEFAULT = os.path.join(REPO, u'tools', u'CORPUS', u'corpus')
PROBE_EXE = u'FsaChainCutEmptySegmentProbeA309.exe'
PROBE_KEY = os.path.splitext(PROBE_EXE)[0].lower()
BUILD_HINT = u'   собрать: pwsh tools\\effmaker\\probes\\build_all.ps1 -Bin BecquerelMonitor\\bin\\Debug_Codex'
VERDICT_OK = u'ВСЕ СОШЛИСЬ'
PROBE_TIMEOUT_SEC = 180
MIN_ROWS = 11  # 5 источников Th-228 + 6 равновесных Th-232

RE_OK = re.compile(u'^  ok  \\S+: ')
RE_RED = re.compile(u'^  ⛔  ')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


class Out(object):
    u"""Печать в консоль и в буфер: самопроверка читает свой же вывод."""

    def __init__(self, silent=False):
        self.buf = io.StringIO()
        self.silent = silent

    def say(self, text=u''):
        self.buf.write(text + u'\n')
        if not self.silent:
            sys.stdout.write(text + u'\n')
            sys.stdout.flush()


def freshness(probes):
    u"""Свежесть — функцией сторожа окна отчёта FSA, приговор сужен до набора приложения, этой пробы и двух exe."""
    try:
        import check_fsa_report_view as neighbour
    except Exception as e:
        return [], [], u'сторож свежести (check_fsa_report_view.freshness) не подключился: %s' % e
    return neighbour.freshness(probes, [PROBE_KEY])


class Verdict(object):
    def __init__(self):
        self.code = 0
        self.why = u''
        self.red = []
        self.last = u''
        self.rows = 0


def judge(code, text):
    u"""Приговор по четырём признакам разом: код пробы, последняя строка, строки отказа, охват."""
    v = Verdict()
    lines = text.splitlines()
    v.red = [l for l in lines if RE_RED.search(l)]
    v.rows = sum(1 for l in lines if RE_OK.search(l))
    for l in reversed(lines):
        if l.strip():
            v.last = l.strip()
            break
    if code == 2:
        v.code = 2
        v.why = u'проба отказала кодом 2 («мерить нечем»)'
        return v
    if code not in (0, 1):
        v.code = 1
        v.why = u'проба упала кодом %d (0x%08X) — приговора нет' % (code, code & 0xFFFFFFFF)
        return v
    if code == 1:
        v.code = 1
        v.why = u'проба нашла расхождения: «%s», строк отказа %d' % (v.last, len(v.red))
        return v
    problems = []
    if v.last != VERDICT_OK:
        problems.append(u'последняя строка «%s», ждали «%s»' % (v.last, VERDICT_OK))
    if v.red:
        problems.append(u'строк отказа %d' % len(v.red))
    if v.rows < MIN_ROWS:
        problems.append(u'охват неполный: строк «ok» %d из %d' % (v.rows, MIN_ROWS))
    if problems:
        v.code = 1
        v.why = u'проба дала код 0, но вывод с ним не сходится: ' + u'; '.join(problems)
    return v


def judge_controls(text):
    u"""Контроль судьи на настоящем выводе: нетронутый принят, четыре порчи отвергнуты."""
    fails = []
    if judge(0, text).code != 0:
        fails.append(u'нетронутый вывод отвергнут')
    lines = text.splitlines()
    if judge(0, u'\n'.join(l for l in lines if l.strip() != VERDICT_OK) + u'\n').code != 1:
        fails.append(u'снятая итоговая строка принята')
    broken = list(lines)
    for i, l in enumerate(broken):
        if RE_OK.search(l):
            broken[i] = u'  ⛔  ' + l[6:]
            break
    if judge(0, u'\n'.join(broken) + u'\n').code != 1:
        fails.append(u'«⛔» в строке принят')
    cut = []
    dropped = False
    for l in lines:
        if not dropped and RE_OK.search(l):
            dropped = True
            continue
        cut.append(l)
    if judge(0, u'\n'.join(cut) + u'\n').code != 1:
        fails.append(u'выкинутая строка принята')
    if judge(7, text).code != 1:
        fails.append(u'код 7 принят')
    return fails


def run_check(probes, corpus, spoil=False, skip_freshness=False, verbose=False, out=None, sink=None):
    out = out or Out()
    say = out.say
    say(u'    == сторож обрыва ряда при пустом отрезке (`AMBER309`) ==')
    say(u'      каталог проб : %s' % probes)
    exe = os.path.join(probes, PROBE_EXE)
    if not os.path.isdir(probes) or not os.path.isfile(exe) or not os.path.isfile(os.path.join(probes, u'BecquerelMonitor.exe')):
        say(u'      ⛔ ПРОБЫ НЕТ: %s (%s / BecquerelMonitor.exe)' % (probes, PROBE_EXE))
        say(BUILD_HINT)
        return 3
    if skip_freshness:
        say(u'      ⚠ свежесть каталога проб НЕ судится (--skip-freshness): стенд, не приёмка')
    else:
        t0 = time.time()
        bad, note, err = freshness(probes)
        if err:
            say(u'      ⛔ %s' % err)
            return 3
        for n in note:
            say(u'      свежесть: %s (%.1f с)' % (n, time.time() - t0))
        if bad:
            say(u'      ⛔ КАТАЛОГ ПРОБ ПРОТУХ (T226) — прогон старым кодом судить нельзя:')
            for b in bad:
                say(u'         - %s' % b)
            say(BUILD_HINT)
            return 3
    spectra = os.path.join(corpus, u'spectra')
    if not os.path.isdir(spectra):
        say(u'      ⛔ СТЕНД: нет каталога спектров корпуса: %s' % spectra)
        return 2
    cmd = [exe, u'--spectra=' + spectra] + ([u'--spoil=expect'] if spoil else [])
    t0 = time.time()
    try:
        proc = subprocess.run(cmd, cwd=probes, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=PROBE_TIMEOUT_SEC)
    except subprocess.TimeoutExpired:
        say(u'      ⛔ %s не вернулась за %d с — висит (модальное окно?)' % (PROBE_EXE, PROBE_TIMEOUT_SEC))
        return 1
    except OSError as e:
        say(u'      ⛔ %s не запустилась: %s' % (PROBE_EXE, e))
        return 2
    text = proc.stdout.decode('utf-8', 'replace')
    if sink is not None:
        sink['text'] = text
    v = judge(proc.returncode, text)
    say(u'      %s: код %d, %.1f с; строк ok %d, строк отказа %d; последняя строка «%s»'
        % (PROBE_EXE, proc.returncode, time.time() - t0, v.rows, len(v.red), v.last))
    if verbose:
        for l in text.splitlines():
            say(u'        %s' % l.rstrip())
    if v.code != 0:
        say(u'      ⛔ %s: %s' % (PROBE_EXE, v.why))
        say(u'         позвано: %s' % u' '.join([PROBE_EXE] + cmd[1:]))
        for l in v.red[:20]:
            say(u'      %s' % l.rstrip())
        if v.code == 2 or not v.red:
            say(u'      хвост вывода пробы:')
            for l in [l for l in text.splitlines() if l.strip()][-8:]:
                say(u'        %s' % l.rstrip())
        return v.code
    fails = judge_controls(text)
    if fails:
        say(u'      ⛔ КОНТРОЛЬ СУДЬИ: сторож слеп к подсаженной порче настоящего вывода: ' + u'; '.join(fails))
        return 1
    say(u'      контроль судьи на настоящем выводе: нетронутый принят, 4 порчи отвергнуты')
    say(u'    СОШЛОСЬ: обрыв ряда при пустом отрезке — %d строк' % v.rows)
    return 0


def selftest(probes, corpus, skip_freshness=False):
    rows = []

    def record(tag, expect, code, buf, must_say):
        text = buf.getvalue()
        missing = [w for w in must_say if w not in text]
        ok = code == expect and not missing
        rows.append(ok)
        print(u'  %s %s: код %d (ждали %d)%s' % (u'ok  ' if ok else u'⛔ ', tag, code, expect,
                                                  u'' if not missing else u'; нет слов: ' + u', '.join(missing)))
        if not ok:
            for l in text.splitlines()[-12:]:
                print(u'      ' + l.rstrip())

    print(u'  == самопроверка сторожа обрыва ряда ==')
    sink = {}
    o = Out(silent=True)
    code = run_check(probes, corpus, skip_freshness=skip_freshness, out=o, sink=sink)
    record(u'(в) штатный прогон проходит', 0, code, o.buf, [u'СОШЛОСЬ', u'4 порчи отвергнуты'])
    empty = tempfile.mkdtemp(prefix=u'chain_cut_empty_')
    try:
        o = Out(silent=True)
        code = run_check(empty, corpus, out=o)
        record(u'(а) пустой каталог проб — отказ со словом причины', 3, code, o.buf, [u'ПРОБЫ НЕТ'])
        o = Out(silent=True)
        code = run_check(probes, corpus, spoil=True, skip_freshness=True, out=o)
        record(u'(б) перевёрнуто ожидание первой строки — проба отказывает, сторож код 1', 1, code, o.buf,
               [u'проба нашла расхождения', u'НЕ СОШЛОСЬ: 1'])
        o = Out(silent=True)
        code = run_check(probes, os.path.join(empty, u'нет_такого_корпуса'), skip_freshness=True, out=o)
        record(u'(г) нет каталога спектров — код 2 «СТЕНД»', 2, code, o.buf, [u'СТЕНД'])
    finally:
        shutil.rmtree(empty, ignore_errors=True)
    fails = judge_controls(sink['text']) if sink.get('text') else [u'нет вывода штатного прогона']
    rows.append(not fails)
    print(u'  %s (д) судья на настоящем выводе: нетронутый принят, 4 порчи отвергнуты%s'
          % (u'ok  ' if not fails else u'⛔ ', u'' if not fails else u'; провалы: ' + u'; '.join(fails)))
    if not all(rows):
        print(u'  САМОПРОВЕРКА НЕ ПРОШЛА: %d из %d' % (rows.count(False), len(rows)))
        return 1
    print(u'  САМОПРОВЕРКА ПРОШЛА: %d из %d — сторож и отказывает, и пропускает' % (len(rows), len(rows)))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=u'сторож обрыва ряда при пустом отрезке')
    ap.add_argument(u'--probes', default=PROBES_DEFAULT)
    ap.add_argument(u'--corpus', default=CORPUS_DEFAULT)
    ap.add_argument(u'--skip-freshness', action=u'store_true')
    ap.add_argument(u'--verbose', action=u'store_true')
    ap.add_argument(u'--selftest', action=u'store_true')
    args = ap.parse_args(argv)
    probes = os.path.abspath(args.probes)
    corpus = os.path.abspath(args.corpus)
    if args.selftest:
        return selftest(probes, corpus, skip_freshness=args.skip_freshness)
    return run_check(probes, corpus, skip_freshness=args.skip_freshness, verbose=args.verbose)


if __name__ == '__main__':
    sys.exit(main())
