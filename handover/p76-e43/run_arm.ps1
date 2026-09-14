# П76 14.09.2026 (E43) — прогон объявляемой базы rev24 на полном корпусе и малой базе.
#   pwsh -File handover\p76-e43\run_arm.ps1 -Arm rev24   # сборка 97ccef9f (worktree D:\BqMoni_Claude\p76\wt, чистый
#                                                         #   коммит: правки полосы — пресет/генератор — в набор разбора
#                                                         #   прогона не входят и на числа FSA не влияют), корпус 135 —
#                                                         #   копия основного дерева в wt (robocopy /MIR) с двумя сценами
#                                                         #   RC103 зазора 3.5 мм, их матрицами и узлами;
#                                                         #   -> tools\pie\out_rev24_full / out_rev24_mini — новая база.
# Плечо 0 — объявленная rev23 (`tools\pie\out_rev23_*`, П66) — не перезаписывается.
# ⚠ Из ОСНОВНОГО дерева оснастку этой сборкой собрать нельзя: сторож оснастки (T226) сверяет исходники побайтно, а
#   основное дерево несёт чужие незакоммиченные правки разбора (П71/П74/П75) и LF у части файлов — прогон из worktree.
# run_appwd.ps1 / run_mini.ps1 зовутся оператором `&` (T84/T91); клеймо .run.json пишут они сами (T249).
# Ключей пробы нет (умолчания приложения). Образец — П66 run_arm.ps1.
param([Parameter(Mandatory)][string]$Arm)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'D:\BqMoni_Claude\p76\wt'
$art = "$root\handover\p76-e43"
$log = "$art\codes.txt"
switch ($Arm) {
    # плечо A — та же сборка 97ccef9f, корпус rev23 (git 97ccef9f: `git checkout -- tools/CORPUS/corpus` в wt) + склад
    #   из снимка D:\BqMoni_Claude\p76\store_backup (46 + 46 = живой склад rev23): отделяет правки разбора a6f2b227 → 97ccef9f
    #   (П67–П72) от зазора; -> out_p76_full_a / out_p76_mini_a. Ждём: runs побитово = rev23, share_pct — сдвиг кода.
    'a'     { $tree = $wt; $bin = "$wt\BecquerelMonitor\bin\Release_p76"; $pb = "$wt\tools\effmaker\probes\build_p76"; $wd = "$wt\tools\CORPUS\scripts\wd_p76a"; $full = "$root\tools\pie\out_p76_full_a"; $mini = "$root\tools\pie\out_p76_mini_a"; $score = "$wt\tools\pie\score.py"; $minicsv = "$wt\tools\CORPUS\corpus\mini.csv" }
    'rev24' { $tree = $wt; $bin = "$wt\BecquerelMonitor\bin\Release_p76"; $pb = "$wt\tools\effmaker\probes\build_p76"; $wd = "$wt\tools\CORPUS\scripts\wd_p76"; $full = "$root\tools\pie\out_rev24_full"; $mini = "$root\tools\pie\out_rev24_mini"; $score = "$wt\tools\pie\score.py"; $minicsv = "$wt\tools\CORPUS\corpus\mini.csv" }
    default { "неизвестное плечо: $Arm" | Out-File -Append $log; exit 2 }
}
Set-Location $tree
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') tree=$tree bin=$bin" | Out-File -Append $log
& "$tree\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd *> "$art\mk_appwd_$Arm.log"
"arm $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
& "$tree\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd *> "$art\check_appwd_$Arm.log"
"arm $Arm check_appwd code=$LASTEXITCODE" | Out-File -Append $log
$t0 = Get-Date
& "$tree\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\run_full_$Arm.log"
"arm $Arm full code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
$t0 = Get-Date
& "$tree\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\run_mini_$Arm.log"
"arm $Arm mini code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    & python $score --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_${Arm}_$part.txt"
    "arm $Arm score full $part code=$LASTEXITCODE" | Out-File -Append $log
    & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_${Arm}_$part.txt"
    "arm $Arm score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
Copy-Item "$full\.run.json" "$art\run_out_${Arm}_full.json" -ErrorAction SilentlyContinue
Copy-Item "$mini\.run.json" "$art\run_out_${Arm}_mini.json" -ErrorAction SilentlyContinue
"arm $Arm end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
