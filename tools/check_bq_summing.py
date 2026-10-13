#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож МНОЖИТЕЛЯ КАСКАДНОГО СУММИРОВАНИЯ панели выделения: приёмка пробы
`BqSummingProbeP256` (`BecquerelCoefficient.Summing`).

## Зачем

Зоны ROI сняты 07.10.2026 (`AMBER208`) вместе с пробой `RoiSummingProbeP167` —
единственной, что сверяла множитель `Summing` с независимым арбитром. Сам множитель
жив: его берёт панель выделения (`EnergySpectrumView.EnsureSelectionAnalytics`) и
печатает числом Бк на экране. Полоса П256 (14.10.2026) вернула покрытие пробой
`BqSummingProbeP256`, но звать её не мог никто — тот же признак отказа без
читателя, ради которого заведён `check_all.py` (`T220`, `T269`). Этот сторож —
читатель.

## Что судит проба (а сторож лишь зовёт её и читает приговор)

  А.  множитель приложения против арбитра `roi_mc2.py` (перебор схемы DDEP на `.rmx`
      корпуса, phys=26): 4 сцены × 9 линий Ba-133/Eu-152 = 36 линий; допуск 5 и 25 см
      — 3 %, вплотную — 6 %. Положительный контроль судьи А — внутри пробы: против
      арбитра 28.09.2026 та же мерка обязана поймать ровно прежнюю беду (`T268`);
  К1. суммирователь приложения (`CreateLikeFsa`) против суммирователя самого FSA
      после `Analyze` — CF и площади сумм девяти линий побитово;
  П.  панель выделения на спектрах поверки ЛСРМ 2016 (Gamma-1S, 5 см): «Бк панели /
      без поправки» обязано равняться множителю `Summing` (семь строк).

Арбитр — ЧИСЛА В КОДЕ ПРОБЫ (`Scenes()`), пересчитанные 14.10.2026; журнал —
`handover/handover-2026-10-14-p256-dose-coefficient-review.md`. Сменится физика
переноса (новые `.rmx`) — арбитр пересчитывать, а проба до того красна ЧЕСТНО.

## Что делает сторож

1. Свежесть штатного каталога проб (`T226`) — ТЕМИ ЖЕ функциями, что у сторожа окна
   отчёта FSA (`check_fsa_report_view.freshness` → `appwd_plan.ps1`): заверение
   `.appwd.json`, набор исходников ПРИЛОЖЕНИЯ, набор самой пробы (её `.cs` + довески),
   sha `BecquerelMonitor.exe` и `BqSummingProbeP256.exe` против заверенных. Своей
   копии отпечатка здесь нет (`T61`). Протух — код 3 с находками, а не прогон
   старым кодом (`A77`: проверять то, что БУДЕТ ИСПОЛЬЗОВАНО). Чужая незакоммиченная
   правка `.cs` приложения в дереве — законный код 3.
2. Прогон `BqSummingProbeP256.exe --corpus=<tools/CORPUS/corpus>` из каталога проб
   (~5 с). Матрицы идут из склада корпуса `corpus/geometries/response` (вне git)
   ПРЯМО В ПАМЯТЬ блоком «матрица из файла спектра» (`AMBER202`, П264); склад
   каталога проб не пишется вовсе — штатный каталог остаётся таким, каким был, и
   одновременные прогоны (несколько полос гонят `check_all`) друг другу не мешают.
3. Приговор — ТРИ признака разом: код пробы 0; последняя строка «ИТОГ: СОШЛОСЬ»; ни
   одной строки отказа (`!!`, «ОСНАСТКА:»). И четвёртый — ОХВАТ: проба напечатала не
   меньше 4 заголовков раздела А, 36 строк линий, 9 строк К1 «ok», 7 строк панели
   (проба, молча пропустившая раздел, иначе сошлась бы «пустым» вердиктом).
   Расхождение любого признака — отказ с печатью строк отказа и хвоста вывода.
