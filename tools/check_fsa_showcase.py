#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ВИТРИНЫ FSA (`T260`): картинка разбора на спектрах, которые Amber видит
каждый день, против ЭТАЛОНА в git — любой сдвиг слоёв, серого слоя, невязки,
долей, значимостей или строк окна отчёта сверх допуска — ОТКАЗ, пока эталон не
переобъявлен явно (`tools/fsa_showcase/snapshot.ps1`, печатает diff числами).

## Зачем

14.09.2026 четыре правки отображения FSA принимались «фит побитово + пробы +
малая база» — и все были зелены, а на экране Amber (Cs-137 в домике, ASN16,
матрица) появилась яма 56–100 кэВ (`AMBER30`): картинку смотрели только на
спектре, породившем задачу (слабый фильтр, где хвоста 1008 отсч.), а на
сильном источнике в домике тот же хвост — 14.0 М отсч. (5.6 % спектра). Ни
один сторож этого не видел: в малой базе все 12 спектров ASN16 без матрицы.
Этот сторож смотрит НА КАРТИНКУ, а не на фит: слои стека по полосам энергии,
серый слой, невязка «не описано / лишнее», доли и строки отчёта КАК ПОКАЗАНЫ.

## Что и как сравнивается

Витрина — `tools/fsa_showcase/showcase.json`: спектр × режим (ключи
`FsaStackShot`), матрица — свой склад витрины (`store:`), живой склад корпуса
(`corpus:`) или её нет (`none`, `--no-matrix`). На каждую пару проба гонится
из рабочего каталога `tools/fsa_showcase/wd/` (копия каталога проб + конфиг
витрины: приборы Amber и корпуса, её библиотека, матрицы под guid кривой
спектра) с `--dump=` (кривые по каналам), `--rates=` (числа разбора) и
`--screen` (строки окна отчёта). Дамп сворачивается в ПОЛОСЫ ЭНЕРГИИ
(`bands_kev` манифеста): сумма отсчётов каждого слоя, модели, показного и
фитового измерения, сырого сплайна, отвязанного хвоста; «не описано» и
«лишнее» — против кривой фита (число строки отчёта) и против показной (лента
на графике). Эталон — `tools/fsa_showcase/reference/<спектр>__<режим>.json`.

Допуск — `rel`/`abs` в самом эталоне (умолчание 1e-9 / 1e-6): проба
ДЕТЕРМИНИРОВАНА (замер П74 14.09.2026: три прогона подряд — дампы, rates и
строки побайтно одинаковы), поэтому сравнение по сути побитовое; ключи
`--rel=`/`--abs=` — только для замера разброса, не для приёмки. Строки `ROW`
и `SCREEN` (имя, значение, подсказка) сравниваются дословно.

⛔ Отказ печатает ТАБЛИЦУ «спектр — режим — полоса — компонент — было — стало —
Δ»: это и есть материал приёмки правки отображения. Правка того, что видит
человек, принимается переобъявлением эталона с этой таблицей в отчёте полосы
(SKILL `todo-work` §6).

## Свежесть каталога проб — сторож `T226`, не обходится

Каталог проб — `tools/effmaker/probes/build` (штатный `-Out` `build_all.ps1`);
иначе `--probes=` или `BQ_FSA_SHOWCASE_PROBES`. Перед прогоном каталог
судится ТЕМИ ЖЕ функциями, что судят оснастку корпуса (`appwd_plan.ps1`:
`Get-AppWdSourceRecord`, `Get-AppWdStampSources`, `Compare-AppWdSourceMap`,
`Get-AppWdSha256` — своей копии отпечатка здесь нет, `T61`): заверение
`.appwd.json`, отпечаток набора исходников ПРИЛОЖЕНИЯ (по `.csproj`) и набора
`FsaStackShot` (её `.cs` + довески), sha обоих двоичных файлов против
заверённых. Приговор нарочно СУЖЕН до того, что сторож исполняет: чужая проба,
правленная соседом, каталог для витрины не протухает (`T226`: «набор берётся
тот, из которого собран данный двоичный файл, а не всё дерево»). Протухший
каталог — код 3 с находками, а не прогон старым кодом (A77: «проверять то,
что будет использовано»).
⚠ Чужая незакоммиченная правка `.cs` ПРИЛОЖЕНИЯ в дереве — законный код 3:
приложение у проб собрано не из этого дерева, и картинка судилась бы не о нём.

## Положительный контроль — `--selftest` (обе стороны)

  (в) тот же прогон против эталона — ОБЯЗАН пройти (сторож, отказывающий
      всегда, бесполезен так же, как молчащий всегда);
  (а) `Cs 137 в домике` с `--tail-as-residual` (яма `AMBER30`: хвост образа
      лентой невязки, мимо серого слоя) — ОБЯЗАН отказать, и среди строк
      таблицы обязана быть полоса 30–100 кэВ;
  (б) подсаженный эталон: доля `share_pct` первого компонента +1 % и слой в
      полосе 300–700 кэВ ×1.01 — ОБЯЗАН отказать.

  python tools/check_fsa_showcase.py [--probes=<каталог проб>] [--only=<ключ>[,…]]
                                     [--snapshot] [--selftest] [--rel=1e-9] [--abs=1e-6]
                                     [--skip-freshness] [--reference=<каталог>]

  --snapshot        переобъявить эталон (снять заново) — печатает diff против
                    прежнего; зовётся из `tools/fsa_showcase/snapshot.ps1`;
  --selftest        положительный контроль (см. выше), нужен собранный склад;
  --skip-freshness  НЕ судить свежесть каталога проб (только для стенда полосы,
                    где каталог собран из ДРУГОГО дерева — worktree; в эталон git
                    такие числа не кладутся: `--snapshot` с этим ключом пишет
                    только в сторонний `--reference=`, и говорит об этом вслух);
  --reference=      каталог эталона вместо `tools/fsa_showcase/reference` (стенд
                    полосы: снять и сравнить, не трогая эталон git).

