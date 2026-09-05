# ═══════════════════════════════════════════════════════════════════════════
# F46, 06.09.2026. ПРИЁМКА ОТПЕЧАТКА НАБОРА ИСХОДНИКОВ (`T226`, `T138`, `T233`).
#
# Мерится ТЕНЕВОЕ дерево, а не рабочее: правки в `BecquerelMonitor\**` полосе
# запрещены, а главное — в дереве идут соседние полосы, и замер, трогающий
# общие файлы, мерил бы смесь поколений. Тень — побайтовая копия исходников;
# что она верна, доказывается ОТДЕЛЬНО: отпечаток тени обязан совпасть с
# отпечатком, записанным в отметку каталога проб при заверении рабочим деревом.
#
# Каждое состояние судится ДВУМЯ сторожами — старым (из `HEAD`) и новым (из
# рабочего дерева), каждый в СВОЁМ процессе. Иначе второй `.` перекрыл бы
# функции первого, и замер мерил бы один сторож дважды.
param(
    [string]$Bin        = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\bin\Debug_F46',
    [string]$ProbeBuild = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\build_f46',
    [string]$AltBin     = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\bin\Debug_F45'
)
$ErrorActionPreference = 'Stop'
$repo  = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$scr   = Join-Path $repo 'handover\f46-stamp'
$tmp   = Join-Path $env:TEMP ('f46_' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$shadow = Join-Path $tmp 'repo'
New-Item -ItemType Directory -Force $shadow | Out-Null

$guardNew = Join-Path $repo 'tools\CORPUS\scripts\appwd_plan.ps1'
$guardOld = Join-Path $tmp  'appwd_plan_HEAD.ps1'
Push-Location $repo
& git show HEAD:tools/CORPUS/scripts/appwd_plan.ps1 | Set-Content -LiteralPath $guardOld -Encoding utf8
Pop-Location
if (-not (Test-Path $guardOld)) { throw 'старый сторож из HEAD не достался' }

# ── ТЕНЬ ────────────────────────────────────────────────────────────────────
# Copy-Item переносит LastWriteTime источника — здесь это НУЖНО: времена правок
# обязаны остаться теми же, иначе всё дерево окажется новее сборки и первый же
# отрицательный контроль не сойдётся по причине, не имеющей отношения к делу.
function Copy-Tree {
    param([string]$RelDir, [string]$Filter, [switch]$Recurse)
    $src = Join-Path $repo $RelDir
    if (-not (Test-Path -LiteralPath $src)) { return }
    $files = if ($Recurse) {
        Get-ChildItem -LiteralPath $src -Recurse -File -Force -Filter $Filter -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    } else {
        Get-ChildItem (Join-Path $src $Filter) -File -Force -ErrorAction SilentlyContinue
    }
    $prefix = (Resolve-Path -LiteralPath $src).Path.TrimEnd('\') + '\'
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($prefix.Length)
        $dst = Join-Path (Join-Path $shadow $RelDir) $rel
        New-Item -ItemType Directory -Force (Split-Path -Parent $dst) | Out-Null
        Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
    }
}
Copy-Tree -RelDir 'BecquerelMonitor'            -Filter '*.cs'     -Recurse
Copy-Tree -RelDir 'BecquerelMonitor'            -Filter '*.csproj'
Copy-Tree -RelDir 'BecquerelMonitor\config'     -Filter '*.xml'    -Recurse
Copy-Tree -RelDir 'config'                      -Filter '*.xml'    -Recurse
Copy-Tree -RelDir 'tools\effmaker'              -Filter '*.cs'
Copy-Tree -RelDir 'tools\effmaker\probes'       -Filter '*.cs'
New-Item -ItemType Directory -Force (Join-Path $shadow 'tools\CORPUS\corpus\geometries\response') | Out-Null

# ── ОЦЕНКА ──────────────────────────────────────────────────────────────────
$evalPs = Join-Path $scr 'f46_eval.ps1'
function Ask {
    param([string]$Guard, [switch]$Fp, [string]$UseBin = '')
    $b = if ($UseBin) { $UseBin } else { $Bin }
    $a = @('-NoProfile', '-File', $evalPs, '-Guard', $Guard, '-Repo', $shadow, '-Bin', $b, '-ProbeBuild', $ProbeBuild)
    if ($Fp) { $a += '-Fingerprint' }
    $out = & pwsh @a 2>&1
    $o = [pscustomobject]@{
        Count = -1; Bad = @(); Note = @(); Raw = @($out); Fp = @{}
    }
    foreach ($l in $out) {
        $s = [string]$l
        if ($s -like 'COUNT=*')  { $o.Count = [int]$s.Substring(6) }
        elseif ($s -like 'BAD=*')   { $o.Bad  += $s.Substring(4) }
        elseif ($s -like 'NOTE=*')  { $o.Note += $s.Substring(5) }
        elseif ($s -like 'FP_EACH=*') { $p = $s.Substring(8).Split('='); $o.Fp['each:' + $p[0]] = $p[1] }
        elseif ($s -like 'FP_*' -or $s -like 'N_*') { $p = $s.Split('=', 2); $o.Fp[$p[0]] = $p[1] }
    }
    $o
}
function New-Both { param([string]$UseBin = '')
    [pscustomobject]@{ Old = (Ask -Guard $guardOld -UseBin $UseBin); New = (Ask -Guard $guardNew -UseBin $UseBin) }
}

# ── ДОКАЗАТЕЛЬСТВО, ЧТО ТЕНЬ ВЕРНА ──────────────────────────────────────────
# Отпечаток тени против ЗАПИСАННОГО в отметку при заверении рабочим деревом.
# Не сойдётся — дальше мерить нечего: тень другая, и все выводы были бы про неё.
$stampFile = Join-Path $ProbeBuild '.appwd.json'
$stamp = Get-Content -LiteralPath $stampFile -Raw | ConvertFrom-Json
$fp0 = Ask -Guard $guardNew -Fp
Write-Host '=== 0. ТЕНЬ ПРОТИВ ЗАВЕРЕННОГО (положительный контроль самой тени) ==='
Write-Host ('  приложение: отметка {0}  тень {1}  {2}' -f $stamp.sources.app.fp.Substring(0,16), $fp0.Fp['FP_APP'].Substring(0,16),
            $(if ($stamp.sources.app.fp -eq $fp0.Fp['FP_APP']) { 'СОШЛОСЬ' } else { '⛔ РАЗОШЛОСЬ' }))
Write-Host ('  пробы     : отметка {0}  тень {1}  {2}' -f $stamp.sources.probes.fp.Substring(0,16), $fp0.Fp['FP_PROBES'].Substring(0,16),
            $(if ($stamp.sources.probes.fp -eq $fp0.Fp['FP_PROBES']) { 'СОШЛОСЬ' } else { '⛔ РАЗОШЛОСЬ' }))
Write-Host ('  файлов    : приложение {0}, пробы {1}, вне проекта {2}' -f $fp0.Fp['N_APP'], $fp0.Fp['N_PROBES'], $fp0.Fp['N_LOOSE'])
if ($stamp.sources.app.fp -ne $fp0.Fp['FP_APP'] -or $stamp.sources.probes.fp -ne $fp0.Fp['FP_PROBES']) {
    Write-Host '⛔ ТЕНЬ НЕ ВЕРНА — замер прекращён' -ForegroundColor Red
    return
}

# ── СЛУЧАИ ──────────────────────────────────────────────────────────────────
$appProj  = Join-Path $shadow 'BecquerelMonitor\BecquerelMonitor.csproj'
$appCs    = Join-Path $shadow 'BecquerelMonitor\Peak.cs'
$appAlien = Join-Path $shadow 'BecquerelMonitor\VneProektaF46.cs'
$probeMy  = Join-Path $shadow 'tools\effmaker\probes\CorpusFsaProbe.cs'
$probeOth = Join-Path $shadow 'tools\effmaker\probes\DoseRateProbe.cs'
$probeNew = Join-Path $shadow 'tools\effmaker\probes\SosedF46Probe.cs'
$comp     = Join-Path $shadow 'tools\effmaker\probes\ProbeDeviceConfig.cs'
foreach ($f in @($appCs, $probeMy, $probeOth, $comp)) {
    if (-not (Test-Path -LiteralPath $f)) { throw "нет файла тени: $f" }
}
$keep = @{}
foreach ($f in @($appCs, $appProj, $probeMy, $probeOth, $comp)) {
    $keep[$f] = [pscustomobject]@{ Bytes = [IO.File]::ReadAllBytes($f); Time = (Get-Item -LiteralPath $f).LastWriteTime }
}
function Restore-All {
    foreach ($f in $keep.Keys) {
        [IO.File]::WriteAllBytes($f, $keep[$f].Bytes)
        (Get-Item -LiteralPath $f).LastWriteTime = $keep[$f].Time
    }
    foreach ($f in @($appAlien, $probeNew)) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
}
function Touch { param([string]$P) (Get-Item -LiteralPath $P).LastWriteTime = (Get-Date) }
function Poke { param([string]$P)   # правка содержимого С СОХРАНЕНИЕМ времени
    $t = (Get-Item -LiteralPath $P).LastWriteTime
    Add-Content -LiteralPath $P -Value '// f46' -Encoding utf8
    (Get-Item -LiteralPath $P).LastWriteTime = $t
}
function PokeNew { param([string]$P) # правка содержимого И времени
    Add-Content -LiteralPath $P -Value '// f46' -Encoding utf8
    (Get-Item -LiteralPath $P).LastWriteTime = (Get-Date)
}

$cases = @(
    @{ Id='D0';  Want=0;  What='ЦЕЛАЯ тень, ничего не тронуто (отрицательный контроль)'; Do={} }
    @{ Id='A1';  Want=0;  What='чужая НОВАЯ проба .cs с Main, exe НЕТ, время СЕЙЧАС (T165/T182)'
       Do={ Set-Content -LiteralPath $probeNew -Encoding utf8 -Value 'class SosedF46 { static int Main() { return 0; } }' } }
    @{ Id='A2';  Want=0;  What='.cs в BecquerelMonitor\ ВНЕ проекта, время СЕЙЧАС (T226)'
       Do={ Set-Content -LiteralPath $appAlien -Encoding utf8 -Value 'class VneProektaF46 {}' } }
    @{ Id='A3';  Want=0;  What='файл ПРИЛОЖЕНИЯ тронут (время СЕЙЧАС), содержимое ТО ЖЕ (T194/T226)'
       Do={ Touch $appCs } }
    @{ Id='A4';  Want=0;  What='.cs ЧУЖОЙ пробы тронут (время СЕЙЧАС), содержимое ТО ЖЕ (T182)'
       Do={ Touch $probeOth } }
    @{ Id='A5';  Want=0;  What='ДОВЕСОК тронут (время СЕЙЧАС), содержимое ТО ЖЕ'
       Do={ Touch $comp } }
    @{ Id='B1';  Want=1;  What='содержимое файла ПРИЛОЖЕНИЯ подменено, время СОХРАНЕНО (T233)'
       Do={ Poke $appCs } }
    @{ Id='B2';  Want=1;  What='содержимое ЧУЖОЙ пробы подменено, время СОХРАНЕНО (T233)'
       Do={ Poke $probeOth } }
    @{ Id='B3';  Want=1;  What='ДОВЕСОК подменён, время СОХРАНЕНО — обязан красить пробы (T233)'
       Do={ Poke $comp } }
    @{ Id='B4';  Want=1;  What='проект .csproj подменён, время СОХРАНЕНО (T233)'
       Do={ Poke $appProj } }
    @{ Id='C1';  Want=1;  What='исходник ПРИЛОЖЕНИЯ правлен и НОВЕЕ сборки (B20/B21, T41)'
       Do={ PokeNew $appCs } }
    @{ Id='C2';  Want=1;  What='исходник СВОЕЙ пробы правлен и НОВЕЕ своего exe (T41)'
       Do={ PokeNew $probeMy } }
    @{ Id='C3';  Want=1;  What='ДОВЕСОК правлен и НОВЕЕ проб (входит в набор КАЖДОЙ)'
       Do={ PokeNew $comp } }
)

$rows = @()
foreach ($c in $cases) {
    Restore-All
    & $c.Do
    $r = New-Both
    $rows += [pscustomobject]@{
        Id = $c.Id; What = $c.What; Want = $c.Want
        Old = $r.Old.Count; New = $r.New.Count
        Ok  = $(if ($c.Want -eq 0) { $r.New.Count -eq 0 } else { $r.New.Count -ge 1 })
        BadNew = $r.New.Bad; NoteNew = $r.New.Note
    }
}
Restore-All

# B5: приложение в -Bin ЧУЖОЕ — сверка ДВОИЧНОГО файла с заверенным (`T138`).
if (Test-Path (Join-Path $AltBin 'BecquerelMonitor.exe')) {
    $r = New-Both -UseBin $AltBin
    $rows += [pscustomobject]@{
        Id='B5'; What='приложение в -Bin ЧУЖОЕ (сборка другой полосы) — T138'; Want=1
        Old=$r.Old.Count; New=$r.New.Count; Ok=($r.New.Count -ge 1); BadNew=$r.New.Bad; NoteNew=$r.New.Note }
}
# B6: подменён САМ ДВОИЧНЫЙ ФАЙЛ ПРОБЫ, время сохранено. Исходники при этом
#     сходятся с заверенными до байта — время и набор молчат оба.
$exeA = Join-Path $ProbeBuild 'DoseRateProbe.exe'
$exeB = Join-Path $ProbeBuild 'RoiLoadProbe.exe'
if ((Test-Path $exeA) -and (Test-Path $exeB)) {
    $keepBytes = [IO.File]::ReadAllBytes($exeA)
    $keepTime  = (Get-Item -LiteralPath $exeA).LastWriteTime
    [IO.File]::WriteAllBytes($exeA, [IO.File]::ReadAllBytes($exeB))
    (Get-Item -LiteralPath $exeA).LastWriteTime = $keepTime
    $r = New-Both
    [IO.File]::WriteAllBytes($exeA, $keepBytes)
    (Get-Item -LiteralPath $exeA).LastWriteTime = $keepTime
    $rows += [pscustomobject]@{
        Id='B6'; What='САМ <проба>.exe подменён, время СОХРАНЕНО (T138/T233)'; Want=1
        Old=$r.Old.Count; New=$r.New.Count; Ok=($r.New.Count -ge 1); BadNew=$r.New.Bad; NoteNew=$r.New.Note }
}
# C4: отметки НЕТ вовсе — обязано работать прежнее правило времени (`T41`).
Move-Item -LiteralPath $stampFile -Destination ($stampFile + '.f46') -Force
try {
    PokeNew $appCs
    $r = New-Both
    $rows += [pscustomobject]@{
        Id='C4'; What='отметки НЕТ, исходник приложения новее сборки — запасное правило T41'; Want=1
        Old=$r.Old.Count; New=$r.New.Count; Ok=($r.New.Count -ge 1); BadNew=$r.New.Bad; NoteNew=$r.New.Note }
    Restore-All
    $r = New-Both
    $rows += [pscustomobject]@{
        Id='D1'; What='отметки НЕТ, дерево цело — запасное правило молчит'; Want=0
        Old=$r.Old.Count; New=$r.New.Count; Ok=($r.New.Count -eq 0); BadNew=$r.New.Bad; NoteNew=$r.New.Note }
} finally {
    Move-Item -LiteralPath ($stampFile + '.f46') -Destination $stampFile -Force
}

Write-Host ''
Write-Host '=== СВОДКА: находок у СТАРОГО и НОВОГО сторожа на одном состоянии ==='
Write-Host ('{0,-5} {1,-4} {2,-4} {3,-6} {4}' -f 'ID', 'СТАР', 'НОВ', 'ждали', 'состояние')
Write-Host ('-' * 118)
foreach ($r in $rows) {
    $w = if ($r.Want -eq 0) { '0' } else { '>=1' }
    Write-Host ('{0,-5} {1,-4} {2,-4} {3,-6} {4}  {5}' -f $r.Id, $r.Old, $r.New, $w, $r.What, $(if ($r.Ok) { '' } else { '⛔ НЕ СОШЛОСЬ' }))
}
$bad = @($rows | Where-Object { -not $_.Ok })
Write-Host ''
if ($bad.Count -eq 0) { Write-Host ('✅ ВСЕ {0} СЛУЧАЕВ СОШЛИСЬ С ОЖИДАНИЕМ' -f $rows.Count) }
else { Write-Host ('⛔ НЕ СОШЛОСЬ: {0} из {1}' -f $bad.Count, $rows.Count) -ForegroundColor Red }

Write-Host ''
Write-Host '=== ЧТО ИМЕННО СКАЗАЛ НОВЫЙ СТОРОЖ ==='
foreach ($r in $rows) {
    if ($r.BadNew.Count -eq 0 -and $r.NoteNew.Count -eq 0) { continue }
    Write-Host ('  {0}:' -f $r.Id)
    foreach ($b in $r.BadNew)  { Write-Host ('     ОТКАЗ: ' + $b) }
    foreach ($n in $r.NoteNew) { Write-Host ('     ⚠ ' + $n) }
}

# ── ОТПЕЧАТОК: меняется РОВНО с набором ─────────────────────────────────────
Write-Host ''
Write-Host '=== ОТПЕЧАТОК НАБОРА: до и после ==='
$f = @{}
Restore-All;                 $f['1 исходный набор']            = (Ask -Guard $guardNew -Fp)
Touch $appCs;                $f['2 файл ТРОНУТ (только время)'] = (Ask -Guard $guardNew -Fp)
Restore-All; Poke $appCs;    $f['3 содержимое ПРАВЛЕНО']        = (Ask -Guard $guardNew -Fp)
Restore-All;                 $f['4 правка ОТКАЧЕНА']            = (Ask -Guard $guardNew -Fp)
Set-Content -LiteralPath $probeNew -Encoding utf8 -Value 'class SosedF46 { static int Main() { return 0; } }'
$f['5 чужая НОВАЯ проба заведена'] = (Ask -Guard $guardNew -Fp)
Restore-All
$base = $f['1 исходный набор']
foreach ($k in ($f.Keys | Sort-Object)) {
    $v = $f[$k]
    Write-Host ('  {0,-32} приложение {1}  {2}' -f $k, $v.Fp['FP_APP'].Substring(0,16),
                $(if ($v.Fp['FP_APP'] -eq $base.Fp['FP_APP']) { 'ТОТ ЖЕ' } else { 'ИНОЙ' }))
}
Write-Host ''
Write-Host '  отпечаток НАБОРА КАЖДОЙ ПРОБЫ при заведении чужой новой пробы:'
$e5 = $f['5 чужая НОВАЯ проба заведена']
$same = 0; $diff = 0
foreach ($k in $base.Fp.Keys) {
    if ($k -notlike 'each:*') { continue }
    if ($e5.Fp.ContainsKey($k) -and $e5.Fp[$k] -eq $base.Fp[$k]) { $same++ } else { $diff++ }
}
Write-Host ('     совпало у {0} проб, разошлось у {1}; общий отпечаток проб {2}' -f $same, $diff,
            $(if ($e5.Fp['FP_PROBES'] -eq $base.Fp['FP_PROBES']) { 'ТОТ ЖЕ' } else { 'ИНОЙ (в наборе стало больше файлов)' }))

Write-Host ''
Write-Host ('тень: {0}' -f $shadow)
Write-Host ('старый сторож: HEAD -> {0}' -f $guardOld)
