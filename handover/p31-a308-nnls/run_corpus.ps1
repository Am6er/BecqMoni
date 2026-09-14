# П31 12.09.2026, `A308` — плечи пола полосы ФИТА (`A302`, ключ `--fit-floor=`) на МАЛОЙ БАЗЕ
# (43 понятных + 16 непонятных, диск AS80_Th232Medal внутри). Стенд: worktree C:\Users\moroz\bqp31
# (HEAD 3dd0da47 + прибор NnlsTraceSink, Release_P31, build_p31, склад .rmx — копия корпусного),
# оснастка wd_p31 там же (mk_appwd.ps1 без -Force). Запуск: pwsh -File <этот файл> [имена плеч]
# ⛔ run_mini.ps1 зовётся ОПЕРАТОРОМ `&` (T84). Плечо `ctl` — умолчание (контроль = out_rev19_mini
# строка в строку, p24_keyed.py); остальные — ключи --fit-floor= / --huber=.
param([string[]]$Arms = @())
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp31'
$here = "$root\handover\p31-a308-nnls\corpus"
New-Item -ItemType Directory -Force $here | Out-Null
$wd = "$wt\tools\CORPUS\scripts\wd_p31"
$log = "$here\arms.log"

if (-not (Test-Path "$wd\CorpusFsaProbe.exe")) {
    & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_P31" -Wd $wd `
        -ProbeBuild "$wt\tools\effmaker\probes\build_p31" *> "$here\mk_appwd.log"
    "mk_appwd code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
    if ($LASTEXITCODE -ne 0) { exit 3 }
}

$all = [ordered]@{
    ctl    = @()
    f20    = @('--fit-floor=20')
    adc    = @('--fit-floor=adc')
    h0     = @('--huber=0')
    f20_h0 = @('--fit-floor=20', '--huber=0')
    f35    = @('--fit-floor=35')
    f35_h0 = @('--fit-floor=35', '--huber=0')
}
if ($Arms.Count -eq 0) { $Arms = @($all.Keys) }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $out = "$root\tools\pie\out_p31_$name"
    $curves = "$here\curves_$name"
    New-Item -ItemType Directory -Force $curves | Out-Null
    $extra = @("--dump-curves=$curves", '--residuals=12') + $all[$name]
    Set-Location $wt
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $out -Wd $wd `
        -Bin "$wt\BecquerelMonitor\bin\Release_P31" -ProbeBuild "$wt\tools\effmaker\probes\build_p31" `
        -Extra $extra *> "$here\run_$name.log"
    "$name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') extra=[$($all[$name] -join ' ')]" | Out-File -Append $log
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