Коды возврата:
  0 — картинка совпала с эталоном на всех парах (или эталон снят/самопроверка прошла);
  1 — картинка ИЗМЕНИЛАСЬ (таблица напечатана) / самопроверка не прошла;
  2 — стенд: нет спектра, матрицы, конфига; проба отказала (её вывод напечатан);
  3 — каталог проб не найден, не заверен или протух (находки `T226` напечатаны);
  4 — эталона нет — сперва `snapshot.ps1`.
"""

import argparse
import csv
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHOW = os.path.join(REPO, u'tools', u'fsa_showcase')
MANIFEST = os.path.join(SHOW, u'showcase.json')
REFERENCE_DEFAULT = os.path.join(SHOW, u'reference')
REFERENCE = REFERENCE_DEFAULT
WD = os.path.join(SHOW, u'wd')
STORE = os.path.join(SHOW, u'store')
CORPUS_GEOM = os.path.join(REPO, u'tools', u'CORPUS', u'corpus', u'geometries')
PROBES_DEFAULT = os.path.join(REPO, u'tools', u'effmaker', u'probes', u'build')
APPWD_PLAN = os.path.join(REPO, u'tools', u'CORPUS', u'scripts', u'appwd_plan.ps1')
BUILD_HINT = u'   собрать: pwsh tools\\effmaker\\probes\\build_all.ps1 -Bin BecquerelMonitor\\bin\\Debug_Codex'
REL_DEFAULT = 1e-9
ABS_DEFAULT = 1e-6
# Столбцы дампа, которые не слои: свёртываются по полосам как есть.
DUMP_FIXED = (u'net', u'model', u'continuum_raw', u'untied_tail', u'fit')
# Разделы `--rates=`, чьи числа сравниваются; `meta` — целиком.
RATES_NUMERIC = (u'count_rate', u'z', u'decision_threshold_rate', u'detection_limit_rate',
                 u'peak_counts', u'share_pct', u'total_yield_pct', u'limit_peak_counts',
                 u'degenerate', u'collinearity')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def say(text=u''):
    print(text)


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def load_manifest():
    with io.open(MANIFEST, encoding='utf-8') as fh:
        return json.load(fh)


def rel_repo(path):
    u"""Путь от корня дерева с косой чертой; вне дерева (другой диск, worktree) — как есть."""
    try:
        rel = os.path.relpath(path, REPO)
    except ValueError:
        return path.replace(u'\\', u'/')
    if rel.startswith(u'..'):
        return path.replace(u'\\', u'/')
    return rel.replace(u'\\', u'/')


def spectrum_path(member):
    return os.path.normpath(os.path.join(SHOW, member[u'spectrum']))


def efficiency_guid(path):
    u"""Guid кривой из узла `<Efficiency>` уровня ResultData (тот, что начинается с
    `<Guid>`); None — узла нет (кривой у спектра нет)."""
    with io.open(path, encoding='utf-8', errors='replace') as fh:
        text = fh.read()
    m = re.search(r'<Efficiency>\s*<Guid>([^<]+)</Guid>', text)
    return m.group(1).strip() if m else None


# ── каталог проб и его свежесть (T226) ──────────────────────────────────────

def find_probes(explicit):
    for candidate in (explicit, os.environ.get('BQ_FSA_SHOWCASE_PROBES')):
        if candidate:
            return os.path.abspath(candidate)
    return PROBES_DEFAULT


def read_stamp(probes):
    path = os.path.join(probes, u'.appwd.json')
    if not os.path.isfile(path):
        return None
    try:
        with io.open(path, encoding='utf-8-sig') as fh:
            return json.load(fh)
    except ValueError:
        return None


def find_pwsh():
    for name in (u'pwsh', u'powershell'):
        path = shutil.which(name)
        if path:
            return path
    return None


def freshness(probes, stamp):
    u"""Находки сторожа свежести — ТЕМИ ЖЕ функциями, что судят оснастку корпуса
    (`appwd_plan.ps1`: отпечаток набора исходников и sha двоичных файлов из заверения
    `.appwd.json`), но приговор СУЖЕН до того, что этот сторож исполняет: набор
    приложения (`.csproj`) и набор `FsaStackShot` (её `.cs` + довески). Чужая проба,
    правленная соседом, сюда не входит — ровно по `T226` («набор берётся тот, из
    которого собран данный двоичный файл, а не всё дерево»).

    Возвращает (bad, note, err): `bad` — находки (пусто = свежий), `err` — сторож не
    отработал (это тоже отказ, а не «проверено» — `T69`).
    """
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
    $key = 'fsastackshot'
    $set = $rec.Each[$key]
    if (-not $set) { $bad.Add('в дереве нет FsaStackShot.cs (tools/effmaker/probes) — пробы витрины нет') }
    else {
        $wasFp = if ($st.PSObject.Properties['each'] -and $st.each.PSObject.Properties[$key]) { [string]$st.each.$key } else { '' }
        if (-not $wasFp) { $bad.Add('в заверении нет набора FsaStackShot (each.fsastackshot) — пересобрать build_all.ps1') }
        elseif ($wasFp -ne $set.Fp) {
            $dp = Compare-AppWdSourceMap -Now $rec.Probes.Map -Was $st.probes.files
            $mine = @(@($dp.Changed) + @($dp.Added) + @($dp.Removed) | Where-Object { $set.Rel -contains $_ })
            $bad.Add(('НАБОР FsaStackShot (её .cs + довески) РАЗОШЁЛСЯ С ЗАВЕРЕННЫМ (T226): заверено {0} -> в дереве {1}; изменились: {2}' -f $wasFp.Substring(0, 12), $set.Fp.Substring(0, 12), (($mine | Sort-Object | Select-Object -First 6) -join ', ')))
        }
        $exeP = Join-Path $probes 'FsaStackShot.exe'
        $certShot = if ($st.PSObject.Properties['binaries'] -and $st.binaries.PSObject.Properties['probes'] -and $st.binaries.probes.PSObject.Properties[$key]) { [string]$st.binaries.probes.$key } else { '' }
        if (-not $certShot) { $bad.Add('в заверении нет sha FsaStackShot.exe (binaries.probes) — пересобрать build_all.ps1') }
        elseif ($certShot -ne (Get-AppWdSha256 -Path $exeP)) { $bad.Add('FsaStackShot.exe ПОДМЕНЁН ПОСЛЕ ЗАВЕРЕНИЯ КАТАЛОГА (T138/T233)') }
    }
    $note.Add(('сверено: набор приложения {0} ({1} файлов), набор FsaStackShot {2} ({3} файлов), sha двух exe' -f $rec.App.Fp.Substring(0, 12), $rec.App.N, $set.Fp.Substring(0, 12), @($set.Rel).Count))
}
$out = [ordered]@{ bad = @($bad); note = @($note) }
'@@JSON@@' + ($out | ConvertTo-Json -Depth 4 -Compress)
""" % {u'plan': APPWD_PLAN.replace(u"'", u"''"), u'repo': REPO.replace(u"'", u"''"),
       u'probes': probes.replace(u"'", u"''")}
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


