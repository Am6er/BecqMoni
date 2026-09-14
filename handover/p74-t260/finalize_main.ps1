# П74 (T260): ПОСЛЕДНЯЯ МИЛЯ в ОСНОВНОМ дереве — после коммита S175 (П75), по слову распорядителя:
#   1. приложение Debug в bin\Debug_Codex (канонический рецепт CLAUDE.md, с /p:GenerateManifests=false);
#   2. build_all.ps1 в штатный tools\effmaker\probes\build (тот, что читает check_all);
#   3. snapshot.ps1 — эталон витрины в tools\fsa_showcase\reference (печатает diff против прежнего);
#   4. python tools\check_all.py — код 0.
# Каждый шаг — кодом возврата; отказ останавливает цепочку.
#   pwsh -File handover\p74-t260\finalize_main.ps1 [-SkipBuild]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
Set-Location $repo
"дерево: $repo, HEAD $(git rev-parse --short HEAD), незакоммиченного: $((git status --short | Measure-Object).Count) строк"
if (-not $SkipBuild) {
    & $msbuild "$repo\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Debug /p:Platform='AnyCPU' /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_Codex\' /v:m /nologo *> "$repo\handover\p74-t260\finalize_build.log"
    $b = $LASTEXITCODE
    "1. приложение Debug_Codex: код $b ($([int]$sw.Elapsed.TotalSeconds) с)"
    if ($b -ne 0) { Get-Content "$repo\handover\p74-t260\finalize_build.log" | Select-String 'error' | Select-Object -First 10; exit 1 }
    & pwsh -File "$repo\tools\effmaker\probes\build_all.ps1" *> "$repo\handover\p74-t260\finalize_build_all.log"
    $a = $LASTEXITCODE
    "2. build_all.ps1 -> tools\effmaker\probes\build: код $a ($([int]$sw.Elapsed.TotalSeconds) с)"
    Get-Content "$repo\handover\p74-t260\finalize_build_all.log" | Select-String 'заверен|все собрались|ОТКАЗ' | Select-Object -Last 3
    if ($a -ne 0) { exit 2 }
}
& "$repo\tools\fsa_showcase\snapshot.ps1" *> "$repo\handover\p74-t260\finalize_snapshot.log"
$s = $LASTEXITCODE
"3. snapshot.ps1: код $s ($([int]$sw.Elapsed.TotalSeconds) с)"
Get-Content "$repo\handover\p74-t260\finalize_snapshot.log" | Select-Object -Last 12
if ($s -ne 0) { exit 3 }
& python "$repo\tools\check_all.py" --quiet *> "$repo\handover\p74-t260\finalize_check_all.log"
$c = $LASTEXITCODE
"4. check_all.py: код $c ($([int]$sw.Elapsed.TotalSeconds) с)"
Get-Content "$repo\handover\p74-t260\finalize_check_all.log" | Select-String '⛔|ОСТАНОВ|ВСЕ ЗЕЛЕНЫ' | Select-Object -Last 6
exit $c
