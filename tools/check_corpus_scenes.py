#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож СЦЕН генератора (`T261`): генератор `CorpusGeomProbe` воспроизводит
живые сцены корпуса `tools/CORPUS/corpus/geometries/*.in` С ТОЧНОСТЬЮ ДО КЛЕЙМА,
опись `index.csv` — побайтно, а матрицы живого склада `*.rmx` посчитаны на ЭТИ
сцены и штатным рецептом (клеймо при умолчаниях).

## Зачем

`check_corpus_generator.py` (`T244`) клеймит набор `build_corpus.py` — спектры,
приборы, сводку, — а генератор СЦЕН `CorpusGeomProbe.cs` в этот набор не входит
и входить не должен (клеймо `T244` пишет полная пересборка, которая сцен не
перестраивает). Поэтому подсаженный в генератор старый сосуд «из объёма» тот
сторож не видит: П72 14.09.2026 измерила — подсадка даёт «ВСЕ СОШЛИСЬ» у самого
генератора (объём-то сходится) и код 0 у `T244`, а клейма расходятся у 18 сцен
из 46 (17 маринелли + уголь), и видно это было только `stamp_check.py` руками.
До того по той же дыре диск `AS80_th_disk` (П22) жил в корпусе мимо генератора,
и полный прогон молча выкидывал его из описи. Здесь — читатель этой дыры.

## Что и как сравнивается

Сцены строятся генератором В СВОЙ ВРЕМЕННЫЙ КАТАЛОГ (`--out=<абс. путь>`;
⛔ без `--out` генератор пишет в ЖИВОЙ каталог корпуса — сторож так не зовёт
никогда). Затем для каждой сцены `MatrixStampProbe` снимает ТРИ клейма:

  L — живого `.in` при умолчаниях настроек матрицы;
  G — построенного генератором `.in` при тех же умолчаниях;
  M — записанное в живой `.rmx` (когда он есть: склад вне git).

Сцена принята, когда G == L и (нет `.rmx` или M == L). Равенство именно
клеймом, а не байтом: с `AMBER1` (08.09.2026) писатель геометрий печатает блок
зазора, а 42 сцены корпуса записаны раньше и блока не несут; клеймо от блока
не зависит по построению (`ResponseMatrix.StampView`). Байтно сравниваются
только опись `index.csv` и СОСТАВ сцен (имена файлов): сцена, которой генератор
не знает, или сцена, которой нет в корпусе, — отказ.

Расхождение M ≠ L разбирается дальше по клейму «по настройкам файла», которое
печатает та же проба: сошлось с M — матрица посчитана на эту сцену, но ДРУГИМ
РЕЦЕПТОМ (сетка/узлы/ключи не умолчания); не сошлось — матрица посчитана на
ДРУГУЮ сцену либо сменился состав клейма/версия физики.

⚠ Код возврата `MatrixStampProbe` сторож НЕ читает — только строки клейм: код
пробы отвечает за ДРУГОЕ (её самопроверка «каждый ключ физики меняет клеймо»),
и смешивать два приговора в один нельзя. Цена смешения измерена: с П37
(13.09.2026, физика 17) позитроны и рэлей ВКЛ умолчанием, а самопроверка пробы
ставила ключи в зашитое `true` и на КАЖДОЙ сцене возвращала код 1 («клеймо не
различает 2 случаев») — четыре дня; найдено и исправлено П91 17.09.2026 (ключ
теперь переворачивается против умолчания самих настроек). Нет строки «клеймо
при умолчаниях» — вот это отказ (сцена не читается), и он красный.

## Свежесть каталога проб — сторож `T226`, не обходится

Каталог проб — `tools/effmaker/probes/build` (штатный `-Out` `build_all.ps1`),
иначе `--probes=` или `BQ_CORPUS_SCENES_PROBES`. Перед прогоном каталог судится
ТЕМИ ЖЕ функциями, что судят оснастку корпуса (`appwd_plan.ps1`), и приговор
СУЖЕН до того, что этот сторож исполняет: набор исходников ПРИЛОЖЕНИЯ (по
`.csproj` — клеймо считает `ResponseMatrix` приложения, сцены строят его
`GeometryPresets`/`GeometryWriter`), наборы `CorpusGeomProbe` и
`MatrixStampProbe` (свой `.cs` + довески), sha трёх exe против заверенных.
Протухший каталог — код 3 с находками, а не прогон старым кодом (`A77`:
02.09.2026 старый бинарь прочёл новый ключ по-старому и испортил три часа
счёта молча; «проверять то, что будет использовано»).
⚠ Чужая незакоммиченная правка `.cs` приложения в дереве — законный код 3.

