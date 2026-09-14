# Прогон `PeakOriginProbe` по ВСЕМУ корпусу: 69 спектров, 23 группы детекторов.
# Считает, сколько найденных пиков объясняются устройством спектрометра —
# вылетом, суммированием или обратным рассеянием, — а не линией нуклида.
#
# Мерить на подмножестве нельзя (см. CLAUDE.md): корпус растянут от 0.22 %
# (HPGe) до 15 % (Obsidian) полуширины, и признак, настроенный по середине,
# виден только на краях.
#
#   pwsh tools/effmaker/run_peakorigin.ps1 [-SkipBuild] [-Groups ASN16,HPGE]
#
# Итог — `tools/effmaker/out/peak_origin.csv`, по строке на объяснённый пик.

param(
    [switch]$SkipBuild,
    [string[]]$Groups,
    [string]$OutDir = "$PSScriptRoot\out"
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot\..\.."
$wdRoot = Join-Path $repo 'tools\CORPUS\scripts'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'

$all = Get-ChildItem $wdRoot -Directory -Filter 'wd_*' |
       ForEach-Object { [pscustomobject]@{ Name = $_.Name.Substring(3); Wd = $_.FullName } }
# При запуске через `pwsh -File` список приезжает одной строкой «A,B,C» —
# разворачиваем, иначе ни одна группа не найдётся.
$wanted = if ($Groups) { $Groups -split ',' | ForEach-Object { $_.Trim() } } else { $null }
$list = if ($wanted) { $all | Where-Object { $wanted -contains $_.Name } } else { $all }
if (-not $list) { throw "нет таких групп: $($wanted -join ',')" }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$csv = Join-Path $OutDir 'peak_origin.csv'
if (Test-Path $csv) { Remove-Item $csv }

if (-not $SkipBuild) {
    $ref = $all[0].Wd
    $exe = Join-Path $ref 'peakoriginprobe.exe'
    & $csc /nologo /target:exe /platform:anycpu /langversion:7.3 `
        "/out:$exe" "/r:$ref\BecquerelMonitor.exe" `
        "/r:$ref\Microsoft.Data.Sqlite.dll" "/r:$ref\WeifenLuo.WinFormsUI.Docking.dll" `
        /r:netstandard.dll /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
        /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        "$repo\tools\effmaker\probes\PeakOriginProbe.cs"
    if ($LASTEXITCODE -ne 0) { throw "csc failed" }
    foreach ($g in $all | Select-Object -Skip 1) {
        Copy-Item $exe (Join-Path $g.Wd 'peakoriginprobe.exe') -Force
    }
    foreach ($g in $all) {
        # Без перенаправлений сборок из конфига приложения Microsoft.Data.Sqlite
        # не поднимается; нативная e_sqlite3 ищется в runtimes\ рядом с exe.
        Copy-Item (Join-Path $g.Wd 'BecquerelMonitor.exe.config') `
                  (Join-Path $g.Wd 'peakoriginprobe.exe.config') -Force
    }
    Write-Host "built: $exe"
}

foreach ($g in $list) {
    Push-Location $g.Wd
    try {
        & '.\peakoriginprobe.exe' --spectra=spectra --group=$($g.Name) --csv=$csv
    } finally {
        Pop-Location
    }
}

Write-Host ""
Write-Host "записано $csv"
