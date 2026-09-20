#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ОКНА ОТЧЁТА FSA (`FSAReportView`): приёмка `FsaReportViewProbe` на паре
спектров корпуса — блок «Качество разбора» (χ²/ndf, σ×, пометки, строки привязки
шкалы `AMBER17`), состав строк по `Tag.Kind`, один результат на график и таблицу,
родители/дочерние, повторяемость, снимки en/ru без обрезанных подписей.

## Зачем

Пробу `FsaReportViewProbe` собирал `build_all.ps1`, но НЕ ЗАПУСКАЛ никто: ни один
`check_*.py` её не звал. Итог измерен: 11.09.2026 окно получило строку «Привязка
шкалы, опор» (`AMBER17`, решение Amber «ВКЛ умолчанием — привязка работает сама,
слой показывает опоры»), проба ждала ровно три пометки и была красна «пометок 4
вместо 3» СЕМЬ ДНЕЙ — заметила это полоса П104 18.09.2026, запустив пробу руками
по другому поводу; починила П105 тем же днём. Признак отказа без читателя — та же
беда, ради которой заведён сам `check_all.py` (`T220`). Здесь — читатель этой
пробы.

## Что делает

1. Свежесть штатного каталога проб (`T226`) — ТЕМИ ЖЕ функциями `appwd_plan.ps1`,
   что судят оснастку корпуса (`Get-AppWdSourceRecord`, `Get-AppWdStampSources`,
   `Compare-AppWdSourceMap`, `Get-AppWdSha256`; своей копии отпечатка нет — `T61`):
   заверение `.appwd.json`, набор исходников ПРИЛОЖЕНИЯ (по `.csproj`), набор
   `FsaReportViewProbe` (её `.cs` + довески), sha `BecquerelMonitor.exe` и
   `FsaReportViewProbe.exe` против заверённых. Протух — код 3 с находками, а не
   прогон старым кодом (`A77`: проверять то, что БУДЕТ ИСПОЛЬЗОВАНО). Чужая
   незакоммиченная правка `.cs` приложения в дереве — законный код 3.
2. Прогон `FsaReportViewProbe.exe --spectrum=<спектр с рядом Th-232>
   --control=<спектр без ряда> --out=<временный каталог>` (пара корпуса по умолчанию:
   `AS80_Th232Medal` / `AS80_Cs137_0cm`; снимки — во временный каталог системы,
   снимается после прогона, `--keep` оставляет; в дерево не пишется ничего).
3. Приговор — ТРИ признака разом: код пробы 0, последняя строка «ВСЕ СОШЛИСЬ» и ни
   одной строки, начинающейся на «  ⛔» (два пробела). Расхождение любого из трёх —
   отказ с печатью строк ⛔ и хвоста вывода. ⚠ Строки «   ⛔ обрезано / наложение /
   чужой шрифт» с ТРЕМЯ пробелами — подброшенные дефекты положительных контролей
   самой пробы (раздел 12), не отказ; их сторож не считает.

## Положительный контроль — `--selftest` (обе стороны)

  (в) штатный прогон — ОБЯЗАН пройти (сторож, отказывающий всегда, бесполезен так
      же, как молчащий всегда);
  (а) каталог проб подменён пустым — ОБЯЗАН дать код 3 со словом причины;
  (б) подброшенный неверный вход: `--control` = тот же спектр С РЯДОМ Th-232 — проба
      ОБЯЗАНА отказать (раздел 8: «без ряда: родители недоступны» — а они доступны),
      сторож — код 1 с её строками ⛔ («НЕ СОШЛОСЬ: 2», замер П105 18.09.2026);
  (г) спектра нет на диске — проба отказывает кодом 2 («мерить нечем»), сторож — код 2
      со словами пробы.

  python tools/check_fsa_report_view.py [--probes=<каталог проб>] [--spectrum=<xml>]
                                        [--control=<xml>] [--skip-freshness] [--keep]
                                        [--selftest]

  --probes=         каталог проб вместо `tools/effmaker/probes/build`
                    (иначе `BQ_FSA_REPORT_VIEW_PROBES`);
  --spectrum=/--control=  другая пара (у `--spectrum` обязан быть ряд Th-232 и
                    состав NucBase+равновесие, у `--control` — ряда быть не должно);
  --skip-freshness  НЕ судить свежесть каталога (только стенд полосы, где каталог
                    собран из ДРУГОГО дерева — worktree; в приёмку не годится);
  --keep            оставить снимки пробы (путь печатается);
  --selftest        положительный контроль (см. выше), ~2 мин.

