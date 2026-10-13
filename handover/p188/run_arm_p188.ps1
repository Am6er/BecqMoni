# П188 29.09.2026 — прогоны корпуса ИЗ WORKTREE p180 (ветка p180-physics25, физика 26), копия П176 run_arm_p176.ps1.
#   -Arm rev37 : объявляемая база, склад worktree (store_night, физика 26) -> <wt>\tools\pie\out_rev37_{full,mini}
#   -Arm kb    : малая база с --dump-curves (окно Kβ) -> D:\BqMoni_Claude\p188\out\mini_<Tag>kb, кривые -> p188\curves\mini_<Tag>
#   (ступень «физика 25 без П182» снята 29–30.09 этим же скриптом, выходы переименованы в *_p25)
# run_appwd.ps1 / run_mini.ps1 зовутся оператором `&` (T84/T91); клеймо .run.json пишут они сами (T249).
#   & D:\BqMoni_Claude\p188\run_arm_p188.ps1 -Arm rev37
param([Parameter(Mandatory)][string]$Arm, [string]$Tag = 'p26',
      [switch]$SkipWd,
      [switch]$MiniOnly)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
$env:TEMP = 'D:\BqMoni_Claude\p188\tmp'; $env:TMP = $env:TEMP
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'D:\BqMoni_Claude\p183\wt'
$art  = 'D:\BqMoni_Claude\p188\art'
$log  = "$art\arm_codes.txt"
$bin  = "$root\BecquerelMonitor\bin\Release_p188_rel"
$pb   = 'D:\BqMoni_Claude\p188\build_p188_rel'
$wd   = "$root\tools\CORPUS\scripts\wd_p188"
$Store = "$root\tools\CORPUS\corpus\geometries"
$extra = @()
switch ($Arm) {
    'rev37' { $full = "$root\tools\pie\out_rev37_full"; $mini = "$root\tools\pie\out_rev37_mini" }
    'kb'    { $full = ''; $mini = "D:\BqMoni_Claude\p188\out\mini_${Tag}kb"; $MiniOnly = $true
              New-Item -ItemType Directory -Force "D:\BqMoni_Claude\p188\curves\mini_$Tag" | Out-Null
              $extra = @("--dump-curves=D:\BqMoni_Claude\p188\curves\mini_$Tag") }
    default { "неизвестное плечо: $Arm" | Out-File -Append $log; exit 2 }
}
$score = "$root\tools\pie\score.py"
$minicsv = "$root\tools\CORPUS\corpus\mini.csv"
Set-Location $root
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') store=$Store bin=$bin" | Out-File -Append $log
if (-not $SkipWd) {
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Arm.log"
    "arm $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$root\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Arm.log"
    "arm $Arm check_appwd code=$LASTEXITCODE" | Out-File -Append $log
    "  response: rmx=" + (Get-ChildItem "$wd\config\device\response\*.rmx" -ErrorAction SilentlyContinue).Count | Out-File -Append $log
}
if (-not $MiniOnly) {
    $t0 = Get-Date
    & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_full_$Arm.log"
    "arm $Arm full code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
}
$t0 = Get-Date
if ($extra.Count -gt 0) {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $extra *> "$art\run_mini_$Arm.log"
} else {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_mini_$Arm.log"
}
"arm $Arm mini code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    if (-not $MiniOnly) {
        & python $score --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_${Arm}_$part.txt"
        "arm $Arm score full $part code=$LASTEXITCODE" | Out-File -Append $log
    }
    & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_${Arm}_$part.txt"
    "arm $Arm score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
Copy-Item "$mini\.run.json" "$art\run_out_${Arm}_mini.json" -ErrorAction SilentlyContinue
if (-not $MiniOnly) { Copy-Item "$full\.run.json" "$art\run_out_${Arm}_full.json" -ErrorAction SilentlyContinue }
"arm $Arm end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Get-Content $log | Select-String "arm $Arm " | ForEach-Object { $_.Line }

