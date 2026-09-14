# П69 (S174): малая база корпуса, плечи А и Б — ОБА из чистых worktree (корпус HEAD 6c2833cf), матрицы —
# из живого склада корпуса основного дерева (`.rmx` вне git, worktree их не несёт; только чтение).
#
#   pwsh -File D:\BqMoni_Claude\p69\mini.ps1 -Arm a    # worktree wt   (HEAD),               build_p69a -> out_mini_p69a
#   pwsh -File D:\BqMoni_Claude\p69\mini.ps1 -Arm b    # worktree wt_b (HEAD + правка S174), build_p69b -> out_mini_p69b
#
# Оснастка корпуса и малая база — оператором вызова & из этой сессии (T84). ⚠ ОДИН запуск на плечо
# (грабля П68: два ждуна стартовали одно и то же в один wd).
param([Parameter(Mandatory)][ValidateSet('a','b')][string]$Arm)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:OS = 'Windows_NT'
$d = 'D:\BqMoni_Claude\p69'
$wt = if ($Arm -eq 'a') { "$d\wt" } else { "$d\wt_b" }
$rel = if ($Arm -eq 'a') { 'Release_p69a' } else { 'Release_p69b' }
$pb = if ($Arm -eq 'a') { 'build_p69a' } else { 'build_p69b' }
$store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$codes = @()
Set-Location $wt
& "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\$rel" -Wd "$wt\tools\CORPUS\scripts\wd_p69$Arm" -ProbeBuild "$wt\tools\effmaker\probes\$pb" -Store $store *> "$d\logs\mk_appwd_$Arm.log"; $codes += "mk_appwd_$Arm=$LASTEXITCODE"
Get-Content "$d\logs\mk_appwd_$Arm.log" -Encoding UTF8 | Select-String 'ОСНАСТКА|⛔|ПРОТУХ' | Select-Object -First 3
& "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out "$d\out_mini_p69$Arm" -Wd "$wt\tools\CORPUS\scripts\wd_p69$Arm" -Bin "$wt\BecquerelMonitor\bin\$rel" -ProbeBuild "$wt\tools\effmaker\probes\$pb" -Store $store *> "$d\logs\run_mini_$Arm.log"; $codes += "run_mini_$Arm=$LASTEXITCODE"
Get-Content "$d\logs\run_mini_$Arm.log" -Encoding UTF8 | Select-String 'ОСНАСТКА СВЕЖАЯ|ПРОГОН|итого|sum chi2|⛔|part=' | Select-Object -First 12
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⚠ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