Коды возврата:
  0 — проба прошла: код 0, «ВСЕ СОШЛИСЬ», строк ⛔ нет (или самопроверка прошла);
  1 — проба нашла расхождения / упала / вывод не сходится с кодом; самопроверка не прошла;
  2 — стенд: нет спектра или контроля на диске, проба отказала кодом 2 («мерить нечем»);
  3 — каталог проб не найден, не заверен или протух (находки `T226` напечатаны).
"""

import argparse
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROBES_DEFAULT = os.path.join(REPO, u'tools', u'effmaker', u'probes', u'build')
APPWD_PLAN = os.path.join(REPO, u'tools', u'CORPUS', u'scripts', u'appwd_plan.ps1')
BUILD_HINT = u'   собрать: pwsh tools\\effmaker\\probes\\build_all.ps1 -Bin BecquerelMonitor\\bin\\Debug_Codex'
PROBE = u'FsaReportViewProbe.exe'
# Набор исходников, от которого зависит приговор (`T226`): ключ `each.*` заверения.
PROBE_KEY = u'fsareportviewprobe'
SPECTRA = os.path.join(REPO, u'tools', u'CORPUS', u'corpus', u'spectra')
SPECTRUM_DEFAULT = os.path.join(SPECTRA, u'AS80_Th232Medal.xml')
CONTROL_DEFAULT = os.path.join(SPECTRA, u'AS80_Cs137_0cm.xml')
VERDICT_OK = u'ВСЕ СОШЛИСЬ'
# Отказ пробы: «  ⛔» с ДВУМЯ пробелами (`Same`, `Denies`, `Control<T>`, `WaitIdle`);
# три пробела — печать подброшенного дефекта положительного контроля раздела 12.
RE_RED = re.compile(u'^  ⛔')
RE_OK = re.compile(u'^  ok  ')


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


# ── каталог проб и его свежесть (T226) ──────────────────────────────────────

def find_probes(explicit):
    for candidate in (explicit, os.environ.get('BQ_FSA_REPORT_VIEW_PROBES')):
        if candidate:
            return os.path.abspath(candidate)
    return PROBES_DEFAULT


def find_pwsh():
    for name in (u'pwsh', u'powershell'):
        path = shutil.which(name)
        if path:
            return path
    return None


def freshness(probes):
    u"""Находки сторожа свежести — ТЕМИ ЖЕ функциями, что судят оснастку корпуса
    (`appwd_plan.ps1`), приговор сужен до набора приложения, набора одной пробы и
    sha двух exe. Возвращает (bad, note, err): `err` — сторож не отработал, и это
    тоже отказ, а не «проверено» (`T69`)."""
    pwsh = find_pwsh()
    if pwsh is None:
        return [], [], u'нет pwsh/powershell — сторож свежести (appwd_plan.ps1) позвать нечем'
    if not os.path.isfile(APPWD_PLAN):
        return [], [], u'нет %s — сторож свежести позвать нечем' % APPWD_PLAN
    script = u"""
