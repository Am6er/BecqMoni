# gen_cfg.ps1 — пересоздать `cfg.h` из открытых простых полей `EfficiencySimulator` (`AMBER160`, П221;
# скрипт заведён П245 07.10.2026 — `cfg.h` и README на него ссылались, а в дереве его не было).
#
#   pwsh tools\effmaker\gpu\gen_cfg.ps1 [-Exe <путь к BecquerelMonitor.exe>] [-Check]
#
# Правило — то же, что у упаковщика `GpuPack.Settings` (BecquerelMonitor\EfficiencyMaker\GpuPack.cs):
# все ОТКРЫТЫЕ ЭКЗЕМПЛЯРНЫЕ поля примитивных типов, кроме счётчиков-выходов `Count*`/`Sum*`/
# `Last*`/`Weight*`, в порядке объявления. `bool` → B, `int` → I, `long` → L, `double` → D.
# Поле другого примитивного типа — отказ: натив таких не читает (`rm_cfg_set`).
#
# `-Check` — не писать, а сверить с нынешним `cfg.h` (код 1 при расхождении): так ловится
# поле, заведённое в C# после последней генерации, ДО того, как `rm_cfg_set` откажет при счёте.
param(
    [string]$Exe = '',
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $dir '..\..\..')).Path
if ($Exe -eq '') { $Exe = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex\BecquerelMonitor.exe' }
if (-not (Test-Path -LiteralPath $Exe)) { Write-Host "нет $Exe — сначала соберите приложение"; exit 2 }

$asm = [System.Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Exe).Path)
$type = $asm.GetType('BecquerelMonitor.EfficiencyMaker.EfficiencySimulator', $true)
$flags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Public
$kinds = @{ 'System.Boolean' = 'B'; 'System.Int32' = 'I'; 'System.Int64' = 'L'; 'System.Double' = 'D' }
$ctypes = @{ 'B' = 'bool'; 'I' = 'int'; 'L' = 'long long'; 'D' = 'double' }
$fields = @()
foreach ($f in $type.GetFields($flags)) {
    if (-not $f.FieldType.IsPrimitive) { continue }
    $n = $f.Name
    if ($n.StartsWith('Count') -or $n.StartsWith('Sum') -or $n.StartsWith('Last') -or $n.StartsWith('Weight')) { continue }
    $k = $kinds[$f.FieldType.FullName]
    if ($null -eq $k) { Write-Host "поле $n типа $($f.FieldType.FullName) — натив такого вида не знает"; exit 1 }
    $fields += [pscustomobject]@{ Name = $n; Kind = $k }
}

$lines = @(
    '// cfg.h — настройки симулятора для GPU. СГЕНЕРИРОВАНО отражением из открытых полей',
    '// `EfficiencySimulator` (всё простое, кроме счётчиков-выходов Count*/Sum*/Last*/Weight*).',
    '// Пересоздать: tools/effmaker/gpu/gen_cfg.ps1. Упаковщик GpuPack.cs ставит КАЖДОЕ поле по имени;',
    '// поле C#, которого здесь нет, и поле здесь, которого нет в C#, — отказ (rm_cfg_set / rm_cfg_check).',
    '#pragma once',
    '#include <stdint.h>',
    'struct Cfg',
    '{'
)
foreach ($f in $fields) { $lines += ('    {0} {1};' -f $ctypes[$f.Kind], $f.Name) }
$lines += '};'
$lines += ''
$lines += '#define RM_CFG_FIELDS(X) \'
for ($i = 0; $i -lt $fields.Count; $i++) {
    $tail = if ($i -lt $fields.Count - 1) { ' \' } else { '' }
    $lines += ('    X({0}, {1}){2}' -f $fields[$i].Kind, $fields[$i].Name, $tail)
}
$text = ($lines -join "`n") + "`n"
$path = Join-Path $dir 'cfg.h'
if ($Check) {
    $have = [System.IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    if ($have -eq $text) { Write-Host ("cfg.h сходится с приложением: {0} полей" -f $fields.Count); exit 0 }
    Write-Host 'cfg.h РАСХОДИТСЯ с открытыми полями EfficiencySimulator — пересоздать gen_cfg.ps1 и перенести новое в ядро'
    $haveNames = [regex]::Matches($have, 'X\([BILD], (\w+)\)') | ForEach-Object { $_.Groups[1].Value }
    $wantNames = $fields | ForEach-Object { $_.Name }
    $only = Compare-Object -ReferenceObject @($haveNames) -DifferenceObject @($wantNames)
    foreach ($d in $only) { Write-Host ('  {0} {1}' -f ($(if ($d.SideIndicator -eq '=>') { 'нет в cfg.h:' } else { 'нет в C#:  ' })), $d.InputObject) }
    exit 1
}
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("cfg.h записан: {0} полей" -f $fields.Count)
exit 0