## Положительный контроль — `--selftest` (обе стороны, ничего в дереве не трогая)

  (в) чистый набор — ОБЯЗАН дать зелёный (сторож, красный всегда, бесполезен
      так же, как зелёный всегда);
  (а) подсадка СОСУДА: в копии корпуса у `G1S_mar1l_oisn06_057_p16` сосуд
      возвращён к прежнему «из объёма» (Ø135.2 × 104, колодец Ø76 × 70, слой
      100 мм — тот самый, что П72 подсаживала в генератор; клеймо подсаженной
      сцены `phys=18;2c366cb9a0b76b09…` — то же, что П72 сняла с генератора со
      старым сосудом, `handover/p72-t258-t259/stamp_check_oldvessel.txt`) —
      ОБЯЗАН отказать на этой сцене двумя строками (G ≠ L и M ≠ L) и принять
      контрольную;
  (б) подсадка МАТРИЦЫ: `.rmx` точки 5 мм положен под именем точки 25 см —
      ОБЯЗАН отказать «матрица посчитана на другую сцену»;
  (г) подсадка ОПИСИ: из `index.csv` снята строка — ОБЯЗАН отказать по описи;
  (д) сцена мимо генератора: в корпус подложена `X_stray.in` (случай П22) —
      ОБЯЗАН отказать «генератор не знает сцены».

  python tools/check_corpus_scenes.py [--probes=<каталог проб>] [--live=<geometries>]
                                      [--only=<сцена>[,…]] [--jobs=N] [--keep=<каталог>]
                                      [--skip-freshness] [--selftest] [--quiet] [--csv=<файл>]

  --live=          каталог живых сцен вместо `tools/CORPUS/corpus/geometries`
                   (стенд полосы: копия корпуса);
  --only=          снимать клейма только у названных сцен (опись и состав
                   судятся всегда целиком);
  --jobs=N         параллельных запусков пробы клейма (умолчание min(8, ядер));
  --keep=<дир>     оставить построенные сцены в `<дир>/gen` (иначе временный
                   каталог снимается);
  --skip-freshness НЕ судить свежесть каталога проб (только стенд полосы, где
                   каталог собран из другого дерева — worktree);
  --quiet          печатать только расхождения и сводку;
  --csv=<файл>     записать регистр клейм (сцена, живой, генератор, матрица, приговор).

Коды возврата:
  0 — генератор воспроизводит корпус, опись побайтно, матрицы на своих сценах
      (или самопроверка прошла);
  1 — расхождение (таблица напечатана) / самопроверка не прошла;
  2 — стенд: генератор не отработал, нет живого каталога, проба не ответила;
  3 — каталог проб не найден, не заверен или протух (находки `T226`).

Время: ~7 с на штатном Debug-каталоге при 8 потоках (генератор 0.4 с, 92
запуска пробы клейма 5.0–5.5 с, свежесть 0.9 с; `--selftest` — 11 с; замерено
П91 17.09.2026, `handover/p91-t261/`).
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
from concurrent.futures import ThreadPoolExecutor

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LIVE_DEFAULT = os.path.join(REPO, u'tools', u'CORPUS', u'corpus', u'geometries')
PROBES_DEFAULT = os.path.join(REPO, u'tools', u'effmaker', u'probes', u'build')
APPWD_PLAN = os.path.join(REPO, u'tools', u'CORPUS', u'scripts', u'appwd_plan.ps1')
BUILD_HINT = u'   собрать: pwsh tools\\effmaker\\probes\\build_all.ps1 -Bin BecquerelMonitor\\bin\\Debug_Codex'
GENERATOR = u'CorpusGeomProbe.exe'
STAMPER = u'MatrixStampProbe.exe'
# Наборы исходников, от которых зависит приговор (`T226`): ключи `each.*` заверения.
PROBE_KEYS = (u'corpusgeomprobe', u'matrixstampprobe')

RE_DEFAULT = re.compile(r'клеймо при умолчаниях\s*:\s*(phys=\S+)')
RE_INFILE = re.compile(r'клеймо в файле\s*:\s*(phys=\S+)')
RE_BYOWN = re.compile(r'клеймо по НАСТРОЙКАМ ФАЙЛА\s*:\s*(phys=\S+)')
RE_GRID = re.compile(r'узлов\s+(\S+)\s+\(умолчание\s+(\S+)\),\s+сетка\s+(\S+)\s+\(умолчание\s+(\S+)\)')
RE_GEN_COUNT = re.compile(r'геометрий:\s*(\d+),\s*спектров под ними:\s*(\d+)')
RE_GEN_ROOT = re.compile(r'корень корпуса\s*:\s*(.+)')


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


