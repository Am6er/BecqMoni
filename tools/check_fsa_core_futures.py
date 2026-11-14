#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ПЕРЕЧНЯ ПОЛЕЙ КОПИЙ ЯДРА FSA (`AMBER326`/`AMBER315`, полоса `a326u` 14.11.2026).

## Зачем

Параллельные заходы ядра разбора (`AMBER326`, полоса `a326p`: заход с привязкой и запасные
заходы стража нуля считаются на КОПИЯХ анализатора) включаются, только если перечень полей
экземпляра `FsaAnalyzer` разобран целиком: каждое поле отнесено к виду копирования
(`CoreFieldKind`). Новое поле-объект, которого перечень не знает, даёт вид `Unknown`, перечень —
`UNSOUND`, и `StartCoreFutures` молча возвращает ложь: разбор идёт на одном потоке, числа те же,
время дольше. Так и случилось 13.11.2026: поле `crystalOwnSource` (`a274g`, `e1d8c675`) выключило
копии у ВСЕХ разборов, и увидел это только критик `crit274u` (лог
`D:\\BqMoni_Claude\\crit274u\\r2\\core_audit.txt`) — сторожа на `UNSOUND` не было. Этот сторож — он.

Что значит зелёный: каждое поле разобрано. Правильность САМОГО разбора (какой вид верен для
поля) сторож не судит — это решает автор поля по правилу у `CoreReferenceFields` в
`FsaAnalyzer.cs`; сторож лишь не даёт забыть.

## Что делает

1. Свежесть штатного каталога проб (`T226`) — функцией сторожа окна отчёта FSA
   (`check_fsa_report_view.freshness`), сужено до набора приложения и пробы `FsaPerfProbeA326`.
2. `FsaPerfProbeA326.exe --audit` — первая строка обязана быть `sound<TAB>N`, строк полей ровно N,
   вид каждого из известных, ни одного `Unknown`. Иначе код 1 с именами неразобранных полей.
3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — на КАЖДОМ прогоне, тем же разбором полей в приложении
   (`FsaAnalyzer.CoreFieldAuditControl`):
   (а) `--audit-control=forget=<имя>` для КАЖДОГО поля вида `Reference` из НАСТОЯЩЕГО перечня —
       поле вычеркнуто из списков видов (так выглядел `master` до правки `a326u` для
       `crystalOwnSource`): судья ОБЯЗАН отказать и назвать это поле. Имена берутся из вывода
       `--audit`, а не из кода сторожа (критик `crit326u`): переименование поля контроль не обезоружит;
       полей вида `Reference` в перечне нет — контроль не состоялся, это отказ;
   (б) `--audit-control=extra` — к полям анализатора добавлено чужое поле-объект: судья ОБЯЗАН
       отказать и назвать `a326uControlField`;
   (в) порчи настоящего вывода в памяти (строка `sound` → `UNSOUND`; вид одного поля → `Unknown`;
       число полей в шапке +1; пустой вывод) — судья обязан отказать на каждой.
   Контроль, который не отказал, — код 1: сторож, не способный отказать, бесполезен.

  python tools/check_fsa_core_futures.py [--probes=<каталог проб>] [--skip-freshness] [--verbose]

  --probes=         каталог проб вместо `tools/effmaker/probes/build` (иначе `BQ_CORE_FUTURES_PROBES`);
  --skip-freshness  НЕ судить свежесть каталога (стенд полосы с каталогом из другого дерева; в приёмку
                    не годится);
  --verbose         напечатать весь перечень полей.

Коды возврата:
  0 — перечень разобран целиком, контроли отказали как должны;
  1 — есть неразобранные поля / вывод не читается / проба упала / контроль не отказал;
  3 — каталог проб не найден, не заверен или протух (находки `T226` напечатаны).

