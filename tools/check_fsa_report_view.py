#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ОКНА ОТЧЁТА FSA (`FSAReportView`): приёмка ЧЕТЫРЁХ проб окна на спектрах
корпуса — `FsaReportViewProbe` (блок «Качество разбора»: χ²/ndf, σ×, пометки, строки
привязки шкалы `AMBER17`; состав строк по `Tag.Kind`, один результат на график и
таблицу, родители/дочерние, повторяемость, снимки en/ru без обрезанных подписей),
`FsaSelectionProbeF16` (выделение компонента на графике `A246`; подписи блока видны
целиком `A247`/`AMBER214`), `FsaResidualProbeF21` (галочка ленты невязки `A248`: проводка,
пиксели кадра, числа при обеих позициях, место в группе «Отрисовка» `A265`/`AMBER222`)
и `FsaInputCheckProbeP195` (заверение входа: плотность пробы против сцены `AMBER159`,
кривая вне линий `AMBER157`, разбор без матрицы `AMBER156`).

## Зачем

Пробу `FsaReportViewProbe` собирал `build_all.ps1`, но НЕ ЗАПУСКАЛ никто: ни один
`check_*.py` её не звал. Итог измерен: 11.09.2026 окно получило строку «Привязка
шкалы, опор» (`AMBER17`, решение Amber «ВКЛ умолчанием — привязка работает сама,
слой показывает опоры»), проба ждала ровно три пометки и была красна «пометок 4
вместо 3» СЕМЬ ДНЕЙ — заметила это полоса П104 18.09.2026, запустив пробу руками
по другому поводу; починила П105 тем же днём. Признак отказа без читателя — та же
беда, ради которой заведён сам `check_all.py` (`T220`). Здесь — читатель этой
пробы.

⛔ (`T269`, П251 14.10.2026) Та же беда повторилась с тремя соседками: F16, F21 и
P195 не звал никто, и все три были красны на чистом дереве — F16 с `AMBER156`
(длинные подписи блока ПЕРЕНОСЯТСЯ, а проба мерила их в одну строку), F21 с `A265`
06.09.2026 (галочку вынесли в группу «Отрисовка», проба ждала её шестой в расчётной;
сборка кадра не знала значков опор `AMBER17`), P195 с первого дня вне оснастки
корпуса (сцене (1) нужна матрица, а в каталоге проб склада нет). Найдено полосой
`AMBER217` 07.10.2026 случайно. Доведены до зелёного и поставлены сюда же: предмет
у всех четырёх один — окно отчёта FSA.

## Что делает

1. Свежесть штатного каталога проб (`T226`) — ТЕМИ ЖЕ функциями `appwd_plan.ps1`,
   что судят оснастку корпуса (`Get-AppWdSourceRecord`, `Get-AppWdStampSources`,
   `Compare-AppWdSourceMap`, `Get-AppWdSha256`; своей копии отпечатка нет — `T61`):
   заверение `.appwd.json`, набор исходников ПРИЛОЖЕНИЯ (по `.csproj`), набор
   КАЖДОЙ из четырёх проб (её `.cs` + довески), sha `BecquerelMonitor.exe` и exe
   проб против заверённых. Протух — код 3 с находками, а не прогон старым кодом
   (`A77`: проверять то, что БУДЕТ ИСПОЛЬЗОВАНО). Чужая незакоммиченная правка
   `.cs` приложения в дереве — законный код 3.
2. Прогон четырёх проб из каталога проб (параллельно; каждая пишет снимки во
   временный каталог системы, снимается после прогона, `--keep` оставляет; в
   дерево и в каталог проб не пишется ничего):
     report — `FsaReportViewProbe --spectrum=AS80_Th232Medal --control=AS80_Cs137_0cm`;
     f16    — `FsaSelectionProbeF16 --spectrum=AS80_Th232Medal`;
     f21    — `FsaResidualProbeF21 --spectrum=AS80_Th232Medal`;
     p195   — `FsaInputCheckProbeP195 --spectrum=G1S16_K40_Mar
              --store=tools/CORPUS/corpus/geometries/response` (склад матриц корпуса
              по guid кривой — вне git, §1.5 `tools/CORPUS/README.md`; матрица идёт
              пробе В ПАМЯТЬ, склад не пишется).
