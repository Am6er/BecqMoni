# П142 23.09.2026 — прогоны корпуса ИЗ ОСНОВНОГО ДЕРЕВА после слияния физики 23 (образец — П114 run_arm.ps1).
#   -Arm rev33      : объявляемая база, живой склад (физика 23) -> tools\pie\out_rev33_{full,mini}
#   -Arm ctrl       : контроль `AMBER70` — сборка БЕЗ подстановки ImageBins (build_p142ctrl),
#                     тот же живой склад -> tools\pie\out_p142_ctrl_{full,mini}
# run_appwd.ps1 / run_mini.ps1 зовутся оператором `&` (T84/T91); клеймо .run.json пишут они сами (T249).
#   pwsh -File handover\p142-rev33\run_arm_p142.ps1 -Arm rev33
param([Parameter(Mandatory)][string]$Arm,
      [string]$Store = '',
      [string]$Tag = '',
      [switch]$SkipWd,
      [switch]$MiniOnly,
      [string[]]$Extra = @())
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art  = "$root\handover\p142-rev33"
$log  = "$art\arm_codes.txt"
switch ($Arm) {
    'rev33' { $bin = "$root\BecquerelMonitor\bin\Release_p142"; $pb = "$root\tools\effmaker\probes\build_p142"
              $wd = "$root\tools\CORPUS\scripts\wd_p142"
              $full = "$root\tools\pie\out_rev33_full$Tag"; $mini = "$root\tools\pie\out_rev33_mini$Tag" }
    'ctrl'  { $bin = "$root\BecquerelMonitor\bin\Release_p142ctrl"; $pb = "$root\tools\effmaker\probes\build_p142ctrl"
              $wd = "$root\tools\CORPUS\scripts\wd_p142ctrl"
              $full = "$root\tools\pie\out_p142_ctrl_full$Tag"; $mini = "$root\tools\pie\out_p142_ctrl_mini$Tag" }
    default { "неизвестное плечо: $Arm" | Out-File -Append $log; exit 2 }
}
if (-not $Store) { $Store = "$root\tools\CORPUS\corpus\geometries" }
$score = "$root\tools\pie\score.py"
$minicsv = "$root\tools\CORPUS\corpus\mini.csv"
Set-Location $root
"arm $Arm$Tag start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') store=$Store bin=$bin" | Out-File -Append $log
if (-not $SkipWd) {
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Arm$Tag.log"
    "arm $Arm$Tag mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$root\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Arm$Tag.log"
    "arm $Arm$Tag check_appwd code=$LASTEXITCODE" | Out-File -Append $log
    "  response: rmx=" + (Get-ChildItem "$wd\config\device\response\*.rmx" -ErrorAction SilentlyContinue).Count | Out-File -Append $log
}
if (-not $MiniOnly) {
    $t0 = Get-Date
    & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_full_$Arm$Tag.log"
    "arm $Arm$Tag full code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
}
$t0 = Get-Date
if ($Extra.Count -gt 0) {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $Extra *> "$art\run_mini_$Arm$Tag.log"
} else {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_mini_$Arm$Tag.log"
}
"arm $Arm$Tag mini code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    if (-not $MiniOnly) {
        & python $score --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_$Arm${Tag}_$part.txt"
        "arm $Arm$Tag score full $part code=$LASTEXITCODE" | Out-File -Append $log
    }
    & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_$Arm${Tag}_$part.txt"
    "arm $Arm$Tag score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
Copy-Item "$mini\.run.json" "$art\run_out_$Arm${Tag}_mini.json" -ErrorAction SilentlyContinue
if (-not $MiniOnly) { Copy-Item "$full\.run.json" "$art\run_out_$Arm${Tag}_full.json" -ErrorAction SilentlyContinue }
"arm $Arm$Tag end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Get-Content $log | Select-String "arm $Arm$Tag " | ForEach-Object { $_.Line }
