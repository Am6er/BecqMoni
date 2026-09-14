# П34 12.09.2026, `A309` — ПОЛНЫЙ КОРПУС (назван Amber решением A309): плечи `off` (обратный ключ; контроль =
# out_rev19_full строка в строку) и `thr` (поставочное умолчание — правило порога по рампе обоих спектров).
# Стенд: worktree C:\Users\moroz\bqp34 (HEAD + правило A309), Release_p34 / build_p34 / wd_p34, склад — копия.
# Запуск: pwsh -File <этот файл> [off,thr]; выход — tools\pie\out_p34_full_<плечо> + score в corpus\.
param([string[]]$Arms = @())
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
$all = [ordered]@{ off = @('--fit-floor=off'); thr = @() }
if ($Arms.Count -eq 0) { $Arms = @('off', 'thr') }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $out = "$root\tools\pie\out_p34_full_$name"
    $curves = "$here\curves_full_$name"
    New-Item -ItemType Directory -Force $curves | Out-Null
    $extra = @("--dump-curves=$curves", '--residuals=12') + $all[$name]
    Set-Location $wt
    $t0 = Get-Date
    & "$wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $out -Wd $wd `
        -Bin "$wt\BecquerelMonitor\bin\Release_p34" -ProbeBuild "$wt\tools\effmaker\probes\build_p34" `
        -Store "$wt\tools\CORPUS\corpus\geometries" -Extra $extra *> "$here\run_full_$name.log"
    "full $name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) extra=[$($all[$name] -join ' ')] out=$out" | Out-File -Append $log
    foreach ($part in @('known', 'unknown')) {
        & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$out" "--part=$part" --members *> "$here\score_full_${name}_$part.txt"
    }
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