3. Приговор каждой — ТРИ признака разом: код пробы 0, последняя строка «ВСЕ
   СОШЛИСЬ» и ни одной строки отказа (у `FsaReportViewProbe` и P195 — «  ⛔» с двумя
   пробелами, у F16/F21 — «  НЕТ »). Расхождение любого из трёх у любой пробы —
   отказ с печатью её строк отказа и хвоста вывода. ⚠ Строки «   ⛔ обрезано /
   наложение / чужой шрифт» с ТРЕМЯ пробелами — подброшенные дефекты положительных
   контролей самой `FsaReportViewProbe` (раздел 12), не отказ; их сторож не считает.

## Положительный контроль — `--selftest` (обе стороны)

  (в) штатный прогон четырёх проб — ОБЯЗАН пройти (сторож, отказывающий всегда,
      бесполезен так же, как молчащий всегда);
  (а) каталог проб подменён пустым — ОБЯЗАН дать код 3 со словом причины;
  (б) подброшенный неверный вход report: `--control` = тот же спектр С РЯДОМ Th-232 —
      проба ОБЯЗАНА отказать (раздел 8: «без ряда: родители недоступны» — а они
      доступны), сторож — код 1 с её строками ⛔ («НЕ СОШЛОСЬ: 2», замер П105 18.09.2026);
  (г) спектра нет на диске — проба отказывает кодом 2 («мерить нечем»), сторож — код 2
      со словами пробы;
  (д) F16 выделяет слой, которого в разборе нет (`--layer=`) — проба ОБЯЗАНА
      отказать (проводка и пиксели), сторож — код 1 с её строками «НЕТ»;
  (е) F21 сверяет кадр с опорными кадрами из ПУСТОГО каталога (`--against=`) —
      проба ОБЯЗАНА отказать («опорного кадра нет»), сторож — код 1;
  (ж) P195 со складом матриц из ПУСТОГО каталога — сцене (1) мерить нечем, проба
      отказывает кодом 2, сторож — код 2 со словами пробы «МЕРИТЬ НЕЧЕМ».

  python tools/check_fsa_report_view.py [--probes=<каталог проб>] [--spectrum=<xml>]
                                        [--control=<xml>] [--store=<склад по guid>]
                                        [--only=report,f16,f21,p195]
                                        [--skip-freshness] [--keep] [--selftest]

  --probes=         каталог проб вместо `tools/effmaker/probes/build`
                    (иначе `BQ_FSA_REPORT_VIEW_PROBES`);
  --spectrum=/--control=  другая пара для report, F16 и F21 (у `--spectrum` обязан
                    быть ряд Th-232 и состав NucBase+равновесие, у `--control` —
                    ряда быть не должно);
  --store=          склад матриц по guid кривой для P195 (умолчание — склад корпуса);
  --only=           гнать только названные пробы (свежесть судится по ним же);
  --skip-freshness  НЕ судить свежесть каталога проб (только стенд полосы, где каталог
                    собран из ДРУГОГО дерева — worktree; в приёмку не годится);
  --keep            оставить снимки проб (путь печатается);
  --selftest        положительный контроль (см. выше), ~3 мин на свободной машине;
                    свежесть судится один раз, в (в); `--skip-freshness` снимает и её.

Коды возврата:
  0 — все пробы прошли: код 0, «ВСЕ СОШЛИСЬ», строк отказа нет (или самопроверка прошла);
  1 — проба нашла расхождения / упала / вывод не сходится с кодом; самопроверка не прошла;
  2 — стенд: нет спектра, контроля или склада матриц на диске, проба отказала кодом 2
      («мерить нечем»);
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
SPECTRA = os.path.join(REPO, u'tools', u'CORPUS', u'corpus', u'spectra')
SPECTRUM_DEFAULT = os.path.join(SPECTRA, u'AS80_Th232Medal.xml')
CONTROL_DEFAULT = os.path.join(SPECTRA, u'AS80_Cs137_0cm.xml')
# P195: спектр понятной части с геометрией маринелли (ρ сцены 0.66) и склад матриц
# корпуса по guid кривой — его читает разбор (`ResponseMatrixStore`), вне git.
P195_SPECTRUM = os.path.join(SPECTRA, u'G1S16_K40_Mar.xml')
STORE_DEFAULT = os.path.join(REPO, u'tools', u'CORPUS', u'corpus', u'geometries', u'response')
VERDICT_OK = u'ВСЕ СОШЛИСЬ'
# Отказ `FsaReportViewProbe`: «  ⛔» с ДВУМЯ пробелами (`Same`, `Denies`, `Control<T>`,
# `WaitIdle`); три пробела — печать подброшенного дефекта положительного контроля
# раздела 12. У P195 `Same` печатает «  ⛔  …»/«  OK  …», у F16/F21 — «  НЕТ  …»/«  ok   …».
RE_RED_SIGN = re.compile(u'^  ⛔')
RE_RED_NET = re.compile(u'^  НЕТ ')
RE_OK_LOWER = re.compile(u'^  ok  ')
RE_OK_UPPER = re.compile(u'^  OK  ')