$ErrorActionPreference = 'Stop'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
. '%(plan)s'
$repo = '%(repo)s'
$probes = '%(probes)s'
$key = '%(key)s'
$bad = [System.Collections.Generic.List[string]]::new()
$note = [System.Collections.Generic.List[string]]::new()
$rec = Get-AppWdSourceRecord -Repo $repo
foreach ($b in $rec.Bad) { $bad.Add($b) }
$st = Get-AppWdStampSources -Wd $probes
if (-not $st) {
    $bad.Add('отметка .appwd.json не читается как запись набора исходников (нет, не разбирается или писана другим образцом отпечатка) — пересобрать build_all.ps1')
} else {
    $d = Compare-AppWdSourceMap -Now $rec.App.Map -Was $st.app.files
    $moved = @($d.Changed) + @($d.Added) + @($d.Removed)
    if ($moved.Count -gt 0) {
        $msg = ('НАБОР ИСХОДНИКОВ ПРИЛОЖЕНИЯ РАЗОШЁЛСЯ С ЗАВЕРЕННЫМ (T226/T41): изменено {0}, добавлено {1}, удалено {2}: ' -f @($d.Changed).Count, @($d.Added).Count, @($d.Removed).Count)
        $msg += ((@($moved) | Sort-Object | Select-Object -First 6) -join ', ')
        if ($moved.Count -gt 6) { $msg += (' … и ещё {0}' -f ($moved.Count - 6)) }
        $bad.Add($msg)
    }
    $pbApp = Join-Path $probes 'BecquerelMonitor.exe'
    $certApp = if ($st.PSObject.Properties['binaries'] -and $st.binaries.PSObject.Properties['probeApp']) { [string]$st.binaries.probeApp } else { '' }
    if (-not $certApp) { $bad.Add('в заверении нет sha приложения рядом с пробами (binaries.probeApp) — отметка старого образца, пересобрать') }
    elseif (-not (Test-Path -LiteralPath $pbApp)) { $bad.Add('нет BecquerelMonitor.exe в каталоге проб') }
    elseif ($certApp -ne (Get-AppWdSha256 -Path $pbApp)) { $bad.Add('ПРИЛОЖЕНИЕ РЯДОМ С ПРОБАМИ ПОДМЕНЕНО ПОСЛЕ ЗАВЕРЕНИЯ (T138/T233): sha не тот, что заверён') }
    $set = $rec.Each[$key]
    if (-not $set) { $bad.Add(('в дереве нет пробы {0} (tools/effmaker/probes) — сторожу нечем работать' -f $key)) }
    else {
        $wasFp = if ($st.PSObject.Properties['each'] -and $st.each.PSObject.Properties[$key]) { [string]$st.each.$key } else { '' }
        if (-not $wasFp) { $bad.Add(('в заверении нет набора {0} (each.{0}) — пересобрать build_all.ps1' -f $key)) }
        elseif ($wasFp -ne $set.Fp) {
            $dp = Compare-AppWdSourceMap -Now $rec.Probes.Map -Was $st.probes.files
            $mine = @(@($dp.Changed) + @($dp.Added) + @($dp.Removed) | Where-Object { $set.Rel -contains $_ })
            $bad.Add(('НАБОР {0} (её .cs + довески) РАЗОШЁЛСЯ С ЗАВЕРЕННЫМ (T226): заверено {1} -> в дереве {2}; изменились: {3}' -f $set.Name, $wasFp.Substring(0, 12), $set.Fp.Substring(0, 12), (($mine | Sort-Object | Select-Object -First 6) -join ', ')))
        }
        $exeP = Join-Path $probes ([System.IO.Path]::GetFileNameWithoutExtension($set.Name) + '.exe')
        $certP = if ($st.PSObject.Properties['binaries'] -and $st.binaries.PSObject.Properties['probes'] -and $st.binaries.probes.PSObject.Properties[$key]) { [string]$st.binaries.probes.$key } else { '' }
        if (-not $certP) { $bad.Add(('в заверении нет sha {0}.exe (binaries.probes) — пересобрать build_all.ps1' -f $key)) }
        elseif (-not (Test-Path -LiteralPath $exeP)) { $bad.Add(('нет {0} в каталоге проб' -f $exeP)) }
        elseif ($certP -ne (Get-AppWdSha256 -Path $exeP)) { $bad.Add(('{0} ПОДМЕНЁН ПОСЛЕ ЗАВЕРЕНИЯ КАТАЛОГА (T138/T233)' -f [System.IO.Path]::GetFileName($exeP))) }
        $note.Add(('сверено: набор приложения {0} ({1} файлов); {2} {3} ({4} файлов); sha двух exe' -f $rec.App.Fp.Substring(0, 12), $rec.App.N, $set.Name, $set.Fp.Substring(0, 12), @($set.Rel).Count))
    }
}
$out = [ordered]@{ bad = @($bad); note = @($note) }
'@@JSON@@' + ($out | ConvertTo-Json -Depth 4 -Compress)
""" % {u'plan': APPWD_PLAN.replace(u"'", u"''"), u'repo': REPO.replace(u"'", u"''"),
       u'probes': probes.replace(u"'", u"''"), u'key': PROBE_KEY}
    env = dict(os.environ)
    env['OS'] = 'Windows_NT'
    try:
        proc = subprocess.run([pwsh, u'-NoProfile', u'-NonInteractive', u'-Command', script],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env, cwd=REPO)
    except OSError as e:
        return [], [], u'сторож свежести не запустился: %s' % e
    out = proc.stdout.decode('utf-8', 'replace')
    m = re.search(r'@@JSON@@(\{.*\})\s*$', out, re.S)
    if not m:
        tail = u'\n'.join(u'      ' + l for l in out.strip().splitlines()[-12:])
        return [], [], (u'сторож свежести не отработал (код %d), ответа без поля bad нет:\n%s'
                        % (proc.returncode, tail))
    data = json.loads(m.group(1))
    bad = data.get(u'bad') or []
    note = data.get(u'note') or []
    if isinstance(bad, str):
        bad = [bad]
    if isinstance(note, str):
        note = [note]
    return list(bad), list(note), None


# ── проба ────────────────────────────────────────────────────────────────────

def run_probe(probes, spectrum, control, out_dir):
    u"""`FsaReportViewProbe --spectrum= --control= --out=` из каталога проб; пути
    абсолютные (конфиг приложение читает от каталога exe, не от cwd). Возвращает
    (код, текст, секунды). Проба ставит `Console.OutputEncoding = UTF8`."""
    exe = os.path.join(probes, PROBE)
    cmd = [exe, u'--spectrum=' + os.path.abspath(spectrum), u'--control=' + os.path.abspath(control),
           u'--out=' + os.path.abspath(out_dir)]
    t0 = time.time()
    proc = subprocess.run(cmd, cwd=probes, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    return proc.returncode, proc.stdout.decode('utf-8', 'replace'), time.time() - t0


def judge(code, text):
    u"""Приговор по трём признакам разом. Возвращает (код сторожа, причина, строки ⛔,
    последняя строка, число ok)."""
    lines = text.splitlines()
    red = [l for l in lines if RE_RED.match(l)]
    oks = sum(1 for l in lines if RE_OK.match(l))
    last = u''
    for l in reversed(lines):
        if l.strip():
            last = l.strip()
            break
    if code == 2:
        return 2, u'проба отказала кодом 2 («мерить нечем»): нет входа или геометрии', red, last, oks
    if code == 0 and last == VERDICT_OK and not red:
        return 0, u'', red, last, oks
    if code == 0:
        return 1, (u'проба дала код 0, но вывод с ним не сходится: последняя строка «%s», строк ⛔ %d'
                   % (last, len(red))), red, last, oks
    if code == 1:
        return 1, u'проба нашла расхождения: «%s», строк ⛔ %d' % (last, len(red)), red, last, oks
    return 1, (u'проба упала кодом %d (0x%08X) — приговора нет' % (code, code & 0xFFFFFFFF)), red, last, oks


def rel(path):
    u"""Путь относительно дерева, если он внутри; иначе как есть."""
    try:
        if os.path.commonpath([os.path.abspath(path), REPO]) == REPO:
            return os.path.relpath(path, REPO)
    except ValueError:
        pass
    return path


def run_check(probes, spectrum, control, keep=False, skip_freshness=False, out=None):
    u"""Полный ход сторожа. Возвращает код возврата; печатает через `out`."""
    out = out or Out()
    say = out.say
    say(u'    == сторож окна отчёта FSA: FsaReportViewProbe на паре корпуса ==')
    say(u'      каталог проб : %s' % probes)
    say(u'      спектр (ряд) : %s' % rel(spectrum))
    say(u'      контроль     : %s' % rel(control))

    exe = os.path.join(probes, PROBE)
    if not os.path.isdir(probes) or not os.path.isfile(exe) or not os.path.isfile(os.path.join(probes, u'BecquerelMonitor.exe')):
        say(u'      ⛔ ПРОБЫ НЕТ: %s (%s / BecquerelMonitor.exe)' % (probes, PROBE))
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

    for label, path in ((u'спектр', spectrum), (u'контроль', control)):
        if not os.path.isfile(path):
            say(u'      ⛔ СТЕНД: нет файла (%s): %s' % (label, path))
            return 2

    out_dir = tempfile.mkdtemp(prefix=u'bq_fsa_report_view_')
    try:
        code, text, secs = run_probe(probes, spectrum, control, out_dir)
        verdict, why, red, last, oks = judge(code, text)
        say(u'      проба: код %d, %.1f с, строк ok %d, строк ⛔ %d, последняя строка «%s»'
            % (code, secs, oks, len(red), last))
        if verdict == 0:
            say(u'    СОШЛОСЬ: окно отчёта FSA — проба прошла (%s)' % VERDICT_OK)
            return 0
        say(u'      ⛔ %s' % why)
        for l in red[:40]:
            say(u'      %s' % l.rstrip())
        if len(red) > 40:
            say(u'      … и ещё %d строк ⛔' % (len(red) - 40))
        if verdict == 2 or not red:
            # Отказ словами или падение — причина в хвосте вывода, не в строках ⛔.
            tail = [l for l in text.splitlines() if l.strip()][-12:]
            say(u'      хвост вывода пробы:')
            for l in tail:
                say(u'        %s' % l.rstrip())
        return verdict
    finally:
        if keep:
            say(u'      снимки пробы оставлены: %s' % out_dir)
        else:
            shutil.rmtree(out_dir, ignore_errors=True)


# ── положительный контроль ───────────────────────────────────────────────────

def selftest(probes, spectrum, control):
    u"""Обе стороны: штатный прогон обязан пройти; пустой каталог проб — код 3;
    контроль С РЯДОМ (тот же спектр) — код 1 со строками ⛔; спектра нет — код 2."""
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

    print(u'  == самопроверка сторожа окна отчёта FSA ==')

    o = Out(silent=True)
    code = run_check(probes, spectrum, control, out=o)
    record(u'(в) штатный прогон проходит', 0, code, o.buf, [VERDICT_OK])

    empty = tempfile.mkdtemp(prefix=u'bq_fsa_report_view_empty_')
    try:
        o = Out(silent=True)
        code = run_check(empty, spectrum, control, out=o)
        record(u'(а) пустой каталог проб — отказ со словом причины', 3, code, o.buf, [u'ПРОБЫ НЕТ'])
    finally:
        shutil.rmtree(empty, ignore_errors=True)

    o = Out(silent=True)
    code = run_check(probes, spectrum, spectrum, out=o)
    record(u'(б) контроль С РЯДОМ Th-232 — проба отказывает, сторож код 1', 1, code, o.buf,
           [u'проба нашла расхождения', u'НЕ СОШЛОСЬ', u'  ⛔'])

    o = Out(silent=True)
    code = run_check(probes, spectrum, os.path.join(SPECTRA, u'NO_SUCH_SPECTRUM.xml'), out=o)
    record(u'(г) спектра нет на диске — код 2 «мерить нечем»', 2, code, o.buf, [u'СТЕНД: нет файла'])

    bad = [r for r in rows if not r[3]]
    if bad:
        print(u'  САМОПРОВЕРКА НЕ ПРОШЛА: %d из %d' % (len(bad), len(rows)))
        return 1
    print(u'  САМОПРОВЕРКА ПРОШЛА: %d из %d — сторож и отказывает, и пропускает там, где должен' % (len(rows), len(rows)))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=u'сторож окна отчёта FSA: приёмка FsaReportViewProbe', add_help=True)
    ap.add_argument(u'--probes', default=None)
    ap.add_argument(u'--spectrum', default=SPECTRUM_DEFAULT)
    ap.add_argument(u'--control', default=CONTROL_DEFAULT)
    ap.add_argument(u'--skip-freshness', action=u'store_true')
    ap.add_argument(u'--keep', action=u'store_true')
    ap.add_argument(u'--selftest', action=u'store_true')
    args = ap.parse_args(argv)
    probes = find_probes(args.probes)
    spectrum = os.path.abspath(args.spectrum)
    control = os.path.abspath(args.control)
    if args.selftest:
        return selftest(probes, spectrum, control)
    return run_check(probes, spectrum, control, keep=args.keep, skip_freshness=args.skip_freshness)


if __name__ == '__main__':
    sys.exit(main())
