# П34 12.09.2026, `A309` — плечи пола полосы ФИТА на МАЛОЙ БАЗЕ (43 понятных + 16 непонятных, диск внутри).
# Стенд: worktree C:\Users\moroz\bqp34 (HEAD + правило A309, Release_p34, build_p34, склад .rmx — копия
# корпусного), оснастка wd_p34 (mk_appwd.ps1). Запуск: pwsh -File <этот файл> [имена плеч]
# ⛔ run_mini.ps1 зовётся ОПЕРАТОРОМ `&` (T84). Плечо `off` — обратный ключ (контроль = out_rev19_mini строка
# в строку, p24_keyed.py); `thr` — поставочное умолчание (правило порога по рампе обоих спектров).
param([string[]]$Arms = @(), [string]$Suffix = 'mini')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp34'
$here = "$root\handover\p34-fit-floor\corpus"
New-Item -ItemType Directory -Force $here | Out-Null
$wd = "$wt\tools\CORPUS\scripts\wd_p34"
$log = "$here\arms.log"

$all = [ordered]@{
    off = @('--fit-floor=off')
    thr = @()
    adc = @('--fit-floor=adc')
    f20 = @('--fit-floor=20')
}
if ($Arms.Count -eq 0) { $Arms = @('off', 'thr') }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $out = "$root\tools\pie\out_p34_${Suffix}_$name"
    $curves = "$here\curves_${Suffix}_$name"
    New-Item -ItemType Directory -Force $curves | Out-Null
    $extra = @("--dump-curves=$curves", '--residuals=12') + $all[$name]
    Set-Location $wt
    $t0 = Get-Date
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd $wd `
        -Bin "$wt\BecquerelMonitor\bin\Release_p34" -ProbeBuild "$wt\tools\effmaker\probes\build_p34" `
        -Store "$wt\tools\CORPUS\corpus\geometries" -Extra $extra *> "$here\run_${Suffix}_$name.log"
    "$name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) extra=[$($all[$name] -join ' ')] out=$out" | Out-File -Append $log
    # score.py по малой базе зовёт сам run_mini.ps1 (с --only=mini.csv) — его строки «итого» лежат в run_*.log
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
