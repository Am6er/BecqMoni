# П99 18.09.2026 (B30, база rev29) — прогоны корпуса из worktree D:\BqMoni_Claude\p99\wt (HEAD 93dafe11 + правки полосы),
# сборка bin\Release_p99 / build_p99, склад — параметр -Store (живой склад главного дерева ПОСЛЕ переноса).
#   & D:\BqMoni_Claude\p99\run_arm.ps1 -Arm rev29                       # -> tools\pie\out_rev29_full / _mini (главное дерево)
# run_appwd.ps1 / run_mini.ps1 зовутся оператором `&` (T84/T91); клеймо .run.json пишут они сами (T249).
param([Parameter(Mandatory)][string]$Arm,
      [string]$Store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries',
      [string[]]$Extra = @(),
      [switch]$SkipWd)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'D:\BqMoni_Claude\p99\wt'
$art = 'D:\BqMoni_Claude\p99\art'
$log = "$art\arm_codes.txt"
$bin = "$wt\BecquerelMonitor\bin\Release_p99"
$pb = "$wt\tools\effmaker\probes\build_p99"
$score = "$wt\tools\pie\score.py"
$minicsv = "$wt\tools\CORPUS\corpus\mini.csv"
$wd = "$wt\tools\CORPUS\scripts\wd_p99"
$full = "$root\tools\pie\out_${Arm}_full"
$mini = "$root\tools\pie\out_${Arm}_mini"
$extra = @() + $Extra
Set-Location $wt
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') store=$Store extra=$($extra -join ' ')" | Out-File -Append $log
if (-not $SkipWd) {
    & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Arm.log"
    "arm $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$wt\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Arm.log"
    "arm $Arm check_appwd code=$LASTEXITCODE" | Out-File -Append $log
    "  response: rmx=" + (Get-ChildItem "$wd\config\device\response\*.rmx" -ErrorAction SilentlyContinue).Count | Out-File -Append $log
}
$t0 = Get-Date
if ($extra.Count -gt 0) {
    & "$wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $extra *> "$art\run_full_$Arm.log"
} else {
    & "$wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_full_$Arm.log"
}
"arm $Arm full code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
$t0 = Get-Date
if ($extra.Count -gt 0) {
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $extra *> "$art\run_mini_$Arm.log"
} else {
    & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_mini_$Arm.log"
}
"arm $Arm mini code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    & python $score --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_${Arm}_$part.txt"
    "arm $Arm score full $part code=$LASTEXITCODE" | Out-File -Append $log
    & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_${Arm}_$part.txt"
    "arm $Arm score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
Copy-Item "$mini\.run.json" "$art\run_out_${Arm}_mini.json" -ErrorAction SilentlyContinue
Copy-Item "$full\.run.json" "$art\run_out_${Arm}_full.json" -ErrorAction SilentlyContinue
"arm $Arm end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Get-Content $log | Select-String "arm $Arm " | ForEach-Object { $_.Line }
Get-Content "$art\run_full_$Arm.log" | Select-String 'КЛЮЧИ|KEYS|SETUP|УГЛОВЫЕ|матриц|БЕЗ МАТРИЦЫ|найден|ошиб|⛔' | Select-Object -Last 8 | ForEach-Object { $_.Line }