class Probe(object):
    u"""Одна проба окна: имя для `--only`, exe, ключ набора в заверении (`T226`),
    образцы строк отказа и «ok», что судит (для печати)."""

    def __init__(self, name, exe, red, ok, what):
        self.name = name
        self.exe = exe
        self.key = os.path.splitext(exe)[0].lower()
        self.red = red
        self.ok = ok
        self.what = what


PROBES = [
    Probe(u'report', u'FsaReportViewProbe.exe', RE_RED_SIGN, RE_OK_LOWER,
          u'блок качества, привязка шкалы (AMBER17), состав строк, родители/дочерние, снимки'),
    Probe(u'f16', u'FsaSelectionProbeF16.exe', RE_RED_NET, RE_OK_LOWER,
          u'выделение компонента на графике (A246), подписи блока целиком (A247/AMBER214)'),
    Probe(u'f21', u'FsaResidualProbeF21.exe', RE_RED_NET, RE_OK_LOWER,
          u'галочка ленты невязки (A248): проводка, пиксели, числа, место в «Отрисовке» (A265/AMBER222)'),
    Probe(u'p195', u'FsaInputCheckProbeP195.exe', RE_RED_SIGN, RE_OK_UPPER,
          u'заверение входа: плотность пробы (AMBER159), кривая (AMBER157), без матрицы (AMBER156)'),
]
PROBE_NAMES = [p.name for p in PROBES]


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


def freshness(probes, keys):
    u"""Находки сторожа свежести — ТЕМИ ЖЕ функциями, что судят оснастку корпуса
    (`appwd_plan.ps1`), приговор сужен до набора приложения, наборов названных проб и
    sha их exe и приложения. Возвращает (bad, note, err): `err` — сторож не отработал,
    и это тоже отказ, а не «проверено» (`T69`)."""
    pwsh = find_pwsh()
    if pwsh is None:
        return [], [], u'нет pwsh/powershell — сторож свежести (appwd_plan.ps1) позвать нечем'
    if not os.path.isfile(APPWD_PLAN):
        return [], [], u'нет %s — сторож свежести позвать нечем' % APPWD_PLAN
    keys_ps = u', '.join(u"'%s'" % k for k in keys)
    script = u"""
$ErrorActionPreference = 'Stop'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
. '%(plan)s'
$repo = '%(repo)s'
$probes = '%(probes)s'
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
    $sets = [System.Collections.Generic.List[string]]::new()
    foreach ($key in @(%(keys)s)) {
        $set = $rec.Each[$key]
        if (-not $set) { $bad.Add(('в дереве нет пробы {0} (tools/effmaker/probes) — сторожу нечем работать' -f $key)); continue }
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
        $sets.Add(('{0} {1} ({2} файлов)' -f $set.Name, $set.Fp.Substring(0, 12), @($set.Rel).Count))
    }
    $note.Add(('сверено: набор приложения {0} ({1} файлов); {2}; sha exe' -f $rec.App.Fp.Substring(0, 12), $rec.App.N, ($sets -join '; ')))
}
$out = [ordered]@{ bad = @($bad); note = @($note) }
'@@JSON@@' + ($out | ConvertTo-Json -Depth 4 -Compress)
""" % {u'plan': APPWD_PLAN.replace(u"'", u"''"), u'repo': REPO.replace(u"'", u"''"),
       u'probes': probes.replace(u"'", u"''"), u'keys': keys_ps}
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


