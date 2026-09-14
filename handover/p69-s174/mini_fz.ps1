# П69 (S174): малая база, плечо Б на ФИНАЛЬНОЙ сборке wt_b, склад — ЗАМОРОЖЕННЫЙ снимок D:\BqMoni_Claude\p69\store_mini
# (сцены и index.csv живого склада + 45 матриц из оснастки плеча А wd_p69a, снятой 19:51). Зачем: живой склад
# основного дерева движется под П66 (новая матрица каждые ~10 мин, 20:16 — coal_046 под новым guid), и сторож
# оснастки (T63/A79) честно отказал перекладке; снимок — то же состояние склада, что у плеча А и первого прогона Б.
#   pwsh -File D:\BqMoni_Claude\p69\mini_fz.ps1 -Arm b   -> out_mini_p69b_fz
param([Parameter(Mandatory)][ValidateSet('a','b')][string]$Arm)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:OS = 'Windows_NT'
$d = 'D:\BqMoni_Claude\p69'
$wt = if ($Arm -eq 'a') { "$d\wt" } else { "$d\wt_b" }
$rel = if ($Arm -eq 'a') { 'Release_p69a' } else { 'Release_p69b' }
$pb = if ($Arm -eq 'a') { 'build_p69a' } else { 'build_p69b' }
$store = "$d\store_mini"
$codes = @()
Set-Location $wt
& "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\$rel" -Wd "$wt\tools\CORPUS\scripts\wd_p69${Arm}fz" -ProbeBuild "$wt\tools\effmaker\probes\$pb" -Store $store *> "$d\logs\mk_appwd_${Arm}_fz.log"; $codes += "mk_appwd_$Arm=$LASTEXITCODE"
Get-Content "$d\logs\mk_appwd_${Arm}_fz.log" -Encoding UTF8 | Select-String 'ОСНАСТКА|⛔|ПРОТУХ|матриц' | Select-Object -First 4
& "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out "$d\out_mini_p69${Arm}_fz" -Wd "$wt\tools\CORPUS\scripts\wd_p69${Arm}fz" -Bin "$wt\BecquerelMonitor\bin\$rel" -ProbeBuild "$wt\tools\effmaker\probes\$pb" -Store $store *> "$d\logs\run_mini_${Arm}_fz.log"; $codes += "run_mini_$Arm=$LASTEXITCODE"
Get-Content "$d\logs\run_mini_${Arm}_fz.log" -Encoding UTF8 | Select-String 'ОСНАСТКА СВЕЖАЯ|ПРОГОН|итого|sum chi2|⛔|part=' | Select-Object -First 12
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⚠ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
