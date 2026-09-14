# П42 13.09.2026, `AMBER22` п. 4 — корпусный диск AS80_Th232Medal на rev21 (склад физики 17) с дампом кривых
# и абляциями (повтор П26 fsa/run_arms.ps1 с именами П42). Стенд: worktree C:\Users\moroz\bqp42 (HEAD 60a2a5d5
# + FsaAnalyzer.cs с хуком/маской П42, умолчания побитово прежние), оснастка wd_p42 там же (mk_appwd.ps1).
# ⛔ run_mini.ps1 зовётся ОПЕРАТОРОМ `&` (T84). Плечо `disk` = out_rev21_full строка диска (контроль).
param([string[]]$Arms = @())
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp42'
$here = "$root\handover\p42-amber22\fsa"
$wd = "$wt\tools\CORPUS\scripts\wd_p42"
$log = "$here\arms.log"
if (-not (Test-Path "$wd\CorpusFsaProbe.exe")) {
    & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p42" -Wd $wd `
        -ProbeBuild "$wt\tools\effmaker\probes\build_p42" -Store "$wt\tools\CORPUS\corpus\geometries" *> "$here\mk_appwd.log"
    "mk_appwd code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
    if ($LASTEXITCODE -ne 0) { exit 3 }
}
$all = [ordered]@{
    disk      = @()
    kfwhm2    = @('--knot-fwhm=2')
    huber0    = @('--huber=0')
    nomatrix  = @('--no-matrix')
    noeq      = @('--no-equilibrium')
    nocascade = @('--no-cascade')
    noanchor  = @('--no-anchor')
    nobg      = @('--no-background')
}
if ($Arms.Count -eq 0) { $Arms = @($all.Keys) }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $out = "$root\tools\pie\out_p42_$name"
    $curves = "$here\curves_$name"
    New-Item -ItemType Directory -Force $curves | Out-Null
    $extra = @("--dump-curves=$curves", '--residuals=12') + $all[$name]
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -List "$here\list_disk.csv" -Out $out -Wd $wd `
        -Bin "$wt\BecquerelMonitor\bin\Release_p42" -ProbeBuild "$wt\tools\effmaker\probes\build_p42" `
        -Extra $extra -SkipScore *> "$here\run_$name.log"
    "$name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') extra=[$($all[$name] -join ' ')]" | Out-File -Append $log
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
