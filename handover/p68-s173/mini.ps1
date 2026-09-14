# П68 (S173): малая база корпуса, плечи А и Б — ОБА из чистых worktree (корпус HEAD 198500a7), матрицы —
# из живого склада корпуса основного дерева (`.rmx` вне git, worktree их не несёт; П66 склад матриц малой
# базы не трогает — её сцены G1S_mar1l_* в малую базу не входят).
#
#   pwsh -File D:\BqMoni_Claude\p68\mini.ps1 -Arm a    # worktree wt  (HEAD),  build_p68a -> out_mini_p68a
#   pwsh -File D:\BqMoni_Claude\p68\mini.ps1 -Arm b    # worktree wt_b (HEAD + правка S173), build_p68b -> out_mini_p68b
#
# Оснастка корпуса и малая база — оператором вызова & из этой сессии (T84).
param([Parameter(Mandatory)][ValidateSet('a','b')][string]$Arm)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:OS = 'Windows_NT'
$d = 'D:\BqMoni_Claude\p68'
$wt = if ($Arm -eq 'a') { "$d\wt" } else { "$d\wt_b" }
$rel = if ($Arm -eq 'a') { 'Release_p68a' } else { 'Release_p68b' }
$pb = if ($Arm -eq 'a') { 'build_p68a' } else { 'build_p68b' }
$store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$codes = @()
Set-Location $wt
& "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\$rel" -Wd "$wt\tools\CORPUS\scripts\wd_p68$Arm" -ProbeBuild "$wt\tools\effmaker\probes\$pb" -Store $store *> "$d\logs\mk_appwd_$Arm.log"; $codes += "mk_appwd_$Arm=$LASTEXITCODE"
Get-Content "$d\logs\mk_appwd_$Arm.log" -Encoding UTF8 | Select-String 'ОСНАСТКА|⛔|ПРОТУХ' | Select-Object -First 3
& "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out "$d\out_mini_p68$Arm" -Wd "$wt\tools\CORPUS\scripts\wd_p68$Arm" -Bin "$wt\BecquerelMonitor\bin\$rel" -ProbeBuild "$wt\tools\effmaker\probes\$pb" -Store $store *> "$d\logs\run_mini_$Arm.log"; $codes += "run_mini_$Arm=$LASTEXITCODE"
Get-Content "$d\logs\run_mini_$Arm.log" -Encoding UTF8 | Select-String 'ОСНАСТКА СВЕЖАЯ|ПРОГОН|итого|sum chi2|⛔|part=' | Select-Object -First 12
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⚠ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
