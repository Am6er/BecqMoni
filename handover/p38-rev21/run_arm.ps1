# П38 13.09.2026 — плечи полного корпуса и малой базы на складе физики 17.
#   pwsh -File handover\p38-rev21\run_arm.ps1 -Arm a      # плечо A: новый склад, СТАРЫЙ корпус -> out_p38_full_a / out_p38_mini_a
#   pwsh -File handover\p38-rev21\run_arm.ps1 -Arm rev21  # плечо B: новый склад, НОВЫЙ корпус  -> out_rev21_full / out_rev21_mini
# Оснастка wd_p38 (mk_appwd.ps1 из Release_p38 / build_p38, склад — живой tools\CORPUS\corpus\geometries).
# run_appwd.ps1 / run_mini.ps1 зовутся оператором `&` (T84/T91); клеймо .run.json пишут они сами (T249).
param([Parameter(Mandatory)][string]$Arm)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p38-rev21"
$wd = "$root\tools\CORPUS\scripts\wd_p38"
$bin = "$root\BecquerelMonitor\bin\Release_p38"
$pb = "$root\tools\effmaker\probes\build_p38"
$log = "$art\codes.txt"
if ($Arm -eq 'rev21') { $full = "$root\tools\pie\out_rev21_full"; $mini = "$root\tools\pie\out_rev21_mini" }
else { $full = "$root\tools\pie\out_p38_full_$Arm"; $mini = "$root\tools\pie\out_p38_mini_$Arm" }
Set-Location $root
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $log
$t0 = Get-Date
& "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\run_full_$Arm.log"
"arm $Arm full code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
$t0 = Get-Date
& "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\run_mini_$Arm.log"
"arm $Arm mini code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_${Arm}_$part.txt"
    "arm $Arm score full $part code=$LASTEXITCODE" | Out-File -Append $log
    & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$root\tools\CORPUS\corpus\mini.csv" *> "$art\score_mini_${Arm}_$part.txt"
    "arm $Arm score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
"arm $Arm end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