# ── пробы ────────────────────────────────────────────────────────────────────

def probe_args(probe, plan, out_dir):
    u"""Доводы пробы; пути абсолютные (конфиг приложение читает от каталога exe, не от cwd)."""
    if probe.name == u'report':
        return [u'--spectrum=' + plan['spectrum'], u'--control=' + plan['control'],
                u'--out=' + os.path.join(out_dir, u'report')]
    if probe.name == u'f16':
        args = [u'--spectrum=' + plan['spectrum'], u'--out=' + os.path.join(out_dir, u'f16')]
        if plan.get('f16_layer'):
            args.append(u'--layer=' + plan['f16_layer'])
        return args
    if probe.name == u'f21':
        args = [u'--spectrum=' + plan['spectrum'], u'--out=' + os.path.join(out_dir, u'f21')]
        if plan.get('f21_against'):
            args.append(u'--against=' + plan['f21_against'])
        return args
    return [u'--spectrum=' + plan['p195_spectrum'], u'--store=' + plan['store']]


def inputs_of(probe, plan):
    u"""Входные файлы и каталоги пробы — (что, путь, каталог?) — для отказа «стенд»."""
    if probe.name == u'report':
        return [(u'спектр', plan['spectrum'], False), (u'контроль', plan['control'], False)]
    if probe.name in (u'f16', u'f21'):
        return [(u'спектр', plan['spectrum'], False)]
    return [(u'спектр P195', plan['p195_spectrum'], False),
            (u'склад матриц корпуса (вне git, tools/CORPUS/README.md §1.5)', plan['store'], True)]


def judge(probe, code, text):
    u"""Приговор по трём признакам разом. Возвращает (код сторожа, причина, строки отказа,
    последняя строка, число ok)."""
    lines = text.splitlines()
    red = [l for l in lines if probe.red.match(l)]
    oks = sum(1 for l in lines if probe.ok.match(l))
    last = u''
    for l in reversed(lines):
        if l.strip():
            last = l.strip()
            break
    if code == 2:
        return 2, u'проба отказала кодом 2 («мерить нечем»): нет входа, геометрии или матрицы', red, last, oks
    if code == 0 and last == VERDICT_OK and not red:
        return 0, u'', red, last, oks
    if code == 0:
        return 1, (u'проба дала код 0, но вывод с ним не сходится: последняя строка «%s», строк отказа %d'
                   % (last, len(red))), red, last, oks
    if code == 1:
        return 1, u'проба нашла расхождения: «%s», строк отказа %d' % (last, len(red)), red, last, oks
    return 1, (u'проба упала кодом %d (0x%08X) — приговора нет' % (code, code & 0xFFFFFFFF)), red, last, oks


def rel(path):
    u"""Путь относительно дерева, если он внутри; иначе как есть."""
    try:
        if os.path.commonpath([os.path.abspath(path), REPO]) == REPO:
            return os.path.relpath(path, REPO)
    except ValueError:
        pass
    return path


def make_plan(spectrum=SPECTRUM_DEFAULT, control=CONTROL_DEFAULT, store=STORE_DEFAULT,
              p195_spectrum=P195_SPECTRUM, f16_layer=None, f21_against=None):
    return {'spectrum': os.path.abspath(spectrum), 'control': os.path.abspath(control),
            'store': os.path.abspath(store), 'p195_spectrum': os.path.abspath(p195_spectrum),
            'f16_layer': f16_layer, 'f21_against': f21_against}