4. Контроль судьи на КАЖДОМ прогоне (микросекунды): в настоящий вывод пробы
   подсаживаются четыре порчи — снята итоговая строка, `ok` → `!!`, выкинуты строки
   раздела А, чужой код возврата — и судья обязан отказать на каждой.

## Положительный контроль — `--selftest` (обе стороны)

  (в) штатный прогон — ОБЯЗАН пройти (сторож, отказывающий всегда, бесполезен так же,
      как молчащий всегда);
  (а) каталог проб подменён пустым — код 3 со словами «ПРОБЫ НЕТ»;
  (б) проба с `--spoil=arbiter` (арбитр первой линии ×1.10 В ПАМЯТИ) — сама проба
      ОБЯЗАНА отказать, сторож — код 1 со строкой `!!`, именем сцены и «отказов 1»;
  (г1) корпуса нет на диске — код 2 «СТЕНД»; (г2) корпус есть, спектров в нём нет —
      проба отказывает кодом 2, сторож — код 2 со словами пробы «ОСНАСТКА»;
  (д) порчи настоящего вывода (п. 4) — судья отказывает на каждой, и принимает
      нетронутый вывод.

  python tools/check_bq_summing.py [--probes=<каталог проб>] [--corpus=<каталог corpus>]
                                   [--skip-freshness] [--verbose] [--selftest]

  --probes=         каталог проб вместо `tools/effmaker/probes/build`
                    (иначе `BQ_SUMMING_PROBES`);
  --corpus=         каталог `corpus` вместо `tools/CORPUS/corpus` (спектры + склад
                    матриц `geometries/response`, вне git — §1.5 `tools/CORPUS/README.md`);
  --skip-freshness  НЕ судить свежесть каталога проб (только стенд полосы, где каталог
                    собран из ДРУГОГО дерева — worktree; в приёмку не годится);
  --verbose         напечатать весь вывод пробы, а не сводку;
  --selftest        положительный контроль (см. выше), ~25 с; свежесть судится один
                    раз, в (в); `--skip-freshness` снимает и её.

Коды возврата:
  0 — проба прошла: код 0, «ИТОГ: СОШЛОСЬ», строк отказа нет, охват полный
      (или самопроверка прошла);
  1 — проба нашла расхождения / упала / вывод не сходится с кодом; самопроверка не прошла;
  2 — стенд: нет корпуса, спектров или склада матриц на диске, проба отказала
      кодом 2 («мерить нечем»);
  3 — каталог проб не найден, не заверен или протух (находки `T226` напечатаны).
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
PROBE_EXE = u'BqSummingProbeP256.exe'
PROBE_KEY = os.path.splitext(PROBE_EXE)[0].lower()
BUILD_HINT = u'   собрать: pwsh tools\\effmaker\\probes\\build_all.ps1 -Bin BecquerelMonitor\\bin\\Debug_Codex'
VERDICT_OK = u'ИТОГ: СОШЛОСЬ'
PROBE_TIMEOUT_SEC = 300

# Охват: сколько строк каждого раздела проба ОБЯЗАНА напечатать (журнал П256, §2.3).
# «Не меньше»: добавленная сцена или линия сторожа не ломает, пропавший раздел — ломает.
MIN_SCENES = 4       # «=== А. <сцена> (…» — Ba-133/Eu-152 на четырёх геометриях
MIN_LINES_A = 36     # строки линий раздела А: 4 сцены × (4 линии Ba + 5 линий Eu)
MIN_LINES_K1 = 9     # строки К1 с «ok»: те же девять линий, побитово
MIN_LINES_P = 7      # строки панели выделения против паспорта

RE_SCENE = re.compile(u'^=== А\\. (\\S+) ')
# «  81.0       1.0627   1.0654   1.0585   1.0599     0.12     -0.40     -0.65»
RE_LINE_A = re.compile(u'^  (\\d+\\.\\d)\\s+(\\d+\\.\\d{4})\\s+(\\d+\\.\\d{4})\\s+(\\d+\\.\\d{4})\\s+(\\d+\\.\\d{4})'
                       u'\\s+(\\d+\\.\\d{2})\\s+(-?\\d+\\.\\d{2})\\s+(-?\\d+\\.\\d{2})')
