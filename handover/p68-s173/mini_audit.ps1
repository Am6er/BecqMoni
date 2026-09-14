# П68 (S173): малая база с --audit (кросс-сверка линий FsaLineAudit, S60) — А/Б; оснастка wd_p68a/wd_p68b уже собрана mini.ps1.
param([Parameter(Mandatory)][ValidateSet('a','b')][string]$Arm)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:OS = 'Windows_NT'
$d = 'D:\BqMoni_Claude\p68'
$wt = if ($Arm -eq 'a') { "$d\wt" } else { "$d\wt_b" }
$rel = if ($Arm -eq 'a') { 'Release_p68a' } else { 'Release_p68b' }
$pb = if ($Arm -eq 'a') { 'build_p68a' } else { 'build_p68b' }
$store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
Set-Location $wt
& "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out "$d\out_mini_p68${Arm}_audit" -Wd "$wt\tools\CORPUS\scripts\wd_p68$Arm" -Bin "$wt\BecquerelMonitor\bin\$rel" -ProbeBuild "$wt\tools\effmaker\probes\$pb" -Store $store -Extra '--audit' -SkipScore *> "$d\logs\run_mini_${Arm}_audit.log"
"run_mini_${Arm}_audit=$LASTEXITCODE"
exit $LASTEXITCODE