def run_check(probes, plan, only=None, keep=False, skip_freshness=False, out=None):
    u"""Полный ход сторожа. Возвращает код возврата; печатает через `out`."""
    out = out or Out()
    say = out.say
    chosen = [p for p in PROBES if only is None or p.name in only]
    say(u'    == сторож окна отчёта FSA: %d проб окна на спектрах корпуса ==' % len(chosen))
    say(u'      каталог проб : %s' % probes)
    for p in chosen:
        say(u'      %-6s : %s — %s' % (p.name, p.exe, p.what))

    missing = [p.exe for p in chosen if not os.path.isfile(os.path.join(probes, p.exe))]
    if not os.path.isdir(probes) or missing or not os.path.isfile(os.path.join(probes, u'BecquerelMonitor.exe')):
        say(u'      ⛔ ПРОБЫ НЕТ: %s (%s / BecquerelMonitor.exe)' % (probes, u', '.join(missing) or u'—'))
        say(BUILD_HINT)
        return 3

    if skip_freshness:
        say(u'      ⚠ свежесть каталога проб НЕ судится (--skip-freshness): стенд, не приёмка')
    else:
        t0 = time.time()
        bad, note, err = freshness(probes, [p.key for p in chosen])
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

    for p in chosen:
        for label, path, is_dir in inputs_of(p, plan):
            if not (os.path.isdir(path) if is_dir else os.path.isfile(path)):
                say(u'      ⛔ СТЕНД: нет %s (%s): %s' % (u'каталога' if is_dir else u'файла', label, rel(path)))
                return 2

    out_dir = tempfile.mkdtemp(prefix=u'bq_fsa_report_view_')
    try:
        # Пробы независимы (каждая пишет только в свой подкаталог временного
        # каталога; каталог проб и дерево не пишутся — замерено П251) — идут разом.
        t0 = time.time()
        running = []
        for p in chosen:
            cmd = [os.path.join(probes, p.exe)] + probe_args(p, plan, out_dir)
            running.append((p, cmd, subprocess.Popen(cmd, cwd=probes, stdout=subprocess.PIPE,
                                                      stderr=subprocess.STDOUT)))
        results = []
        for p, cmd, proc in running:
            data, _ = proc.communicate()
            results.append((p, cmd, proc.returncode, data.decode('utf-8', 'replace')))
        secs = time.time() - t0

        verdicts = []
        for p, cmd, code, text in results:
            verdict, why, red, last, oks = judge(p, code, text)
            verdicts.append(verdict)
            say(u'      %-6s: код %d, строк ok %d, строк отказа %d, последняя строка «%s»'
                % (p.name, code, oks, len(red), last))
            if verdict == 0:
                continue
            say(u'      ⛔ %s: %s' % (p.exe, why))
            say(u'         позвано: %s %s' % (p.exe, u' '.join(probe_args(p, plan, u'<врем.>'))))
            for l in red[:40]:
                say(u'      %s' % l.rstrip())
            if len(red) > 40:
                say(u'      … и ещё %d строк отказа' % (len(red) - 40))
            if verdict == 2 or not red:
                # Отказ словами или падение — причина в хвосте вывода, не в строках отказа.
                tail = [l for l in text.splitlines() if l.strip()][-12:]
                say(u'      хвост вывода пробы:')
                for l in tail:
                    say(u'        %s' % l.rstrip())
        say(u'      пробы шли разом %.1f с' % secs)
        # Код сторожа — худший по смыслу: расхождение (1) важнее стенда (2).
        worst = 1 if 1 in verdicts else (2 if 2 in verdicts else 0)
        if worst == 0:
            say(u'    СОШЛОСЬ: окно отчёта FSA — %d из %d проб прошли (%s)' % (len(results), len(results), VERDICT_OK))
        return worst
    finally:
        if keep:
            say(u'      снимки проб оставлены: %s' % out_dir)
        else:
            shutil.rmtree(out_dir, ignore_errors=True)


# ── положительный контроль ───────────────────────────────────────────────────

