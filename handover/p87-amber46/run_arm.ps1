# П87 16.09.2026 (AMBER46) — прогоны корпуса из worktree D:\BqMoni_Claude\p87\wt (HEAD 8b164a98 + правки полосы),
# сборка bin\Release_p87 / build_p87, склад — параметр -Store (после переноса: живой склад основного дерева,
# 46 .rmx формата 9, ни одного .qk).
#   & D:\BqMoni_Claude\p87\run_arm.ps1 -Arm rev26            # умолчания (ключ ВКЛ) -> tools\pie\out_rev26_full / _mini
#   & D:\BqMoni_Claude\p87\run_arm.ps1 -Arm off              # --angcorr=0 -> out_p87_off_full / _mini (= rev24 побитово?)
#   & D:\BqMoni_Claude\p87\run_arm.ps1 -Arm first2 -Extra ...# первые два спектра с парами (контроль настройки)
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
$wt = 'D:\BqMoni_Claude\p87\wt'
$art = 'D:\BqMoni_Claude\p87\art'
$log = "$art\arm_codes.txt"
$bin = "$wt\BecquerelMonitor\bin\Release_p87"
$pb = "$wt\tools\effmaker\probes\build_p87"
$wd = "$wt\tools\CORPUS\scripts\wd_p87"
$score = "$wt\tools\pie\score.py"
$minicsv = "$wt\tools\CORPUS\corpus\mini.csv"
switch ($Arm) {
    'rev26'  { $full = "$root\tools\pie\out_rev26_full"; $mini = "$root\tools\pie\out_rev26_mini"; $extra = @() }
    'off'    { $full = "$root\tools\pie\out_p87_off_full"; $mini = "$root\tools\pie\out_p87_off_mini"; $extra = @('--angcorr=0') }
    'first2' { $full = "$root\tools\pie\out_p87_first2"; $mini = ''; $extra = @('--only=G1S16_Co60_P5,G1S16_Eu152_P5') + $Extra }
    default  { "неизвестное плечо: $Arm" | Out-File -Append $log; exit 2 }
}
Set-Location $wt
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') store=$Store extra=$($extra -join ' ')" | Out-File -Append $log
if (-not $SkipWd) {
    & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Arm.log"
    "arm $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$wt\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Arm.log"
    "arm $Arm check_appwd code=$LASTEXITCODE" | Out-File -Append $log
    "  response: rmx=" + (Get-ChildItem "$wd\config\device\response\*.rmx" -ErrorAction SilentlyContinue).Count + " qk=" + (Get-ChildItem "$wd\config\device\response\*.qk" -ErrorAction SilentlyContinue).Count | Out-File -Append $log
}
$t0 = Get-Date
if ($extra.Count -gt 0) {
    & "$wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $extra *> "$art\run_full_$Arm.log"
} else {
    & "$wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_full_$Arm.log"
}
"arm $Arm full code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
if ($mini) {
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
}
Copy-Item "$full\.run.json" "$art\run_out_${Arm}_full.json" -ErrorAction SilentlyContinue
"arm $Arm end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Get-Content $log | Select-String "arm $Arm " | ForEach-Object { $_.Line }
Get-Content "$art\run_full_$Arm.log" | Select-String 'КЛЮЧИ|KEYS|SETUP|УГЛОВЫЕ|матриц|БЕЗ МАТРИЦЫ|найден|ошиб|⛔' | Select-Object -Last 8 | ForEach-Object { $_.Line }