# ── рабочий каталог ─────────────────────────────────────────────────────────

def mirror_probes(probes, wd):
    u"""Копия каталога проб в `wd` без его `config\\device` и `config\\NuclideDefinition.xml`
    (их даёт витрина). Копируется то, что изменилось по размеру/времени; лишнее в `wd`
    (кроме `out\\` и `config\\device\\`) снимается — иначе чужой exe пережил бы пересборку."""
    copied = 0
    wanted = set()
    for root, dirs, files in os.walk(probes):
        rel_root = os.path.relpath(root, probes)
        if rel_root == u'.':
            rel_root = u''
        low = rel_root.lower()
        if low == u'config\\device' or low.startswith(u'config\\device\\'):
            dirs[:] = []
            continue
        for name in files:
            rel = os.path.join(rel_root, name) if rel_root else name
            if rel.lower() == u'config\\nuclidedefinition.xml':
                continue
            wanted.add(rel.lower())
            src = os.path.join(root, name)
            dst = os.path.join(wd, rel)
            st = os.stat(src)
            try:
                dt = os.stat(dst)
                same = dt.st_size == st.st_size and abs(dt.st_mtime - st.st_mtime) < 2.0
            except OSError:
                same = False
            if same:
                continue
            d = os.path.dirname(dst)
            if not os.path.isdir(d):
                os.makedirs(d)
            shutil.copy2(src, dst)
            copied += 1
    removed = 0
    for root, dirs, files in os.walk(wd):
        rel_root = os.path.relpath(root, wd)
        if rel_root == u'.':
            rel_root = u''
        low = rel_root.lower()
        if low == u'out' or low.startswith(u'out\\') or low == u'config\\device' or low.startswith(u'config\\device\\'):
            dirs[:] = []
            continue
        for name in files:
            rel = os.path.join(rel_root, name) if rel_root else name
            if rel.lower() in wanted or rel.lower() == u'config\\nuclidedefinition.xml':
                continue
            os.remove(os.path.join(root, name))
            removed += 1
    return copied, removed


def sync_dir_exact(src_files, dst_dir, mask_ext):
    u"""В `dst_dir` лежат РОВНО файлы из `src_files` (по имени назначения) среди файлов с
    расширениями `mask_ext`; остальные такие снимаются (`T33`: лишний прибор с тем же
    GUID = модальное окно = зависание)."""
    if not os.path.isdir(dst_dir):
        os.makedirs(dst_dir)
    want = {}
    for src, dst_name in src_files:
        want[dst_name.lower()] = src
        dst = os.path.join(dst_dir, dst_name)
        if not os.path.isfile(dst) or sha256_file(dst) != sha256_file(src):
            shutil.copy2(src, dst)
    for name in os.listdir(dst_dir):
        p = os.path.join(dst_dir, name)
        if os.path.isfile(p) and os.path.splitext(name)[1].lower() in mask_ext and name.lower() not in want:
            os.remove(p)


def matrix_sources(member, guid):
    u"""Откуда матрица члена витрины: список (источник, имя в response). `none` — пусто."""
    spec = member.get(u'matrix', u'none')
    if spec == u'none':
        return [], None
    kind, key = spec.split(u':', 1)
    if kind == u'store':
        base = STORE
    elif kind == u'corpus':
        base = CORPUS_GEOM
    else:
        return [], u'манифест: неизвестный род матрицы «%s» у %s' % (spec, member[u'key'])
    if guid is None:
        return [], u'%s: матрица %s, а у спектра НЕТ узла Efficiency (guid неизвестен)' % (member[u'key'], spec)
    rmx = os.path.join(base, key + u'.rmx')
    if not os.path.isfile(rmx):
        hint = (u' — сперва tools\\fsa_showcase\\rebuild_store.ps1' if kind == u'store'
                else u' — живой склад корпуса без этой сцены (CorpusMatrixProbe)')
        return [], u'%s: нет матрицы %s%s' % (member[u'key'], rmx, hint)
    out = [(rmx, guid + u'.rmx')]
    qk = os.path.join(base, key + u'.qk')
    if os.path.isfile(qk):
        out.append((qk, guid + u'.qk'))
    return out, None