def selftest(probes, plan, skip_freshness=False):
    u"""Обе стороны: штатный прогон обязан пройти; пустой каталог проб — код 3;
    контроль С РЯДОМ (тот же спектр) — код 1; спектра нет — код 2; F16 с чужим
    слоем — код 1; F21 с пустыми опорными кадрами — код 1; P195 с пустым складом —
    код 2.

    Свежесть каталога судится ОДИН раз — в (в): плечи (б)…(ж) гонят тот же каталог
    и судят приговор проб, не его свежесть (иначе соседняя полоса, правящая `.cs`
    приложения посреди самопроверки, роняла бы их кодом 3 — замерено П251).
    `--skip-freshness` снимает и её (стенд полосы, не приёмка)."""
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
    code = run_check(probes, plan, skip_freshness=skip_freshness, out=o)
    record(u'(в) штатный прогон четырёх проб проходит', 0, code, o.buf, [u'СОШЛОСЬ', u'4 из 4'])

    empty = tempfile.mkdtemp(prefix=u'bq_fsa_report_view_empty_')
    try:
        o = Out(silent=True)
        code = run_check(empty, plan, out=o)
        record(u'(а) пустой каталог проб — отказ со словом причины', 3, code, o.buf, [u'ПРОБЫ НЕТ'])

        o = Out(silent=True)
        bad_plan = dict(plan, control=plan['spectrum'])
        code = run_check(probes, bad_plan, only=[u'report'], skip_freshness=True, out=o)
        record(u'(б) report: контроль С РЯДОМ Th-232 — проба отказывает, сторож код 1', 1, code, o.buf,
               [u'проба нашла расхождения', u'НЕ СОШЛОСЬ', u'  ⛔'])

        o = Out(silent=True)
        bad_plan = dict(plan, control=os.path.join(SPECTRA, u'NO_SUCH_SPECTRUM.xml'))
        code = run_check(probes, bad_plan, only=[u'report'], skip_freshness=True, out=o)
        record(u'(г) спектра нет на диске — код 2 «мерить нечем»', 2, code, o.buf, [u'СТЕНД: нет файла'])

        o = Out(silent=True)
        bad_plan = dict(plan, f16_layer=u'нет такого слоя T269')
        code = run_check(probes, bad_plan, only=[u'f16'], skip_freshness=True, out=o)
        record(u'(д) F16 выделяет слой, которого нет, — проба отказывает, сторож код 1', 1, code, o.buf,
               [u'проба нашла расхождения', u'НЕ СОШЛОСЬ', u'  НЕТ '])

        o = Out(silent=True)
        bad_plan = dict(plan, f21_against=empty)
        code = run_check(probes, bad_plan, only=[u'f21'], skip_freshness=True, out=o)
        record(u'(е) F21 против пустых опорных кадров — проба отказывает, сторож код 1', 1, code, o.buf,
               [u'проба нашла расхождения', u'НЕ СОШЛОСЬ', u'опорный кадр старой сборки есть'])

        o = Out(silent=True)
        bad_plan = dict(plan, store=empty)
        code = run_check(probes, bad_plan, only=[u'p195'], skip_freshness=True, out=o)
        record(u'(ж) P195 с пустым складом матриц — мерить нечем, код 2', 2, code, o.buf,
               [u'МЕРИТЬ НЕЧЕМ', u'проба отказала кодом 2'])
    finally:
        shutil.rmtree(empty, ignore_errors=True)

    bad = [r for r in rows if not r[3]]
    if bad:
        print(u'  САМОПРОВЕРКА НЕ ПРОШЛА: %d из %d' % (len(bad), len(rows)))
        return 1
    print(u'  САМОПРОВЕРКА ПРОШЛА: %d из %d — сторож и отказывает, и пропускает там, где должен' % (len(rows), len(rows)))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=u'сторож окна отчёта FSA: приёмка четырёх проб окна', add_help=True)
    ap.add_argument(u'--probes', default=None)
    ap.add_argument(u'--spectrum', default=SPECTRUM_DEFAULT)
    ap.add_argument(u'--control', default=CONTROL_DEFAULT)
    ap.add_argument(u'--store', default=STORE_DEFAULT)
    ap.add_argument(u'--only', default=None)
    ap.add_argument(u'--skip-freshness', action=u'store_true')
    ap.add_argument(u'--keep', action=u'store_true')
    ap.add_argument(u'--selftest', action=u'store_true')
    args = ap.parse_args(argv)
    probes = find_probes(args.probes)
    plan = make_plan(spectrum=args.spectrum, control=args.control, store=args.store)
    only = None
    if args.only:
        only = [s.strip() for s in args.only.split(u',') if s.strip()]
        unknown = [s for s in only if s not in PROBE_NAMES]
        if unknown:
            print(u'ОТКАЗ: неизвестные пробы в --only: %s (есть: %s)' % (u', '.join(unknown), u', '.join(PROBE_NAMES)))
            return 2
    if args.selftest:
        return selftest(probes, plan, skip_freshness=args.skip_freshness)
    return run_check(probes, plan, only=only, keep=args.keep, skip_freshness=args.skip_freshness)


if __name__ == '__main__':
    sys.exit(main())
