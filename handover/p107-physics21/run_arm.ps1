# П107 19.09.2026 — прогоны корпуса (образец — П103 run_arm.ps1):
#   -Arm rev31     : worktree wt (физика 21), склад — параметр -Store (умолчание — склад полосы D:\BqMoni_Claude\p107\store
#                    после кривых) -> tools\pie\out_p107_wt_{full,mini} (репетиция; не объявляемая база).
#   -Arm rev31main : ГЛАВНОЕ дерево после переноса (Release_p107/build_p107 там), живой склад -> out_rev31_{full,mini}
#                    (объявляемая база).
# run_appwd.ps1 / run_mini.ps1 зовутся оператором `&` (T84/T91); клеймо .run.json пишут они сами (T249).
#   pwsh -File D:\BqMoni_Claude\p107\scripts\run_arm.ps1 -Arm rev31main
param([Parameter(Mandatory)][string]$Arm,
      [string]$Store = '',
      [string]$Tag = '',
      [string[]]$Extra = @(),
      [switch]$SkipWd)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = 'D:\BqMoni_Claude\p107\art'
$log = "$art\arm_codes.txt"
switch ($Arm) {
    'rev31' { $wt = 'D:\BqMoni_Claude\p107\wt'; $bin = "$wt\BecquerelMonitor\bin\Release_p107"; $pb = "$wt\tools\effmaker\probes\build_p107"
              $wd = "$wt\tools\CORPUS\scripts\wd_p107"; $full = "$root\tools\pie\out_p107_wt_full$Tag"; $mini = "$root\tools\pie\out_p107_wt_mini$Tag"
              if (-not $Store) { $Store = 'D:\BqMoni_Claude\p107\store' } }
    'rev31main' { $wt = $root; $bin = "$wt\BecquerelMonitor\bin\Release_p107"; $pb = "$wt\tools\effmaker\probes\build_p107"
              $wd = "$wt\tools\CORPUS\scripts\wd_p107"; $full = "$root\tools\pie\out_rev31_full$Tag"; $mini = "$root\tools\pie\out_rev31_mini$Tag"
              if (-not $Store) { $Store = "$root\tools\CORPUS\corpus\geometries" } }
    default { "неизвестное плечо: $Arm" | Out-File -Append $log; exit 2 }
}
$score = "$wt\tools\pie\score.py"
$minicsv = "$wt\tools\CORPUS\corpus\mini.csv"
$extra = @() + $Extra
Set-Location $wt
"arm $Arm$Tag start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') store=$Store extra=$($extra -join ' ')" | Out-File -Append $log
if (-not $SkipWd) {
    & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Arm$Tag.log"
    "arm $Arm$Tag mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$wt\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Arm$Tag.log"
    "arm $Arm$Tag check_appwd code=$LASTEXITCODE" | Out-File -Append $log
    "  response: rmx=" + (Get-ChildItem "$wd\config\device\response\*.rmx" -ErrorAction SilentlyContinue).Count | Out-File -Append $log
}
$t0 = Get-Date
if ($extra.Count -gt 0) {
    & "$wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $extra *> "$art\run_full_$Arm$Tag.log"
} else {
    & "$wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_full_$Arm$Tag.log"
}
"arm $Arm$Tag full code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
$t0 = Get-Date
if ($extra.Count -gt 0) {
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $extra *> "$art\run_mini_$Arm$Tag.log"
} else {
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_mini_$Arm$Tag.log"
}
"arm $Arm$Tag mini code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    & python $score --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_$Arm${Tag}_$part.txt"
    "arm $Arm$Tag score full $part code=$LASTEXITCODE" | Out-File -Append $log
    & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_$Arm${Tag}_$part.txt"
    "arm $Arm$Tag score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
Copy-Item "$mini\.run.json" "$art\run_out_$Arm${Tag}_mini.json" -ErrorAction SilentlyContinue
Copy-Item "$full\.run.json" "$art\run_out_$Arm${Tag}_full.json" -ErrorAction SilentlyContinue
"arm $Arm$Tag end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Get-Content $log | Select-String "arm $Arm$Tag " | ForEach-Object { $_.Line }
Get-Content "$art\run_full_$Arm$Tag.log" | Select-String 'КЛЮЧИ|KEYS|SETUP|УГЛОВЫЕ|матриц|БЕЗ МАТРИЦЫ|найден|ошиб|⛔' | Select-Object -Last 8 | ForEach-Object { $_.Line }