RE_LINE_K1 = re.compile(u'^  (?:Ba|Eu)-\\d+\\s+\\d+\\.\\d+: CF FSA .* ok$')
# «  G1S16_Cs137_P5      661.7     83.64  1.0000      79023      81495      79283    1.031    1.003»
RE_LINE_P = re.compile(u'^  \\S+\\s+\\d+\\.\\d\\s+\\S+\\s+\\d\\.\\d{4}\\s+\\d+\\s+\\d+\\s+\\d+\\s+\\d\\.\\d{3}\\s+\\d\\.\\d{3}\\s*$')
# Отказ пробы: «    !! …» (раздел А, П), «  !! …» (К1, судья А), «… !!» (хвост строки К1),
# «ОСНАСТКА: …» (нет кривой, фона, матрицы в корпусе).
RE_RED = re.compile(u'^\\s*!!|\\s!!\\s*$|^\\s*ОСНАСТКА:')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


class Out(object):
    u"""Печать в консоль и в буфер — самопроверка читает свой же вывод (`silent`)."""

    def __init__(self, silent=False):
        self.buf = io.StringIO()
        self.silent = silent

    def say(self, text=u''):
        self.buf.write(text + u'\n')
        if not self.silent:
            sys.stdout.write(text + u'\n')
            sys.stdout.flush()


def find_probes(explicit):
    for candidate in (explicit, os.environ.get('BQ_SUMMING_PROBES')):
        if candidate:
            return os.path.abspath(candidate)
    return PROBES_DEFAULT


def rel(path):
    u"""Путь относительно дерева, если он внутри; иначе как есть."""
    try:
        if os.path.commonpath([os.path.abspath(path), REPO]) == REPO:
            return os.path.relpath(path, REPO)
    except ValueError:
        pass
    return path


def freshness(probes):
    u"""Находки сторожа свежести — функцией сторожа окна отчёта FSA (она же зовёт
    `appwd_plan.ps1`), приговор сужен до набора приложения, набора этой пробы и sha их
    exe. Возвращает (bad, note, err): `err` — сторож не отработал, и это тоже отказ,
    а не «проверено» (`T69`)."""
    try:
        import check_fsa_report_view as neighbour
    except Exception as e:  # сосед не читается — свежесть судить нечем, и это отказ
        return [], [], u'сторож свежести (check_fsa_report_view.freshness) не подключился: %s' % e
    return neighbour.freshness(probes, [PROBE_KEY])


# ── приговор ────────────────────────────────────────────────────────────────

class Verdict(object):
    u"""Итог чтения вывода пробы."""

    def __init__(self):
        self.code = 0          # код сторожа: 0 / 1 / 2
        self.why = u''         # причина отказа словами
        self.red = []          # строки отказа пробы
        self.last = u''        # последняя непустая строка
        self.scenes = 0
        self.lines_a = 0
        self.lines_k1 = 0
        self.lines_p = 0
        self.worst = None      # (|к нов.%|, значение, сцена, энергия)