Идёт несколько секунд: запуск пробы на `--audit`, на каждое поле вида `Reference` (их 14 на
14.11.2026) и на чужое поле — проба отвечает до первого разбора; плюс сверка свежести.
"""

import argparse
import os
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

PROBES_DEFAULT = os.path.join(REPO, u'tools', u'effmaker', u'probes', u'build')
PROBE_EXE = u'FsaPerfProbeA326.exe'
PROBE_KEY = os.path.splitext(PROBE_EXE)[0].lower()
BUILD_HINT = u'   собрать: pwsh tools\\effmaker\\probes\\build_all.ps1 -Bin BecquerelMonitor\\bin\\Debug_Codex'
PROBE_TIMEOUT_SEC = 120

KINDS = (u'Setting', u'Value', u'Container', u'Reference', u'Shared', u'Fresh', u'Orchestrator')
CONTROL_EXTRA = u'a326uControlField'


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def say(text=u''):
    sys.stdout.write(text + u'\n')
    sys.stdout.flush()


def find_probes(explicit):
    for candidate in (explicit, os.environ.get('BQ_CORE_FUTURES_PROBES')):
        if candidate:
            return os.path.abspath(candidate)
    return PROBES_DEFAULT


def freshness(probes):
    try:
        import check_fsa_report_view as neighbour
    except Exception as e:
        return [], [], u'сторож свежести (check_fsa_report_view.freshness) не подключился: %s' % e
    return neighbour.freshness(probes, [PROBE_KEY])


def run_probe(exe, key):
    u"""(код, текст) запуска пробы с одним ключом; код None — не запустилась."""
    try:
        p = subprocess.run([exe, key], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                           timeout=PROBE_TIMEOUT_SEC, cwd=os.path.dirname(exe))
    except (OSError, subprocess.TimeoutExpired) as e:
        return None, u'%s' % e
    text = p.stdout.decode('utf-8', errors='replace').replace(u'\r\n', u'\n')
    return p.returncode, text


def judge(code, text):
    u"""Приговор перечню: (годен, причина, неразобранные имена, число полей)."""
    if code != 0:
        return False, u'проба кончилась кодом %s' % code, [], 0
    lines = [l for l in text.split(u'\n') if l.strip()]
    if not lines:
        return False, u'вывод пуст', [], 0
    head = lines[0].split(u'\t')
    if len(head) != 2 or not head[1].isdigit():
        return False, u'шапка не читается: «%s»' % lines[0], [], 0
    declared = int(head[1])
    rows = lines[1:]
    unknown, foreign = [], []
    for row in rows:
        parts = row.split(u'\t')
        if len(parts) != 3:
            foreign.append(row)
            continue
        if parts[1] == u'Unknown':
            unknown.append(parts[0])
        elif parts[1] not in KINDS:
            foreign.append(row)
    if foreign:
        return False, u'строки перечня не читаются: %s' % u'; '.join(foreign[:4]), unknown, len(rows)
    if len(rows) != declared:
        return False, u'в шапке полей %d, строк %d' % (declared, len(rows)), unknown, len(rows)
    if unknown:
        return False, u'неразобранных полей %d: %s' % (len(unknown), u', '.join(unknown)), unknown, len(rows)
    if head[0] != u'sound':
        return False, u'шапка «%s» при пустом списке Unknown' % head[0], unknown, len(rows)
    return True, u'', unknown, len(rows)


def reference_fields(text):
    u"""Имена полей вида Reference из настоящего вывода `--audit` (порядок вывода)."""
    out = []
    for line in text.split(u'\n')[1:]:
        parts = line.split(u'\t')
        if len(parts) == 3 and parts[1] == u'Reference':
            out.append(parts[0])
    return out


def spoils(text):
    u"""Порчи настоящего (годного) вывода: (имя, испорченный текст)."""
    lines = text.split(u'\n')
    out = []
    out.append((u'шапка sound → UNSOUND', u'\n'.join([lines[0].replace(u'sound', u'UNSOUND', 1)] + lines[1:])))
    for i in range(1, len(lines)):
        parts = lines[i].split(u'\t')
        if len(parts) == 3 and parts[1] == u'Reference':
            spoiled = list(lines)
            spoiled[i] = u'\t'.join([parts[0], u'Unknown', parts[2]])
            out.append((u'вид поля %s → Unknown' % parts[0], u'\n'.join(spoiled)))
            break
    head = lines[0].split(u'\t')
    if len(head) == 2 and head[1].isdigit():
        out.append((u'число полей в шапке +1', u'\n'.join([u'%s\t%d' % (head[0], int(head[1]) + 1)] + lines[1:])))
    out.append((u'пустой вывод', u''))
    return out


def main():
    ap = argparse.ArgumentParser(description=u'перечень полей копий ядра FSA обязан быть разобран (`AMBER315`)')
    ap.add_argument('--probes', default=None)
    ap.add_argument('--skip-freshness', action='store_true')
    ap.add_argument('--verbose', action='store_true')
    args = ap.parse_args()

    probes = find_probes(args.probes)
    exe = os.path.join(probes, PROBE_EXE)
    say(u'== перечень полей копий ядра FSA (`AMBER326`, сторож `a326u`) ==')
    say(u'   каталог проб: %s' % probes)
    if not os.path.isfile(exe):
        say(u'!! ПРОБЫ НЕТ: %s' % exe)
        say(BUILD_HINT)
        return 3

    if args.skip_freshness:
        say(u'   свежесть каталога НЕ судится (--skip-freshness) — в приёмку такой прогон не годится')
    else:
        bad, note, err = freshness(probes)
        for n in note:
            say(u'   %s' % n)
        if err:
            say(u'!! %s' % err)
            return 3
        if bad:
            say(u'!! КАТАЛОГ ПРОБ НЕ СВЕЖ (T226):')
            for b in bad:
                say(u'   %s' % b)
            say(BUILD_HINT)
            return 3

    failed = False

    code, text = run_probe(exe, u'--audit')
    ok, why, unknown, count = judge(code, text)
    if args.verbose:
        say(text.rstrip())
    if ok:
        say(u'   перечень: sound, полей %d, неразобранных нет' % count)
    else:
        failed = True
        say(u'!! ПЕРЕЧЕНЬ НЕ ГОДЕН: %s' % why)
        say(u'   заходы ядра на копиях анализатора ВЫКЛЮЧЕНЫ у всех разборов (StartCoreFutures → false);')
        say(u'   отнести поле к виду копирования в FsaAnalyzer.cs (CoreReferenceFields и соседи) по правилу там')

    # положительный контроль (а), (б): тот же разбор полей в приложении обязан отказать.
    # (а) — КАЖДОЕ поле вида Reference из НАСТОЯЩЕГО перечня (критик `crit326u`: не имя из кода сторожа).
    references = reference_fields(text) if code == 0 else []
    if not references:
        failed = True
        say(u'!! КОНТРОЛЬ (а) НЕ СОСТОЯЛСЯ: в перечне нет ни одного поля вида Reference')
    controls = [(u'--audit-control=forget=' + r, r) for r in references]
    controls.append((u'--audit-control=extra', CONTROL_EXTRA))
    caught_forget = 0
    for key, name in controls:
        c_code, c_text = run_probe(exe, key)
        c_ok, c_why, c_unknown, _ = judge(c_code, c_text)
        if c_code != 0:
            failed = True
            say(u'!! КОНТРОЛЬ %s: проба кончилась кодом %s — контролю нечем отказать' % (key, c_code))
        elif c_ok or name not in c_unknown:
            failed = True
            say(u'!! КОНТРОЛЬ %s НЕ ОТКАЗАЛ (неразобранные: %s)' % (key, u', '.join(c_unknown) or u'нет'))
        elif name == CONTROL_EXTRA:
            say(u'   контроль (б) %s: отказ, назван %s — так и должно' % (key, name))
        else:
            caught_forget += 1
    if references:
        say(u'   контроль (а) «поле вида Reference вычеркнуто из списков»: отказ с именем у %d из %d (%s)'
            % (caught_forget, len(references), u', '.join(references)))

    # положительный контроль (в): порчи настоящего вывода
    if ok:
        caught = 0
        cases = spoils(text)
        for label, spoiled in cases:
            s_ok, _, _, _ = judge(0, spoiled)
            if s_ok:
                failed = True
                say(u'!! КОНТРОЛЬ СУДЬИ: порча «%s» принята' % label)
            else:
                caught += 1
        if caught == len(cases):
            say(u'   контроль судьи: порчи %d из %d пойманы' % (caught, len(cases)))

    if failed:
        say(u'ИТОГ: ОТКАЗ')
        return 1
    say(u'ИТОГ: СОШЛОСЬ')
    return 0


if __name__ == '__main__':
    sys.exit(main())
