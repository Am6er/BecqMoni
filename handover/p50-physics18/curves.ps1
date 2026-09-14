# П50 14.09.2026 — кривые всех 45 сцен одной физикой (18) — ПОСЛЕ склада: `CorpusEffProbe --force`
# в worktree (как П37 §6): считает кривую (200 000 историй на узел, умолчания EfficiencyCalculationOptions),
# кладёт <ключ>.rmx под guid в geometries\response\ и вставляет узел <Efficiency> в каждый спектр геометрии
# (tools\CORPUS\corpus\spectra\*.xml worktree). Клеймо кривой обязано нести
# phys=18; … kdip=1; lys=2; etr=1; e+tr=1; e+off=1; rayl2=1; ecomp=1; bpath=2. Основного дерева не касается.
# ⛔ ПЕРЕД счётом — пересборка проб не нужна: код заморожен с контролей (правка кода во время замера рвёт
# развёртку), каталог build_p50 тот же, что считал склад (sha exe в codes.txt).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$wt = 'D:\BqMoni_Claude\p50\wt'
$bin = "$wt\tools\effmaker\probes\build_p50"
$art = 'D:\BqMoni_Claude\p50\art'
$store = "$wt\tools\CORPUS\corpus\geometries"
"curves start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'); rmx in store: $((Get-ChildItem $store -Filter *.rmx).Count)" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusEffProbe.exe" "--dir=$store" "--spectra=$wt\tools\CORPUS\corpus\spectra" --force > "$art\curves.log" 2>&1
"curves code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
"curves end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