def judge(code, text):
    u"""Приговор по четырём признакам разом: код пробы, последняя строка, строки
    отказа, охват. Возвращает Verdict."""
    v = Verdict()
    lines = text.splitlines()
    v.red = [l for l in lines if RE_RED.search(l)]
    for l in reversed(lines):
        if l.strip():
            v.last = l.strip()
            break
    scene = u'?'
    for l in lines:
        m = RE_SCENE.match(l)
        if m:
            v.scenes += 1
            scene = m.group(1)
            continue
        m = RE_LINE_A.match(l)
        if m:
            v.lines_a += 1
            dev = float(m.group(7))
            if v.worst is None or abs(dev) > v.worst[0]:
                v.worst = (abs(dev), dev, scene, m.group(1))
            continue
        if RE_LINE_K1.match(l):
            v.lines_k1 += 1
        elif RE_LINE_P.match(l):
            v.lines_p += 1

    if code == 2:
        v.code = 2
        v.why = u'проба отказала кодом 2 («мерить нечем»): нет спектра, кривой, матрицы корпуса'
        return v
    if code not in (0, 1):
        v.code = 1
        v.why = u'проба упала кодом %d (0x%08X) — приговора нет' % (code, code & 0xFFFFFFFF)
        return v
    if code == 1:
        v.code = 1
        v.why = u'проба нашла расхождения: «%s», строк отказа %d' % (v.last, len(v.red))
        return v
    # код 0 — сверить с выводом
    problems = []
    if v.last != VERDICT_OK:
        problems.append(u'последняя строка «%s», ждали «%s»' % (v.last, VERDICT_OK))
    if v.red:
        problems.append(u'строк отказа %d' % len(v.red))
    short = []
    for name, have, need in ((u'разделов А', v.scenes, MIN_SCENES), (u'строк линий А', v.lines_a, MIN_LINES_A),
                             (u'строк К1 «ok»', v.lines_k1, MIN_LINES_K1), (u'строк панели П', v.lines_p, MIN_LINES_P)):
        if have < need:
            short.append(u'%s %d из %d' % (name, have, need))
    if short:
        problems.append(u'охват неполный: ' + u', '.join(short))
    if problems:
        v.code = 1
        v.why = u'проба дала код 0, но вывод с ним не сходится: ' + u'; '.join(problems)
    return v


def judge_controls(text):
    u"""Контроль судьи на настоящем выводе (п. 4 шапки): нетронутый вывод принят, четыре
    порчи отвергнуты. Возвращает список слов-провалов (пусто — судья зряч)."""
    fails = []
    if judge(0, text).code != 0:
        fails.append(u'нетронутый вывод отвергнут')
    lines = text.splitlines()

    def mutated(fn):
        return u'\n'.join(fn(list(lines))) + u'\n'

    # 1. снята итоговая строка (проба оборвалась молча, код 0)
    def drop_verdict(ls):
        return [l for l in ls if l.strip() != VERDICT_OK]
    if judge(0, mutated(drop_verdict)).code != 1:
        fails.append(u'снятая «ИТОГ» принята')
    # 2. одна строка К1 «ok» стала «!!»
    def break_k1(ls):
        for i, l in enumerate(ls):
            if RE_LINE_K1.match(l):
                ls[i] = l[:-2] + u'!!'
                break
        return ls
    if judge(0, mutated(break_k1)).code != 1:
        fails.append(u'«!!» в строке К1 принят')
    # 3. выкинуты строки раздела А (проба молча пропустила сцену)
    def cut_a(ls):
        out, skip = [], 9
        for l in ls:
            if skip and RE_LINE_A.match(l):
                skip -= 1
                continue
            out.append(l)
        return out
    if judge(0, mutated(cut_a)).code != 1:
        fails.append(u'выкинутые строки А приняты')
    # 4. чужой код возврата при чистом выводе
    if judge(7, text).code != 1:
        fails.append(u'код 7 принят')
    return fails


# ── ход сторожа ──────────────────────────────────────────────────────────────

