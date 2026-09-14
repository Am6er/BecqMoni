# П66 14.09.2026 — лестница 0 -> A -> B -> C на полном корпусе и малой базе (изменение задания
# распорядителем 14.09.2026: объявляется плечо C = сосуды + уголь + S172).
#   pwsh -File handover\p66-rev23\run_arm.ps1 -Arm a   # плечо A: сборка 16de7feb (worktree wt), корпус rev22 (131) +
#                                                       #          17 сцен маринелли ОМАСН (матрицы + узлы) -> out_p66_full_a / out_p66_mini_a;
#                                                       #          скрипты и score.py — worktree wt (манифест 131)
#   pwsh -File handover\p66-rev23\run_arm.ps1 -Arm b   # плечо B: та же сборка 16de7feb, корпус 135 (уголь) — копия основного дерева
#                                                       #          в wt -> out_p66_full_b / out_p66_mini_b; скрипты wt, score.py основного (манифест 135)
#   pwsh -File handover\p66-rev23\run_arm.ps1 -Arm c   # плечо C: сборка a6f2b227 (worktree wt2: S172), корпус 135 — копия основного дерева
#                                                       #          в wt2 (robocopy /MIR), скрипты wt2 -> out_rev23_full / out_rev23_mini — новая база.
#                                                       #          ⚠ Из ОСНОВНОГО дерева оснастку с этой сборкой собрать нельзя: сторож оснастки (T226)
#                                                       #          сверяет исходники побайтно, а 107 файлов приложения лежат в основном дереве с LF при
#                                                       #          CRLF в worktree — «изменено 113» (mk_appwd_c_maintree.log), содержимое то же
#                                                       #          (check_declared_base сверяет с CRLF→LF).
# run_appwd.ps1 / run_mini.ps1 зовутся оператором `&` (T84/T91); клеймо .run.json пишут они сами (T249).
# Ключей пробы нет ни у одного плеча (умолчания приложения). Образец — П51 run_arm.ps1.
param([Parameter(Mandatory)][string]$Arm)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'D:\BqMoni_Claude\p66\wt'
$wt2 = 'D:\BqMoni_Claude\p66\wt2'
$art = "$root\handover\p66-rev23"
$log = "$art\codes.txt"
switch ($Arm) {
    'a' { $tree = $wt;   $bin = "$wt\BecquerelMonitor\bin\Release_p66";   $pb = "$wt\tools\effmaker\probes\build_p66";   $wd = "$wt\tools\CORPUS\scripts\wd_p66a";  $full = "$root\tools\pie\out_p66_full_a"; $mini = "$root\tools\pie\out_p66_mini_a"; $score = "$wt\tools\pie\score.py";   $minicsv = "$wt\tools\CORPUS\corpus\mini.csv" }
    'b' { $tree = $wt;   $bin = "$wt\BecquerelMonitor\bin\Release_p66";   $pb = "$wt\tools\effmaker\probes\build_p66";   $wd = "$wt\tools\CORPUS\scripts\wd_p66b";  $full = "$root\tools\pie\out_p66_full_b"; $mini = "$root\tools\pie\out_p66_mini_b"; $score = "$root\tools\pie\score.py"; $minicsv = "$root\tools\CORPUS\corpus\mini.csv" }
    'c' { $tree = $wt2;  $bin = "$wt2\BecquerelMonitor\bin\Release_p66c"; $pb = "$wt2\tools\effmaker\probes\build_p66c"; $wd = "$wt2\tools\CORPUS\scripts\wd_p66c"; $full = "$root\tools\pie\out_rev23_full";  $mini = "$root\tools\pie\out_rev23_mini";  $score = "$root\tools\pie\score.py"; $minicsv = "$root\tools\CORPUS\corpus\mini.csv" }
    default { "неизвестное плечо: $Arm" | Out-File -Append $log; exit 2 }
}
Set-Location $tree
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') tree=$tree bin=$bin" | Out-File -Append $log
$t0 = Get-Date
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
