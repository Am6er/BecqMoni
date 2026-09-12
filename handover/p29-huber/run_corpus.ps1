# П29 12.09.2026, `AMBER22` — плечи Хубера на МАЛОЙ БАЗЕ (43 понятных + 16 непонятных, диск AS80_Th232Medal
# внутри — mini.csv с П24). Стенд: worktree C:\Users\moroz\bqp29 (HEAD ebd3bd10, Release_P29, build_p29,
# склад .rmx — копия корпусного), оснастка wd_p29 там же (mk_appwd.ps1 без -Force).
# Запуск: pwsh -File <этот файл> [имена плеч]
# ⛔ run_mini.ps1 зовётся ОПЕРАТОРОМ `&` (T84: массив -Extra через командную строку схлопывается).
# Плечо `ctl` — умолчание (контроль = out_rev19_mini строка в строку, p24_keyed.py); остальные — ключ --huber=.
param([string[]]$Arms = @())
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp29'
$here = "$root\handover\p29-huber\corpus"
New-Item -ItemType Directory -Force $here | Out-Null
$wd = "$wt\tools\CORPUS\scripts\wd_p29"
$log = "$here\arms.log"

if (-not (Test-Path "$wd\CorpusFsaProbe.exe")) {
    & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_P29" -Wd $wd `
        -ProbeBuild "$wt\tools\effmaker\probes\build_p29" *> "$here\mk_appwd.log"
    "mk_appwd code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
    if ($LASTEXITCODE -ne 0) { exit 3 }
}

$all = [ordered]@{
    ctl   = @()
    h0    = @('--huber=0')
    h10   = @('--huber=10')
    h1    = @('--huber=1')
    h05   = @('--huber=0.5')
}
if ($Arms.Count -eq 0) { $Arms = @($all.Keys) }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $out = "$root\tools\pie\out_p29_$name"
    $curves = "$here\curves_$name"
    New-Item -ItemType Directory -Force $curves | Out-Null
    $extra = @("--dump-curves=$curves", '--residuals=12') + $all[$name]
    Set-Location $wt
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd $wd `
        -Bin "$wt\BecquerelMonitor\bin\Release_P29" -ProbeBuild "$wt\tools\effmaker\probes\build_p29" `
        -Extra $extra *> "$here\run_$name.log"
    "$name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') extra=[$($all[$name] -join ' ')]" | Out-File -Append $log
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