def run_check(probes, corpus, spoil=False, skip_freshness=False, verbose=False, out=None, sink=None):
    u"""Полный ход сторожа. Возвращает код возврата; печатает через `out`; `sink` (dict)
    получает 'text' — вывод пробы — для самопроверки."""
    out = out or Out()
    say = out.say
    say(u'    == сторож множителя каскадного суммирования панели выделения (BqSummingProbeP256) ==')
    say(u'      каталог проб : %s' % probes)
    say(u'      корпус       : %s' % corpus)

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
            say(u'      ⛔ КАТАЛОГ ПРОБ ПРОТУХ (T226) — прогон старым кодом судил бы не то, что в дереве:')
            for b in bad:
                say(u'         - %s' % b)
            say(BUILD_HINT)
            return 3

    for label, path in ((u'корпуса', corpus), (u'спектров корпуса', os.path.join(corpus, u'spectra')),
                        (u'склада матриц корпуса (вне git, tools/CORPUS/README.md §1.5)',
                         os.path.join(corpus, u'geometries', u'response'))):
        if not os.path.isdir(path):
            say(u'      ⛔ СТЕНД: нет каталога %s: %s' % (label, rel(path)))
            return 2

    cmd = [exe, u'--corpus=' + corpus] + ([u'--spoil=arbiter'] if spoil else [])
    t0 = time.time()
    try:
        proc = subprocess.run(cmd, cwd=probes, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              timeout=PROBE_TIMEOUT_SEC)
    except subprocess.TimeoutExpired:
        say(u'      ⛔ %s не вернулась за %d с — висит (модальное окно? безоконный прогон не должен их открывать)'
            % (PROBE_EXE, PROBE_TIMEOUT_SEC))
        return 1
    except OSError as e:
        say(u'      ⛔ %s не запустилась: %s' % (PROBE_EXE, e))
        return 2
    secs = time.time() - t0
    text = proc.stdout.decode('utf-8', 'replace')
    if sink is not None:
        sink['text'] = text

    v = judge(proc.returncode, text)
    say(u'      %s: код %d, %.1f с; разделов А %d, строк А %d, К1 %d, П %d; последняя строка «%s»; строк отказа %d'
        % (PROBE_EXE, proc.returncode, secs, v.scenes, v.lines_a, v.lines_k1, v.lines_p, v.last, len(v.red)))
    if verbose:
        for l in text.splitlines():
            say(u'        %s' % l.rstrip())
    if v.code != 0:
        say(u'      ⛔ %s: %s' % (PROBE_EXE, v.why))
        say(u'         позвано: %s' % u' '.join([PROBE_EXE] + cmd[1:]))
        for l in v.red[:40]:
            say(u'      %s' % l.rstrip())
        if len(v.red) > 40:
            say(u'      … и ещё %d строк отказа' % (len(v.red) - 40))
        if v.code == 2 or not v.red:
            # Отказ словами или падение — причина в хвосте вывода, не в строках отказа.
            say(u'      хвост вывода пробы:')
            for l in [l for l in text.splitlines() if l.strip()][-12:]:
                say(u'        %s' % l.rstrip())
        return v.code

    fails = judge_controls(text)
    if fails:
        say(u'      ⛔ КОНТРОЛЬ СУДЬИ: сторож слеп к подсаженной порче настоящего вывода: %s' % u'; '.join(fails))
        return 1
    say(u'      контроль судьи на настоящем выводе: нетронутый принят, 4 порчи отвергнуты')
    if v.worst is not None:
        say(u'      худшая линия раздела А: %s %s кэВ, %s %% от арбитра' % (v.worst[2], v.worst[3],
                                                                         (u'%+.2f' % v.worst[1])))
    say(u'    СОШЛОСЬ: множитель суммирования панели выделения — %d линий против арбитра, К1 побитово, панель сходится с Summing'
        % v.lines_a)
    return 0


# ── положительный контроль ───────────────────────────────────────────────────