def assemble_wd(probes, manifest, members):
    u"""Собрать рабочий каталог; возвращает (inputs, error)."""
    if not os.path.isdir(WD):
        os.makedirs(WD)
    copied, removed = mirror_probes(probes, WD)
    cfg = os.path.join(WD, u'config')
    if not os.path.isdir(cfg):
        return None, u'в каталоге проб нет config\\ (BecquerelMonitor.xml, ROI) — собран не build_all.ps1?'
    lib_src = os.path.join(SHOW, u'config', u'NuclideDefinition.xml')
    if not os.path.isfile(lib_src):
        return None, u'нет библиотеки витрины %s' % lib_src
    shutil.copy2(lib_src, os.path.join(cfg, u'NuclideDefinition.xml'))
    dev_src_dir = os.path.join(SHOW, u'config', u'device')
    devices = sorted(n for n in os.listdir(dev_src_dir) if n.lower().endswith(u'.xml'))
    if not devices:
        return None, u'нет приборов витрины в %s' % dev_src_dir
    sync_dir_exact([(os.path.join(dev_src_dir, n), n) for n in devices],
                   os.path.join(cfg, u'device'), (u'.xml',))
    # Входы — ПО ЧЛЕНАМ: у каждого свой спектр и свои матрицы, общие — библиотека и
    # приборы; эталон пары несёт входы только своего члена, и дрейф судится по ним.
    common = {u'library_sha': sha256_file(lib_src)[:16],
              u'devices': dict((n, sha256_file(os.path.join(dev_src_dir, n))[:16]) for n in devices)}
    inputs = {u'common': common, u'members': {}}
    response = []
    for member in members:
        sp = spectrum_path(member)
        if not os.path.isfile(sp):
            return None, u'%s: нет спектра %s' % (member[u'key'], sp)
        mine = {u'spectrum': member[u'spectrum'], u'spectrum_sha': sha256_file(sp)[:16], u'matrices': {}}
        guid = efficiency_guid(sp)
        files, err = matrix_sources(member, guid)
        if err:
            return None, err
        for src, dst_name in files:
            response.append((src, dst_name))
            mine[u'matrices'][dst_name] = {u'from': rel_repo(src), u'sha': sha256_file(src)[:16]}
        inputs[u'members'][member[u'key']] = mine
    sync_dir_exact(response, os.path.join(cfg, u'device', u'response'), (u'.rmx', u'.qk'))
    out = os.path.join(WD, u'out')
    if not os.path.isdir(out):
        os.makedirs(out)
    say(u'рабочий каталог: %s (скопировано %d, снято %d; приборов %d, матриц %d)'
        % (WD, copied, removed, len(devices), len([r for r in response if r[1].endswith(u'.rmx')])))
    return inputs, None


# ── прогон пробы и свёртка ──────────────────────────────────────────────────