def decode_console(raw):
    u"""Вывод .NET-пробы: `MatrixStampProbe` ставит UTF-8, `CorpusGeomProbe` — нет и
    пишет кодовой страницей консоли (OEM, здесь cp866). Сперва строгий UTF-8, иначе cp866."""
    try:
        return raw.decode('utf-8')
    except UnicodeDecodeError:
        return raw.decode('cp866', 'replace')


def short(stamp):
    if not stamp:
        return u'?'
    return stamp[:24]


def scenes_in(folder):
    u"""Имена сцен (`.in` без расширения) в каталоге — только верхний уровень
    (`pinned/` и `response/` — не сцены)."""
    if not os.path.isdir(folder):
        return set()
    return set(n[:-3] for n in os.listdir(folder)
               if n.lower().endswith(u'.in') and os.path.isfile(os.path.join(folder, n)))


# ── каталог проб и его свежесть (T226) ──────────────────────────────────────

def find_probes(explicit):
    for candidate in (explicit, os.environ.get('BQ_CORPUS_SCENES_PROBES')):
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
    (`appwd_plan.ps1`), приговор сужен до набора приложения, наборов двух проб и
    sha трёх exe. Возвращает (bad, note, err): `err` — сторож не отработал, и это
    тоже отказ, а не «проверено» (`T69`)."""
    pwsh = find_pwsh()
    if pwsh is None:
        return [], [], u'нет pwsh/powershell — сторож свежести (appwd_plan.ps1) позвать нечем'
    if not os.path.isfile(APPWD_PLAN):
        return [], [], u'нет %s — сторож свежести позвать нечем' % APPWD_PLAN
    keys_ps = u', '.join(u"'%s'" % k for k in PROBE_KEYS)
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
    $note.Add(('сверено: набор приложения {0} ({1} файлов); {2}; sha трёх exe' -f $rec.App.Fp.Substring(0, 12), $rec.App.N, ($sets -join '; ')))
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


# ── генератор ────────────────────────────────────────────────────────────────

def run_generator(probes, out_dir):
    u"""`CorpusGeomProbe --out=<абс. путь>` из каталога проб. Возвращает (код, текст)."""
    exe = os.path.join(probes, GENERATOR)
    out_dir = os.path.abspath(out_dir)
    proc = subprocess.run([exe, u'--out=' + out_dir], cwd=probes,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    return proc.returncode, decode_console(proc.stdout)


# ── клейма ───────────────────────────────────────────────────────────────────

def stamps_of(probes, geometry, matrix):
    u"""Три клейма `MatrixStampProbe`: при умолчаниях, в файле `.rmx`, по настройкам
    файла; плюс строка сетки. Код возврата пробы НЕ читается (см. шапку)."""
    exe = os.path.join(probes, STAMPER)
    proc = subprocess.run([exe, u'--geometry=' + geometry, u'--matrix=' + matrix], cwd=probes,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    text = decode_console(proc.stdout)
    m1 = RE_DEFAULT.search(text)
    m2 = RE_INFILE.search(text)
    m3 = RE_BYOWN.search(text)
    mg = RE_GRID.search(text)
    return {
        u'default': m1.group(1) if m1 else None,
        u'infile': m2.group(1) if m2 else None,
        u'byown': m3.group(1) if m3 else None,
        u'grid': mg.groups() if mg else None,
        u'text': text,
        u'code': proc.returncode,
    }


def judge_scene(key, live_dir, gen_dir, probes, none_rmx):
    u"""Приговор одной сцене: словарь с клеймами и списком расхождений (пусто = сошлось)."""
    live_in = os.path.join(live_dir, key + u'.in')
    gen_in = os.path.join(gen_dir, key + u'.in')
    rmx = os.path.join(live_dir, key + u'.rmx')
    has_rmx = os.path.isfile(rmx)
    row = {u'key': key, u'live': None, u'gen': None, u'rmx': None, u'has_rmx': has_rmx, u'bad': []}
    if not os.path.isfile(live_in):
        row[u'bad'].append(u'в корпусе нет этой сцены, а генератор её строит')
    else:
        s = stamps_of(probes, live_in, rmx if has_rmx else none_rmx)
        row[u'live'] = s[u'default']
        row[u'rmx'] = s[u'infile'] if has_rmx else None
        row[u'byown'] = s[u'byown']
        row[u'grid'] = s[u'grid']
        if row[u'live'] is None:
            tail = u' | '.join(l.strip() for l in s[u'text'].strip().splitlines()[-3:])
            row[u'bad'].append(u'живой .in не читается пробой клейма (код %d): %s' % (s[u'code'], tail))
    if not os.path.isfile(gen_in):
        row[u'bad'].append(u'ГЕНЕРАТОР НЕ ЗНАЕТ СЦЕНЫ — полный прогон выкинул бы её из описи (случай П22)')
    else:
        g = stamps_of(probes, gen_in, none_rmx)
        row[u'gen'] = g[u'default']
        if row[u'gen'] is None:
            tail = u' | '.join(l.strip() for l in g[u'text'].strip().splitlines()[-3:])
            row[u'bad'].append(u'построенный .in не читается пробой клейма (код %d): %s' % (g[u'code'], tail))
    if row[u'live'] and row[u'gen'] and row[u'live'] != row[u'gen']:
        row[u'bad'].append(u'ГЕНЕРАТОР СТРОИТ ДРУГУЮ СЦЕНУ: клеймо построенного %s ≠ живого %s'
                           % (short(row[u'gen']), short(row[u'live'])))
    if has_rmx and row[u'live']:
        if row[u'rmx'] is None:
            row[u'bad'].append(u'матрица .rmx есть, а клейма из неё проба не прочла')
        elif row[u'rmx'] != row[u'live']:
            if row.get(u'byown') and row[u'byown'] == row[u'rmx']:
                grid = row.get(u'grid')
                detail = (u' (узлов %s против умолчания %s, сетка %s против %s)' % grid) if grid else u''
                row[u'bad'].append(u'МАТРИЦА ПОСЧИТАНА НА ЭТУ СЦЕНУ, НО ДРУГИМ РЕЦЕПТОМ: клеймо файла %s '
                                   u'равно клейму по его настройкам, а не умолчаниям%s' % (short(row[u'rmx']), detail))
            else:
                p_file = (row[u'rmx'].split(u';')[0] if row[u'rmx'] else u'?')
                p_now = row[u'live'].split(u';')[0]
                why = (u' — версия физики файла %s против нынешней %s' % (p_file, p_now)) if p_file != p_now else u''
                row[u'bad'].append(u'МАТРИЦА ПОСЧИТАНА НА ДРУГУЮ СЦЕНУ или состав клейма сменился: '
                                   u'клеймо файла %s ≠ клейма сцены %s%s' % (short(row[u'rmx']), short(row[u'live']), why))
    return row


# ── опись и состав ───────────────────────────────────────────────────────────

def compare_index(live_dir, gen_dir, out):
    u"""`index.csv` побайтно; при расхождении — первая разошедшаяся строка и числа строк."""
    live = os.path.join(live_dir, u'index.csv')
    gen = os.path.join(gen_dir, u'index.csv')
    if not os.path.isfile(live):
        out.say(u'  ⛔ в корпусе нет описи %s' % live)
        return False
    if not os.path.isfile(gen):
        out.say(u'  ⛔ генератор не записал опись %s' % gen)
        return False
    with open(live, 'rb') as fh:
        a = fh.read()
    with open(gen, 'rb') as fh:
        b = fh.read()
    if a == b:
        out.say(u'  ✅ опись index.csv: побайтно равна (%d байт, %d строк)' % (len(a), a.count(b'\n')))
        return True
    la = a.split(b'\n')
    lb = b.split(b'\n')
    first = None
    for i in range(max(len(la), len(lb))):
        x = la[i] if i < len(la) else None
        y = lb[i] if i < len(lb) else None
        if x != y:
            first = (i + 1, x, y)
            break
    out.say(u'  ⛔ ОПИСЬ index.csv РАЗОШЛАСЬ: в корпусе %d байт / %d строк, у генератора %d байт / %d строк'
            % (len(a), a.count(b'\n'), len(b), b.count(b'\n')))
    if first:
        n, x, y = first
        shown = lambda v: u'<нет>' if v is None else v.decode('utf-8', 'replace').rstrip(u'\r')
        out.say(u'     первая разошедшаяся строка %d: корпус «%s», генератор «%s»' % (n, shown(x), shown(y)))
    return False


# ── прогон ───────────────────────────────────────────────────────────────────

def run_check(probes, live_dir, only=None, jobs=None, keep=None, skip_freshness=False,
              quiet=False, silent=False, csv_path=None):
    u"""Полный приговор. Возвращает (код, Out, rows). `quiet` — не печатать сошедшиеся
    строки таблицы; `silent` — не печатать ничего (самопроверка читает буфер)."""
    out = Out(silent=silent)
    t0 = time.time()
    out.say(u'== сторож сцен генератора (T261): генератор → клеймо → живые .in / .rmx ==')
    out.say(u'  каталог проб : %s' % probes)
    out.say(u'  живые сцены  : %s' % live_dir)
    rows = []

    for exe in (GENERATOR, STAMPER):
        if not os.path.isfile(os.path.join(probes, exe)):
            out.say(u'  ⛔ в каталоге проб нет %s' % exe)
            out.say(BUILD_HINT)
            return 3, out, rows
    if not os.path.isdir(live_dir):
        out.say(u'  ⛔ нет каталога живых сцен %s' % live_dir)
        return 2, out, rows

    # 1. свежесть (T226)
    t_fresh = 0.0
    if skip_freshness:
        out.say(u'  ⚠ свежесть каталога проб НЕ судится (--skip-freshness) — только стенд полосы')
    else:
        t = time.time()
        bad, note, err = freshness(probes)
        t_fresh = time.time() - t
        if err:
            out.say(u'  ⛔ %s' % err)
            return 3, out, rows
        for n in note:
            out.say(u'  свежесть: %s (%.1f с)' % (n, t_fresh))
        if bad:
            out.say(u'  ⛔ КАТАЛОГ ПРОБ ПРОТУХ (T226) — прогон старым кодом судил бы не то, что в дереве:')
            for b in bad:
                out.say(u'     - %s' % b)
            out.say(BUILD_HINT)
            return 3, out, rows

    # 2. генератор — в свой каталог
    tmp = None
    if keep:
        gen_dir = os.path.join(os.path.abspath(keep), u'gen')
        if os.path.isdir(gen_dir):
            shutil.rmtree(gen_dir)
        os.makedirs(gen_dir)
    else:
        tmp = tempfile.mkdtemp(prefix='bq_scenes_')
        gen_dir = os.path.join(tmp, u'gen')
        os.makedirs(gen_dir)
    try:
        t = time.time()
        code, text = run_generator(probes, gen_dir)
        t_gen = time.time() - t
        m = RE_GEN_COUNT.search(text)
        mr = RE_GEN_ROOT.search(text)
        verdict = u'ВСЕ СОШЛИСЬ' if u'ВСЕ СОШЛИСЬ' in text else (u'ЕСТЬ РАЗОШЕДШИЕСЯ' if u'РАЗОШЕДШИЕСЯ' in text else u'?')
        out.say(u'  генератор %s --out=%s: код %d, %.1f с; %s; корень корпуса генератора: %s'
                % (GENERATOR, gen_dir, code, t_gen,
                   (u'геометрий %s, спектров %s, %s' % (m.group(1), m.group(2), verdict)) if m else u'сводной строки нет',
                   mr.group(1).strip() if mr else u'?'))
        if code != 0:
            out.say(u'  ⛔ ГЕНЕРАТОР ОТКАЗАЛ (код %d) — его последние строки:' % code)
            for line in text.strip().splitlines()[-8:]:
                out.say(u'     ' + line.rstrip())
            # 1 у самого генератора — его собственная приёмка (объём, пресет) не
            # сошлась: это дефект генератора/таблиц, красный; 2 — стенд.
            return (1 if code == 1 else 2), out, rows

        # 3. состав сцен и опись
        live_keys = scenes_in(live_dir)
        gen_keys = scenes_in(gen_dir)
        out.say(u'  сцен в корпусе %d, построено генератором %d' % (len(live_keys), len(gen_keys)))
        index_ok = compare_index(live_dir, gen_dir, out)
        union = sorted(live_keys | gen_keys)
        if only:
            wanted = set(only)
            unknown = wanted - set(union)
            if unknown:
                out.say(u'  ⛔ --only: неизвестные сцены %s' % u', '.join(sorted(unknown)))
                return 2, out, rows
            to_stamp = [k for k in union if k in wanted]
            out.say(u'  клейма снимаются только у %d сцен (--only)' % len(to_stamp))
        else:
            to_stamp = union

        # 4. клейма — параллельно
        none_rmx = os.path.join(gen_dir, u'__none__.rmx')
        n_jobs = jobs or min(8, os.cpu_count() or 1)
        t = time.time()
        with ThreadPoolExecutor(max_workers=n_jobs) as pool:
            rows = list(pool.map(lambda k: judge_scene(k, live_dir, gen_dir, probes, none_rmx), to_stamp))
        t_stamp = time.time() - t

        # состав — сцены, у которых клейма не снимались (--only), тоже судятся по имени
        for k in union:
            if k in set(r[u'key'] for r in rows):
                continue
            if k not in live_keys:
                rows.append({u'key': k, u'live': None, u'gen': None, u'rmx': None, u'has_rmx': False,
                             u'bad': [u'в корпусе нет этой сцены, а генератор её строит']})
            elif k not in gen_keys:
                rows.append({u'key': k, u'live': None, u'gen': None, u'rmx': None, u'has_rmx': False,
                             u'bad': [u'ГЕНЕРАТОР НЕ ЗНАЕТ СЦЕНЫ — полный прогон выкинул бы её из описи (случай П22)']})
        rows.sort(key=lambda r: r[u'key'])

        out.say(u'')
        n_bad = 0
        n_rmx = 0
        n_rmx_ok = 0
        header = False
        for r in rows:
            ok = not r[u'bad']
            if r[u'has_rmx']:
                n_rmx += 1
                if r[u'rmx'] and r[u'rmx'] == r[u'live']:
                    n_rmx_ok += 1
            if not ok:
                n_bad += 1
            if ok and quiet:
                continue
            if not header:
                out.say(u'  %-30s %-24s %-24s %-24s %s' % (u'сцена', u'живой .in', u'генератор', u'матрица .rmx', u'приговор'))
                header = True
            out.say(u'  %-30s %-24s %-24s %-24s %s' % (
                r[u'key'], short(r[u'live']), short(r[u'gen']),
                short(r[u'rmx']) if r[u'has_rmx'] else u'нет .rmx',
                u'СОШЛОСЬ' if ok else u'РАЗОШЛОСЬ'))
            for b in r[u'bad']:
                out.say(u'  %-30s   ⛔ %s' % (u'', b))
        if header:
            out.say(u'')
        stamped = [r for r in rows if r[u'live'] is not None or r[u'gen'] is not None]
        out.say(u'  сцен %d: сошлось %d, разошлось %d; клейма сняты у %d; матриц на месте %d из %d, на своей сцене и рецепте %d'
                % (len(rows), len(rows) - n_bad, n_bad, len(stamped), n_rmx, len(live_keys), n_rmx_ok))
        out.say(u'  время %.1f с: генератор %.1f, клейма %.1f на %d потоках (%d запусков), свежесть %.1f'
                % (time.time() - t0, t_gen, t_stamp, n_jobs, 2 * len(to_stamp), t_fresh))
        if csv_path:
            with io.open(csv_path, 'w', encoding='utf-8', newline='') as fh:
                fh.write(u'scene,stamp_live_in,stamp_generated_in,stamp_rmx,verdict\n')
                for r in rows:
                    fh.write(u'%s,%s,%s,%s,%s\n' % (r[u'key'], r[u'live'] or u'', r[u'gen'] or u'',
                                                    (r[u'rmx'] or u'') if r[u'has_rmx'] else u'',
                                                    u'СОШЛОСЬ' if not r[u'bad'] else u'РАЗОШЛОСЬ: ' + u' | '.join(r[u'bad'])))
            out.say(u'  регистр клейм: %s' % csv_path)
        if n_bad or not index_ok:
            out.say(u'  ⛔ НЕ СОШЛОСЬ: %s' % u'; '.join(filter(None, [
                (u'клейма/состав — %d сцен' % n_bad) if n_bad else None,
                None if index_ok else u'опись index.csv'])))
            return 1, out, rows
        out.say(u'  ✅ СОШЛОСЬ: генератор воспроизводит корпус с точностью до клейма, опись побайтно, '
                u'матрицы склада — на своих сценах штатным рецептом')
        return 0, out, rows
    finally:
        if tmp and os.path.isdir(tmp):
            shutil.rmtree(tmp, ignore_errors=True)


# ── самопроверка ─────────────────────────────────────────────────────────────

# Прежний сосуд «из объёма» (до П66/П72 14.09.2026), которым П72 подсаживала
# генератор: Ø135.2 × 104, колодец Ø76 × 70, слой 100 мм. Здесь он подсаживается
# в КОПИЮ корпуса — зеркально, с тем же ожиданием «клейма расходятся».
OLD_VESSEL = [
    (b'SM_BeakerDiameter = 15.4 cm', b'SM_BeakerDiameter = 13.519602 cm'),
    (b'SM_BeakerHeight = 11.2 cm', b'SM_BeakerHeight = 10.4 cm'),
    (b'SM_BeakerHoleDiameter = 9.7 cm', b'SM_BeakerHoleDiameter = 7.6 cm'),
    (b'SM_BeakerHoleHeight = 6.5 cm', b'SM_BeakerHoleHeight = 7 cm'),
    (b'SM_SourceHeight = 8.6058 cm', b'SM_SourceHeight = 10 cm'),
]
PLANT_SCENE = u'G1S_mar1l_oisn06_057_p16'
CONTROL_SCENE = u'G1S_point5'
MATRIX_VICTIM = u'G1S_point25'
STRAY_SCENE = u'X_stray'


def copy_corpus(live_dir, into):
    u"""Копия каталога сцен (только `.in`, `.rmx`, `index.csv`) во временный каталог."""
    os.makedirs(into)
    for n in os.listdir(live_dir):
        p = os.path.join(live_dir, n)
        if not os.path.isfile(p):
            continue
        low = n.lower()
        if low.endswith(u'.in') or low.endswith(u'.rmx') or low == u'index.csv':
            shutil.copy2(p, os.path.join(into, n))


def selftest(probes, live_dir, jobs):
    say = lambda s=u'': (sys.stdout.write(s + u'\n'), sys.stdout.flush())
    say(u'== самопроверка сторожа сцен (T261): обе стороны ==')
    results = []

    def record(tag, expect_code, code, extra_ok, extra_why, out):
        ok = (code == expect_code) and extra_ok
        results.append((tag, expect_code, code, ok, extra_why))
        say(u'  %s %s: ожидался код %d, получен %d%s' % (u'✅' if ok else u'⛔', tag, expect_code, code,
                                                       (u'; ' + extra_why) if extra_why else u''))
        if not ok:
            say(u'  --- вывод прогона ---')
            for line in out.buf.getvalue().splitlines():
                say(u'      ' + line)

    def bad_of(rows, key):
        for r in rows:
            if r[u'key'] == key:
                return r[u'bad']
        return None

    # (в) чистый набор — зелёный, полный прогон, свежесть судится
    code, out, rows = run_check(probes, live_dir, jobs=jobs, silent=True)
    n_bad = sum(1 for r in rows if r[u'bad'])
    record(u'(в) чистый набор', 0, code, n_bad == 0 and len(rows) > 0,
           u'сцен %d, разошлось %d' % (len(rows), n_bad), out)
    if code == 3:
        say(u'  каталог проб протух — подсадки не имеют смысла, сперва пересобрать')
        return 1

    tmp = tempfile.mkdtemp(prefix='bq_scenes_self_')
    try:
        # (а) подсадка сосуда
        cp = os.path.join(tmp, u'a')
        copy_corpus(live_dir, cp)
        victim = os.path.join(cp, PLANT_SCENE + u'.in')
        with open(victim, 'rb') as fh:
            data = fh.read()
        missing = [old.decode('ascii') for old, new in OLD_VESSEL if old not in data]
        if missing:
            say(u'  ⛔ (а) подсадка не легла: в %s нет строк %s — контроль мерит пустоту' % (PLANT_SCENE, u', '.join(missing)))
            results.append((u'(а) подсадка сосуда', 1, None, False, u'подсадка не легла'))
        else:
            for old, new in OLD_VESSEL:
                data = data.replace(old, new)
            with open(victim, 'wb') as fh:
                fh.write(data)
            code, out, rows = run_check(probes, cp, only=[PLANT_SCENE, CONTROL_SCENE], jobs=jobs,
                                        skip_freshness=True, silent=True)
            b = bad_of(rows, PLANT_SCENE) or []
            ctrl = bad_of(rows, CONTROL_SCENE)
            hit_gen = any(u'ГЕНЕРАТОР СТРОИТ ДРУГУЮ СЦЕНУ' in x for x in b)
            hit_rmx = any(u'МАТРИЦА ПОСЧИТАНА НА ДРУГУЮ СЦЕНУ' in x for x in b)
            record(u'(а) подсадка сосуда «из объёма» в %s' % PLANT_SCENE, 1, code,
                   hit_gen and hit_rmx and ctrl == [],
                   u'генератор≠живой %s, матрица≠живой %s, контроль %s сошёлся %s'
                   % (hit_gen, hit_rmx, CONTROL_SCENE, ctrl == []), out)

        # (б) подсадка матрицы
        cp = os.path.join(tmp, u'b')
        copy_corpus(live_dir, cp)
        src = os.path.join(cp, CONTROL_SCENE + u'.rmx')
        dst = os.path.join(cp, MATRIX_VICTIM + u'.rmx')
        if not os.path.isfile(src):
            say(u'  ⚠ (б) в живом складе нет %s.rmx — подсадка матрицы пропущена (склад вне git)' % CONTROL_SCENE)
            results.append((u'(б) подсадка матрицы', 1, None, True, u'пропущена: склада нет'))
        else:
            shutil.copy2(src, dst)
            code, out, rows = run_check(probes, cp, only=[MATRIX_VICTIM, CONTROL_SCENE], jobs=jobs,
                                        skip_freshness=True, silent=True)
            b = bad_of(rows, MATRIX_VICTIM) or []
            ctrl = bad_of(rows, CONTROL_SCENE)
            hit = any(u'МАТРИЦА ПОСЧИТАНА НА ДРУГУЮ СЦЕНУ' in x for x in b)
            only_that = len(b) == 1
            record(u'(б) матрица %s под именем %s' % (CONTROL_SCENE, MATRIX_VICTIM), 1, code,
                   hit and only_that and ctrl == [],
                   u'строка «на другую сцену» %s, генератор при этом сошёлся %s, контроль %s' % (hit, only_that, ctrl == []), out)

        # (г) подсадка описи
        cp = os.path.join(tmp, u'g')
        copy_corpus(live_dir, cp)
        idx = os.path.join(cp, u'index.csv')
        with open(idx, 'rb') as fh:
            lines = fh.read().split(b'\n')
        with open(idx, 'wb') as fh:
            fh.write(b'\n'.join(lines[:-2] + lines[-1:]))
        code, out, rows = run_check(probes, cp, only=[CONTROL_SCENE], jobs=jobs, skip_freshness=True, silent=True)
        hit = u'ОПИСЬ index.csv РАЗОШЛАСЬ' in out.buf.getvalue()
        record(u'(г) из index.csv снята строка', 1, code, hit and all(not r[u'bad'] for r in rows),
               u'отказ по описи %s, сцены сошлись %s' % (hit, all(not r[u'bad'] for r in rows)), out)

        # (д) сцена мимо генератора
        cp = os.path.join(tmp, u'd')
        copy_corpus(live_dir, cp)
        shutil.copy2(os.path.join(cp, CONTROL_SCENE + u'.in'), os.path.join(cp, STRAY_SCENE + u'.in'))
        code, out, rows = run_check(probes, cp, only=[STRAY_SCENE, CONTROL_SCENE], jobs=jobs,
                                    skip_freshness=True, silent=True)
        b = bad_of(rows, STRAY_SCENE) or []
        hit = any(u'ГЕНЕРАТОР НЕ ЗНАЕТ СЦЕНЫ' in x for x in b)
        record(u'(д) сцена %s в корпусе мимо генератора' % STRAY_SCENE, 1, code,
               hit and bad_of(rows, CONTROL_SCENE) == [],
               u'строка «генератор не знает» %s' % hit, out)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    say(u'')
    n_ok = sum(1 for r in results if r[3])
    say(u'  контролей %d, повели себя как положено %d' % (len(results), n_ok))
    if n_ok == len(results):
        say(u'  ✅ САМОПРОВЕРКА ПРОШЛА: сторож зелен на чистом наборе и красен на каждой подсадке')
        return 0
    say(u'  ⛔ САМОПРОВЕРКА НЕ ПРОШЛА')
    return 1


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--probes', default=None)
    ap.add_argument('--live', default=None)
    ap.add_argument('--only', default=None)
    ap.add_argument('--jobs', type=int, default=None)
    ap.add_argument('--keep', default=None)
    ap.add_argument('--skip-freshness', action='store_true')
    ap.add_argument('--selftest', action='store_true')
    ap.add_argument('--quiet', action='store_true')
    ap.add_argument('--csv', default=None)
    args = ap.parse_args(argv)

    probes = find_probes(args.probes)
    live_dir = os.path.abspath(args.live) if args.live else LIVE_DEFAULT
    if args.selftest:
        return selftest(probes, live_dir, args.jobs)
    only = [s.strip() for s in args.only.split(u',') if s.strip()] if args.only else None
    code, _, _ = run_check(probes, live_dir, only=only, jobs=args.jobs, keep=args.keep,
                           skip_freshness=args.skip_freshness, quiet=args.quiet,
                           csv_path=os.path.abspath(args.csv) if args.csv else None)
    return code


if __name__ == '__main__':
    sys.exit(main())
