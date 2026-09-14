# П37 13.09.2026 — кривые всех 45 сцен одной физикой (17) — ПОСЛЕ склада: `CorpusEffProbe --force`
# в worktree (как П24 §2 для kdip=1): считает кривую (200 000 историй на узел, умолчания
# EfficiencyCalculationOptions), кладёт <ключ>.rmx под guid в geometries\response\ и вставляет узел
# <Efficiency> в каждый спектр геометрии (tools\CORPUS\corpus\spectra\*.xml worktree). Клеймо кривой
# обязано нести phys=17; kdip=1; lys=2; etr=1; e+tr=1; e+off=1; rayl2=1. Основного дерева не касается.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp37'
$bin = "$wt\tools\effmaker\probes\build_p37"
$art = "$root\handover\p37-store"
$store = "$wt\tools\CORPUS\corpus\geometries"
"curves start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'); rmx in store: $((Get-ChildItem $store -Filter *.rmx).Count)" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusEffProbe.exe" "--dir=$store" "--spectra=$wt\tools\CORPUS\corpus\spectra" --force > "$art\curves.log" 2>&1
"curves code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
