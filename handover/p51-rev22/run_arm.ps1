# П51 14.09.2026 — плечи полного корпуса и малой базы на складе физики 18 (лестница против out_rev21_*).
#   pwsh -File handover\p51-rev22\run_arm.ps1 -Arm a      # плечо A: новый склад + --weights=data (чистая физика 18
#                                                          #          на линейке rev21) -> out_p51_full_a / out_p51_mini_a
#   pwsh -File handover\p51-rev22\run_arm.ps1 -Arm rev22  # плечо B: новый склад + умолчания (физика 18 + веса по модели,
#                                                          #          A310) -> out_rev22_full / out_rev22_mini — новая база
# Оснастка wd_p51 (mk_appwd.ps1 из Release_p51 / build_p51, склад — живой tools\CORPUS\corpus\geometries).
# run_appwd.ps1 / run_mini.ps1 зовутся оператором `&` (T84/T91); клеймо .run.json пишут они сами (T249).
# Ключ --angcorr не трогается (умолчание ВЫКЛ, решение Amber 13.09.2026 «ВЫКЛ до разбора S170»).
# Образец — П38 run_arm.ps1.
param([Parameter(Mandatory)][string]$Arm)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p51-rev22"
$wd = "$root\tools\CORPUS\scripts\wd_p51"
$bin = "$root\BecquerelMonitor\bin\Release_p51"
$pb = "$root\tools\effmaker\probes\build_p51"
$log = "$art\codes.txt"
if ($Arm -eq 'rev22') { $full = "$root\tools\pie\out_rev22_full"; $mini = "$root\tools\pie\out_rev22_mini"; $extra = @() }
elseif ($Arm -eq 'a') { $full = "$root\tools\pie\out_p51_full_a"; $mini = "$root\tools\pie\out_p51_mini_a"; $extra = @('--weights=data') }
else { "неизвестное плечо: $Arm" | Out-File -Append $log; exit 2 }
Set-Location $root
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') extra=[$($extra -join ' ')]" | Out-File -Append $log
$t0 = Get-Date
if ($extra.Count -gt 0) {
    & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Extra $extra *> "$art\run_full_$Arm.log"
} else {
    & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\run_full_$Arm.log"
}
"arm $Arm full code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
$t0 = Get-Date
if ($extra.Count -gt 0) {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Extra $extra *> "$art\run_mini_$Arm.log"
} else {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\run_mini_$Arm.log"
}
"arm $Arm mini code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_${Arm}_$part.txt"
    "arm $Arm score full $part code=$LASTEXITCODE" | Out-File -Append $log
    & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$root\tools\CORPUS\corpus\mini.csv" *> "$art\score_mini_${Arm}_$part.txt"
    "arm $Arm score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
"arm $Arm end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