def run_shot(manifest, member, mode, args, extra, tag):
    u"""Позвать FsaStackShot из wd; вернуть (код, лог, dump, rates)."""
    out = os.path.join(WD, u'out')
    stem = u'%s__%s%s' % (member[u'key'], mode, tag)
    dump = os.path.join(out, stem + u'.curves.csv')
    rates = os.path.join(out, stem + u'.rates.csv')
    png = os.path.join(out, stem + u'.png')
    log = os.path.join(out, stem + u'.log')
    for p in (dump, rates, png, log):
        if os.path.isfile(p):
            os.remove(p)
    cmd = [os.path.join(WD, u'FsaStackShot.exe'), u'--spectrum=' + spectrum_path(member)]
    cmd += list(args) + list(extra) + list(manifest.get(u'common_args', []))
    cmd += [u'--dump=' + dump, u'--rates=' + rates, u'--out=' + png]
    t0 = time.time()
    try:
        proc = subprocess.run(cmd, cwd=WD, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    except OSError as e:
        return 99, u'проба не запустилась: %s' % e, None, None, 0.0
    text = proc.stdout.decode('utf-8', 'replace')
    with io.open(log, 'w', encoding='utf-8') as fh:
        fh.write(u'# ' + u' '.join(cmd) + u'\n' + text)
    return proc.returncode, text, dump, rates, time.time() - t0


def summarize(text, dump, rates, bands):
    u"""Дамп → полосы; rates → словарь; лог → строки ROW/SCREEN и числа шапки."""
    result = {u'bands': {}, u'rates': {}, u'rows': [], u'screen': [], u'head': {}}
    with io.open(dump, encoding='utf-8') as fh:
        reader = csv.reader(fh)
        header = next(reader)
        cols = header[2:]
        acc = dict((b, dict((c, 0.0) for c in cols)) for b in bands)
        miss = dict((b, {u'missing_fit': 0.0, u'excess_fit': 0.0, u'missing_net': 0.0, u'excess_net': 0.0,
                          u'channels': 0}) for b in bands)
        i_model = cols.index(u'model')
        i_fit = cols.index(u'fit')
        i_net = cols.index(u'net')
        for row in reader:
            kev = float(row[1])
            vals = [float(v) for v in row[2:]]
            for b in bands:
                if b[0] <= kev < b[1]:
                    a = acc[b]
                    for c, v in zip(cols, vals):
                        a[c] += v
                    d = vals[i_fit] - vals[i_model]
                    m = miss[b]
                    m[u'missing_fit' if d > 0 else u'excess_fit'] += abs(d)
                    d = vals[i_net] - vals[i_model]
                    m[u'missing_net' if d > 0 else u'excess_net'] += abs(d)
                    m[u'channels'] += 1
                    break
    for b in bands:
        key = u'%g-%g' % b
        entry = dict(acc[b])
        entry.update(miss[b])
        result[u'bands'][key] = entry
    result[u'layers'] = [c for c in cols if c not in DUMP_FIXED]
    with io.open(rates, encoding='utf-8') as fh:
        reader = csv.DictReader(fh)
        for row in reader:
            section = row[u'section']
            name = row[u'name']
            if section == u'meta':
                result[u'rates'].setdefault(u'meta', {})[name] = _num(row[u'count_rate'])
                continue
            entry = {}
            for k in RATES_NUMERIC:
                v = row.get(k, u'')
                if v not in (u'', None):
                    entry[k] = _num(v)
            for k in (u'kind', u'detected', u'chain_root', u'decay_chain_root', u'tied_to', u'unmeasurable_at'):
                v = row.get(k, u'')
                if v:
                    entry[k] = v
            result[u'rates'].setdefault(section, {})[name] = entry
    for line in text.splitlines():
        if line.startswith(u'ROW\t'):
            result[u'rows'].append(line.split(u'\t')[1:])
        elif line.startswith(u'SCREEN\t'):
            parts = line.split(u'\t')
            result[u'screen'].append([parts[1] if len(parts) > 1 else u'', parts[2] if len(parts) > 2 else u'',
                                      u'\t'.join(parts[3:]) if len(parts) > 3 else u''])
        elif line.startswith(u'chi2/ndf '):
            result[u'head'][u'chi2'] = line
        elif line.startswith(u'состав'):
            result[u'head'][u'composition'] = line
        elif line.startswith(u'шкала:'):
            result[u'head'][u'scale'] = line
        elif line.startswith(u'отвязанный хвост'):
            result[u'head'][u'untied'] = line
        elif line.startswith(u'серый слой'):
            result[u'head'][u'grey'] = line
    return result


def _num(text):
    try:
        return float(text)
    except ValueError:
        return text


# ── сравнение ───────────────────────────────────────────────────────────────

def close(a, b, rel, abs_tol):
    if isinstance(a, float) and isinstance(b, float):
        return abs(a - b) <= abs_tol + rel * max(abs(a), abs(b))
    return a == b


def fmt(v):
    if isinstance(v, float):
        return (u'%.3f' % v) if abs(v) >= 1e-3 or v == 0 else (u'%.6g' % v)
    return u'%s' % (v,)


def compare(ref, now, rel, abs_tol):
    u"""Список расхождений: (полоса/раздел, компонент, было, стало)."""
    diffs = []
    rb, nb = ref.get(u'bands', {}), now.get(u'bands', {})
    for band in sorted(set(rb) | set(nb), key=lambda k: float(k.split(u'-')[0])):
        r, n = rb.get(band, {}), nb.get(band, {})
        for comp in sorted(set(r) | set(n)):
            a, b = r.get(comp), n.get(comp)
            if a is None or b is None or not close(a, b, rel, abs_tol):
                diffs.append((band + u' кэВ', comp, a, b))
    rr, nr = ref.get(u'rates', {}), now.get(u'rates', {})
    for section in sorted(set(rr) | set(nr)):
        r, n = rr.get(section, {}), nr.get(section, {})
        for name in sorted(set(r) | set(n)):
            a, b = r.get(name), n.get(name)
            if section == u'meta':
                if a is None or b is None or not close(a, b, rel, abs_tol):
                    diffs.append((u'rates ' + section, name, a, b))
                continue
            if a is None or b is None:
                diffs.append((u'rates ' + section, name, u'есть' if a is not None else u'нет',
                              u'есть' if b is not None else u'нет'))
                continue
            for k in sorted(set(a) | set(b)):
                x, y = a.get(k), b.get(k)
                if x is None or y is None or not close(x, y, rel, abs_tol):
                    diffs.append((u'rates ' + section, name + u'.' + k, x, y))
    for what in (u'rows', u'screen'):
        r, n = ref.get(what, []), now.get(what, [])
        if r != n:
            width = max(len(r), len(n))
            for i in range(width):
                x = r[i] if i < len(r) else None
                y = n[i] if i < len(n) else None
                if x != y:
                    diffs.append((what.upper(), u'строка %d' % (i + 1),
                                  u' | '.join(x)[:90] if x else u'—', u' | '.join(y)[:90] if y else u'—'))
    # У эталона строки шапки лежат в `head_lines` (`head` там — ревизия git), у
    # свежей свёртки — в `head`; оба вида принимаются, чтобы сравнивать и эталон с
    # прогоном, и эталон с эталоном (подсадка в самопроверке).
    rh = ref.get(u'head_lines') if isinstance(ref.get(u'head_lines'), dict) else (
        ref.get(u'head') if isinstance(ref.get(u'head'), dict) else {})
    nh = now.get(u'head_lines') if isinstance(now.get(u'head_lines'), dict) else (
        now.get(u'head') if isinstance(now.get(u'head'), dict) else {})
    for k in sorted(set(rh) | set(nh)):
        if rh.get(k) != nh.get(k):
            diffs.append((u'шапка', k, (rh.get(k) or u'—')[:90], (nh.get(k) or u'—')[:90]))
    return diffs


def print_diffs(member_key, mode, diffs, limit=60):
    say(u'  %-22s %-14s %-16s %-28s %18s %18s %14s' % (u'спектр', u'режим', u'полоса', u'компонент', u'было', u'стало', u'Δ'))
    for i, (band, comp, a, b) in enumerate(diffs):
        if i >= limit:
            say(u'  … и ещё %d строк' % (len(diffs) - limit))
            break
        if isinstance(a, float) and isinstance(b, float):
            d = b - a
            pct = (u' (%+.2f %%)' % (100.0 * d / a)) if a else u''
            delta = fmt(d) + pct
        else:
            delta = u'≠'
        say(u'  %-22s %-14s %-16s %-28s %18s %18s %14s' % (member_key[:22], mode[:14], band[:16], comp[:28],
                                                             fmt(a)[:18], fmt(b)[:18], delta))


def ref_path(member_key, mode):
    return os.path.join(REFERENCE, u'%s__%s.json' % (member_key, mode))


def load_ref(member_key, mode):
    p = ref_path(member_key, mode)
    if not os.path.isfile(p):
        return None
    with io.open(p, encoding='utf-8') as fh:
        return json.load(fh)


# ── главный ход ─────────────────────────────────────────────────────────────

def run_pairs(manifest, members, rel, abs_tol, extra_by_member=None, tag=u''):
    u"""Прогнать все пары; вернуть {(key, mode): (summary, seconds)} либо ошибку."""
    bands = [tuple(b) for b in manifest[u'bands_kev']]
    results = {}
    for member in members:
        for mode, args in member[u'modes'].items():
            extra = (extra_by_member or {}).get(member[u'key'], [])
            code, text, dump, rates, dt = run_shot(manifest, member, mode, args, extra, tag)
            if code != 0 or not (dump and os.path.isfile(dump) and os.path.isfile(rates)):
                tail = u'\n'.join(u'      ' + l for l in text.strip().splitlines()[-15:])
                return None, (u'%s / %s: FsaStackShot вернул код %d (%.1f с):\n%s' % (member[u'key'], mode, code, dt, tail))
            results[(member[u'key'], mode)] = (summarize(text, dump, rates, bands), dt)
            say(u'  %s / %s: %.1f с' % (member[u'key'], mode, dt))
    return results, None


def stand(args, manifest, members):
    u"""Каталог проб → свежесть → рабочий каталог. Возвращает (inputs, код)."""
    probes = find_probes(args.probes)
    exe = os.path.join(probes, u'FsaStackShot.exe')
    if not os.path.isfile(exe) or not os.path.isfile(os.path.join(probes, u'BecquerelMonitor.exe')):
        say(u'⛔ ПРОБЫ НЕТ: %s (FsaStackShot.exe / BecquerelMonitor.exe)' % probes)
        say(u'   Сторож судить нечем, и молчать об этом нельзя.')
        say(BUILD_HINT)
        return None, 3
    stamp = read_stamp(probes)
    if stamp is None:
        say(u'⛔ КАТАЛОГ ПРОБ НЕ ЗАВЕРЕН: в %s нет читаемой .appwd.json (T226) — из какого набора исходников' % probes)
        say(u'   собраны пробы, не сказано; такой каталог не судят, его пересобирают.')
        say(BUILD_HINT)
        return None, 3
    say(u'каталог проб: %s (заверен %s из %s)' % (probes, stamp.get(u'built'), stamp.get(u'bin')))
    stamp_repo = os.path.normcase(os.path.normpath(stamp.get(u'repo') or u''))
    if not args.skip_freshness and stamp_repo != os.path.normcase(os.path.normpath(REPO)):
        say(u'⛔ КАТАЛОГ ПРОБ СОБРАН ИЗ ДРУГОГО ДЕРЕВА: в заверении repo = %s, а сторож судит %s' % (stamp.get(u'repo'), REPO))
        say(u'   Картинка судилась бы не об этом дереве. Собрать каталог из этого дерева (build_all.ps1)')
        say(u'   либо — только для стенда полосы — --skip-freshness (в эталон git такие числа не годятся).')
        return None, 3
    if args.skip_freshness:
        say(u'⚠ --skip-freshness: свежесть каталога проб НЕ судилась — числа этого прогона в эталон не годятся')
    else:
        t0 = time.time()
        bad, note, err = freshness(probes, stamp)
        if err:
            say(u'⛔ СТОРОЖ СВЕЖЕСТИ НЕ ОТРАБОТАЛ — это отказ, а не «проверено» (T69):')
            say(u'   ' + err)
            return None, 3
        for n in note:
            say(u'  ⚠ ' + n.replace(u'\n', u'\n    '))
        if bad:
            say(u'⛔ КАТАЛОГ ПРОБ ПРОТУХ ИЛИ ПОДМЕНЁН (T226/T41) — прогон старым кодом судил бы не это дерево:')
            for b in bad:
                say(u'   • ' + b.replace(u'\n', u'\n     '))
            say(u'   Пересобрать приложение и перегнать build_all.ps1 (%.1f с на сверку).' % (time.time() - t0))
            return None, 3
        say(u'свежесть каталога проб: сошлась с деревом (T226, %.1f с)' % (time.time() - t0))
    inputs, err = assemble_wd(probes, manifest, members)
    if err:
        say(u'⛔ СТЕНД: ' + err)
        return None, 2
    inputs[u'probes'] = {u'dir': rel_repo(probes),
                         u'built': stamp.get(u'built'),
                         u'app_sha': sha256_file(os.path.join(probes, u'BecquerelMonitor.exe'))[:16],
                         u'shot_sha': sha256_file(exe)[:16],
                         u'freshness_checked': not args.skip_freshness}
    return inputs, 0


def inputs_for(inputs, member_key):
    u"""Входы одной пары: общие (библиотека, приборы, пробы) плюс свои (спектр, матрицы)."""
    out = dict(inputs.get(u'common', {}))
    out[u'probes'] = inputs.get(u'probes', {})
    out.update(inputs.get(u'members', {}).get(member_key, {}))
    return out


def describe_inputs_drift(ref, inputs, member_key):
    u"""Изменились ли ВХОДЫ пары против эталона — печатается словами, судят числа."""
    old = ref.get(u'inputs') or {}
    now = inputs_for(inputs, member_key)
    notes = []
    if old.get(u'library_sha') != now.get(u'library_sha'):
        notes.append(u'библиотека витрины %s → %s' % (old.get(u'library_sha'), now.get(u'library_sha')))
    for name, sha in now.get(u'devices', {}).items():
        if old.get(u'devices', {}).get(name) != sha:
            notes.append(u'прибор %s: %s → %s' % (name, old.get(u'devices', {}).get(name), sha))
    for name, m in now.get(u'matrices', {}).items():
        o = old.get(u'matrices', {}).get(name)
        if not o or o.get(u'sha') != m.get(u'sha'):
            notes.append(u'матрица %s (%s): %s → %s' % (name, m.get(u'from'), (o or {}).get(u'sha'), m.get(u'sha')))
    if old.get(u'spectrum_sha') != now.get(u'spectrum_sha'):
        notes.append(u'спектр %s: %s → %s' % (now.get(u'spectrum'), old.get(u'spectrum_sha'), now.get(u'spectrum_sha')))
    return notes


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--probes', default=None)
    ap.add_argument('--only', default=None)
    ap.add_argument('--snapshot', action='store_true')
    ap.add_argument('--selftest', action='store_true')
    ap.add_argument('--rel', type=float, default=None)
    ap.add_argument('--abs', dest='abs_tol', type=float, default=None)
    ap.add_argument('--skip-freshness', action='store_true')
    ap.add_argument('--reference', default=None)
    args = ap.parse_args(argv)
    global REFERENCE
    if args.reference:
        REFERENCE = os.path.abspath(args.reference)

    t_all = time.time()
    manifest = load_manifest()
    members = manifest[u'members']
    if args.only:
        wanted = [s.strip() for s in args.only.split(u',') if s.strip()]
        unknown = [w for w in wanted if w not in [m[u'key'] for m in members]]
        if unknown:
            say(u'ОТКАЗ: в витрине нет %s' % u', '.join(unknown))
            return 2
        members = [m for m in members if m[u'key'] in wanted]
    if args.selftest:
        members = [m for m in members if m[u'key'] == u'ASN16_Cs137_house']
        if not members:
            say(u'ОТКАЗ: самопроверке нужен член ASN16_Cs137_house')
            return 2

    inputs, code = stand(args, manifest, members)
    if code:
        return code

    say(u'прогон FsaStackShot (%d спектров, %d пар):' % (len(members), sum(len(m[u'modes']) for m in members)))
    results, err = run_pairs(manifest, members, REL_DEFAULT, ABS_DEFAULT)
    if err:
        say(u'⛔ СТЕНД: ' + err)
        return 2

    if args.snapshot:
        return snapshot(manifest, members, results, inputs, args)
    if args.selftest:
        return selftest(manifest, members, results, inputs, args)

    # штатный ход: сравнить с эталоном
    total = 0
    missing = []
    for member in members:
        for mode in member[u'modes']:
            ref = load_ref(member[u'key'], mode)
            if ref is None:
                missing.append(u'%s__%s' % (member[u'key'], mode))
                continue
            rel = args.rel if args.rel is not None else ref.get(u'tolerance', {}).get(u'rel', REL_DEFAULT)
            abs_tol = args.abs_tol if args.abs_tol is not None else ref.get(u'tolerance', {}).get(u'abs', ABS_DEFAULT)
            now = results[(member[u'key'], mode)][0]
            diffs = compare(ref, now, rel, abs_tol)
            drift = describe_inputs_drift(ref, inputs, member[u'key'])
            if diffs:
                total += len(diffs)
                say(u'')
                say(u'⛔ КАРТИНКА ИЗМЕНИЛАСЬ: %s / %s — %d расхождений (допуск rel %g, abs %g; эталон объявлен %s)'
                    % (member[u'key'], mode, len(diffs), rel, abs_tol, ref.get(u'declared')))
                for d in drift:
                    say(u'  ⚠ вход изменился: ' + d)
                print_diffs(member[u'key'], mode, diffs)
            else:
                say(u'  ✅ %s / %s: совпало с эталоном (%s)' % (member[u'key'], mode, ref.get(u'declared')))
                for d in drift:
                    say(u'     ⚠ вход изменился, а картинка нет: ' + d)
    if missing:
        say(u'⛔ ЭТАЛОНА НЕТ: %s — снять: pwsh tools\\fsa_showcase\\snapshot.ps1' % u', '.join(missing))
        return 4
    say(u'')
    if total:
        say(u'ОТКАЗ: картинка FSA на витрине разошлась с эталоном (%d строк). Это либо дефект отображения,'
            % total)
        say(u'       либо правка, которую принимают ПЕРЕОБЪЯВЛЕНИЕМ эталона: tools\\fsa_showcase\\snapshot.ps1')
        say(u'       (таблица выше — в отчёт полосы). Снимки: tools\\fsa_showcase\\wd\\out\\*.png. %.0f с.' % (time.time() - t_all))
        return 1
    say(u'ВИТРИНА СОШЛАСЬ С ЭТАЛОНОМ: %d пар, %.0f с.' % (len(results), time.time() - t_all))
    return 0


def snapshot(manifest, members, results, inputs, args):
    if args.skip_freshness and os.path.normcase(REFERENCE) == os.path.normcase(REFERENCE_DEFAULT):
        say(u'⛔ эталон git (%s) с --skip-freshness не снимается: свежесть каталога проб не судилась;' % REFERENCE_DEFAULT)
        say(u'   для стенда полосы — свой каталог эталона ключом --reference=<каталог>')
        return 3
    if args.skip_freshness:
        say(u'⚠ эталон снимается в сторонний каталог %s БЕЗ сторожа свежести — стендовый, не для git' % REFERENCE)
    if not os.path.isdir(REFERENCE):
        os.makedirs(REFERENCE)
    head = git_head()
    declared = time.strftime(u'%Y-%m-%d %H:%M')
    changed = 0
    for member in members:
        for mode in member[u'modes']:
            now, dt = results[(member[u'key'], mode)]
            old = load_ref(member[u'key'], mode)
            rel, abs_tol = REL_DEFAULT, ABS_DEFAULT
            if old is not None:
                diffs = compare(old, now, rel, abs_tol)
                if diffs:
                    changed += 1
                    say(u'')
                    say(u'ПЕРЕОБЪЯВЛЕНИЕ %s / %s: %d расхождений с прежним эталоном (%s):'
                        % (member[u'key'], mode, len(diffs), old.get(u'declared')))
                    print_diffs(member[u'key'], mode, diffs)
                else:
                    say(u'  %s / %s: числа те же, что в эталоне %s (переписано клеймо)' % (member[u'key'], mode, old.get(u'declared')))
            else:
                say(u'  %s / %s: эталон снят ВПЕРВЫЕ' % (member[u'key'], mode))
            entry = {
                u'member': member[u'key'], u'mode': mode, u'spectrum': member[u'spectrum'],
                u'args': member[u'modes'][mode], u'matrix': member.get(u'matrix'),
                u'declared': declared, u'head': head, u'seconds': round(dt, 1),
                u'tolerance': {u'rel': rel, u'abs': abs_tol},
                u'inputs': inputs_for(inputs, member[u'key']),
                u'bands_kev': manifest[u'bands_kev'],
                u'layers': now[u'layers'], u'head_lines': now[u'head'],
                u'bands': now[u'bands'], u'rates': now[u'rates'], u'rows': now[u'rows'], u'screen': now[u'screen'],
            }
            with io.open(ref_path(member[u'key'], mode), 'w', encoding='utf-8', newline='\n') as fh:
                json.dump(entry, fh, ensure_ascii=False, indent=1, sort_keys=True)
                fh.write(u'\n')
    say(u'')
    say(u'ЭТАЛОН ОБЪЯВЛЕН %s (HEAD %s): %d пар, из них с изменившимися числами %d — %s'
        % (declared, head, len(results), changed, REFERENCE))
    return 0


def selftest(manifest, members, results, inputs, args):
    u"""Обе стороны: тот же прогон обязан пройти; яма AMBER30 и подсаженный эталон — отказать."""
    member = members[0]
    mode = list(member[u'modes'].keys())[0]
    ref = load_ref(member[u'key'], mode)
    if ref is None:
        say(u'⛔ самопроверке нужен эталон %s — сперва snapshot.ps1' % ref_path(member[u'key'], mode))
        return 4
    rel, abs_tol = ref.get(u'tolerance', {}).get(u'rel', REL_DEFAULT), ref.get(u'tolerance', {}).get(u'abs', ABS_DEFAULT)
    ok = True
    # (в) тот же прогон — обязан пройти
    diffs = compare(ref, results[(member[u'key'], mode)][0], rel, abs_tol)
    if diffs:
        ok = False
        say(u'⛔ (в) штатный прогон разошёлся с эталоном — самопроверка не о том стенде (%d строк):' % len(diffs))
        print_diffs(member[u'key'], mode, diffs, 20)
    else:
        say(u'✅ (в) штатный прогон совпал с эталоном')
    # (а) яма AMBER30
    say(u'(а) прогон с --tail-as-residual (яма AMBER30)…')
    hole, err = run_pairs(manifest, [member], rel, abs_tol, {member[u'key']: [u'--tail-as-residual']}, u'__selftest_hole')
    if err:
        say(u'⛔ (а) проба с --tail-as-residual не отработала: ' + err)
        return 2
    diffs = compare(ref, hole[(member[u'key'], mode)][0], rel, abs_tol)
    band_hit = [d for d in diffs if d[0] == u'30-100 кэВ']
    if diffs and band_hit:
        say(u'✅ (а) яма AMBER30 поймана: %d расхождений, из них в полосе 30–100 кэВ %d; первые:' % (len(diffs), len(band_hit)))
        print_diffs(member[u'key'], mode, band_hit, 12)
    else:
        ok = False
        say(u'⛔ (а) яма AMBER30 НЕ поймана: расхождений %d, в полосе 30–100 кэВ %d' % (len(diffs), len(band_hit)))
    # (б) подсаженный эталон
    planted = json.loads(json.dumps(ref))
    comp = None
    for name, entry in planted.get(u'rates', {}).get(u'component', {}).items():
        if u'share_pct' in entry:
            entry[u'share_pct'] = entry[u'share_pct'] + 1.0
            comp = name
            break
    band = planted[u'bands'].get(u'300-700')
    layer = None
    if band:
        for k in planted.get(u'layers', []):
            if k in band and band[k]:
                band[k] = band[k] * 1.01
                layer = k
                break
    diffs = compare(planted, results[(member[u'key'], mode)][0], rel, abs_tol)
    names = set(d[1] for d in diffs)
    want = set()
    if comp:
        want.add(comp + u'.share_pct')
    if layer:
        want.add(layer)
    if diffs and want and want <= names:
        say(u'✅ (б) подсаженный эталон (%s +1 %%, слой %s в 300–700 кэВ ×1.01) отвергнут: %d расхождений'
            % (comp, layer, len(diffs)))
    else:
        ok = False
        say(u'⛔ (б) подсаженный эталон НЕ отвергнут: расхождений %d, ждали %s' % (len(diffs), u', '.join(sorted(want))))
    say(u'')
    say(u'САМОПРОВЕРКА %s' % (u'ПРОШЛА: сторож проходит на своём эталоне и отказывает на яме и на подсадке'
                               if ok else u'НЕ ПРОШЛА'))
    return 0 if ok else 1


def git_head():
    try:
        proc = subprocess.run([u'git', u'rev-parse', u'--short', u'HEAD'], cwd=REPO,
                              stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        return proc.stdout.decode('ascii', 'replace').strip() or u'?'
    except OSError:
        return u'?'


if __name__ == '__main__':
    sys.exit(main())