def selftest(probes, corpus, skip_freshness=False):
    u"""Обе стороны: штатный прогон обязан пройти; пустой каталог проб — код 3; порча арбитра
    (`--spoil=arbiter`) — код 1; корпуса нет — код 2; корпус без спектров — код 2 пробы;
    порчи настоящего вывода — судья отказывает.

    Свежесть каталога судится ОДИН раз — в (в): остальные плечи гонят тот же каталог и
    судят приговор пробы, не его свежесть (иначе соседняя полоса, правящая `.cs`
    приложения посреди самопроверки, роняла бы их кодом 3). `--skip-freshness` снимает
    и её (стенд полосы, не приёмка)."""
    rows = []

    def record(tag, expect, code, buf, must_say):
        text = buf.getvalue()
        missing = [w for w in must_say if w not in text]
        ok = code == expect and not missing
        rows.append((tag, expect, code, ok, missing))
        print(u'  %s %s: код %d (ждали %d)%s' % (u'ok  ' if ok else u'⛔ ', tag, code, expect,
                                                  u'' if not missing else u'; нет слов: ' + u', '.join(missing)))
        if not ok:
            for l in text.splitlines()[-16:]:
                print(u'      ' + l.rstrip())

    print(u'  == самопроверка сторожа множителя суммирования панели выделения ==')

    sink = {}
    o = Out(silent=True)
    code = run_check(probes, corpus, skip_freshness=skip_freshness, out=o, sink=sink)
    record(u'(в) штатный прогон проходит', 0, code, o.buf, [u'СОШЛОСЬ', u'4 порчи отвергнуты'])

    empty = tempfile.mkdtemp(prefix=u'bq_summing_empty_')
    hollow = tempfile.mkdtemp(prefix=u'bq_summing_hollow_')
    try:
        o = Out(silent=True)
        code = run_check(empty, corpus, out=o)
        record(u'(а) пустой каталог проб — отказ со словом причины', 3, code, o.buf, [u'ПРОБЫ НЕТ'])

        o = Out(silent=True)
        code = run_check(probes, corpus, spoil=True, skip_freshness=True, out=o)
        record(u'(б) арбитр первой линии ×1.10 — проба отказывает, сторож код 1', 1, code, o.buf,
               [u'проба нашла расхождения', u'  !! G1S_point5_p16', u'РАЗОШЛОСЬ, отказов 1'])

        o = Out(silent=True)
        code = run_check(probes, os.path.join(empty, u'нет_такого_корпуса'), skip_freshness=True, out=o)
        record(u'(г1) корпуса нет на диске — код 2 «СТЕНД»', 2, code, o.buf, [u'СТЕНД: нет каталога корпуса'])

        os.makedirs(os.path.join(hollow, u'spectra'))
        os.makedirs(os.path.join(hollow, u'geometries', u'response'))
        o = Out(silent=True)
        code = run_check(probes, hollow, skip_freshness=True, out=o)
        record(u'(г2) корпус без спектров — проба отказывает кодом 2, сторож код 2', 2, code, o.buf,
               [u'проба отказала кодом 2', u'ОСНАСТКА'])
    finally:
        shutil.rmtree(empty, ignore_errors=True)
        shutil.rmtree(hollow, ignore_errors=True)

    fails = judge_controls(sink['text']) if sink.get('text') else [u'нет вывода штатного прогона (в)']
    ok = not fails
    rows.append((u'(д)', 0, 0 if ok else 1, ok, fails))
    print(u'  %s (д) судья на настоящем выводе: нетронутый принят, 4 порчи отвергнуты%s'
          % (u'ok  ' if ok else u'⛔ ', u'' if ok else u'; провалы: ' + u'; '.join(fails)))

    bad = [r for r in rows if not r[3]]
    if bad:
        print(u'  САМОПРОВЕРКА НЕ ПРОШЛА: %d из %d' % (len(bad), len(rows)))
        return 1
    print(u'  САМОПРОВЕРКА ПРОШЛА: %d из %d — сторож и отказывает, и пропускает там, где должен' % (len(rows), len(rows)))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=u'сторож множителя каскадного суммирования панели выделения', add_help=True)
    ap.add_argument(u'--probes', default=None)
    ap.add_argument(u'--corpus', default=CORPUS_DEFAULT)
    ap.add_argument(u'--skip-freshness', action=u'store_true')
    ap.add_argument(u'--verbose', action=u'store_true')
    ap.add_argument(u'--selftest', action=u'store_true')
    args = ap.parse_args(argv)
    probes = find_probes(args.probes)
    corpus = os.path.abspath(args.corpus)
    if args.selftest:
        return selftest(probes, corpus, skip_freshness=args.skip_freshness)
    return run_check(probes, corpus, skip_freshness=args.skip_freshness, verbose=args.verbose)


if __name__ == '__main__':
    sys.exit(main())
