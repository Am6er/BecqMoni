# П71 (S170): оснастка wd_p71<arm> из сборки плеча, склад матриц — живой склад основного дерева (только чтение).
#   pwsh -File D:\BqMoni_Claude\p71\mk_wd.ps1 -Arm a|b
param([Parameter(Mandatory)][ValidateSet('a','b')][string]$Arm)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$wt = if ($Arm -eq 'a') { 'D:\BqMoni_Claude\p71\wt' } else { 'D:\BqMoni_Claude\p71\wt_b' }
$store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
Set-Location $wt
& "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p71$Arm" -Wd "$wt\tools\CORPUS\scripts\wd_p71$Arm" -ProbeBuild "$wt\tools\effmaker\probes\build_p71$Arm" -Store $store
"mk_appwd code $LASTEXITCODE"
exit $LASTEXITCODE
