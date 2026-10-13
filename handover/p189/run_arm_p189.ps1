# П189 — прогон плеча из worktree: полная (run_appwd) + малая (run_mini), score обеих частей. Копия run_arm_p181/p187.
#   & D:\BqMoni_Claude\p189\run_arm_p189.ps1 -Arm base -Tag base [-MiniOnly] [-Extra @('--dump-curves=...')]
param([Parameter(Mandatory)][string]$Arm, [string]$Tag = 'base', [string]$BinRoot = 'D:\BqMoni_Claude\p189\wt', [switch]$MiniOnly, [string[]]$Extra = @(), [switch]$SkipWd)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = $BinRoot
$art  = 'D:\BqMoni_Claude\p189\art'
New-Item -ItemType Directory -Force $art | Out-Null
$log  = "$art\arm_codes.txt"
$bin  = "$BinRoot\BecquerelMonitor\bin\Release_P189_$Tag"
$pb   = "D:\BqMoni_Claude\p189\build_$Tag"
$wd   = "$root\tools\CORPUS\scripts\wd_p189_$Tag"
$Store = "$root\tools\CORPUS\corpus\geometries"
$full = "D:\BqMoni_Claude\p189\out\full_$Arm"
$mini = "D:\BqMoni_Claude\p189\out\mini_$Arm"
$score = "$root\tools\pie\score.py"
$minicsv = "$root\tools\CORPUS\corpus\mini.csv"
Set-Location $root
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') bin=$bin extra=$($Extra -join ' ')" | Out-File -Append $log
if (-not $SkipWd) {
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Arm.log"
    "arm $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$root\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Arm.log"
    "arm $Arm check_appwd code=$LASTEXITCODE" | Out-File -Append $log
}
if (-not $MiniOnly) {
    $t0 = Get-Date
    if ($Extra.Count -gt 0) {
        & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $Extra *> "$art\run_full_$Arm.log"
    } else {
        & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_full_$Arm.log"
    }
    "arm $Arm full code=$LASTEXITCODE ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$full" | Out-File -Append $log
}
$t0 = Get-Date
if ($Extra.Count -gt 0) {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $Extra *> "$art\run_mini_$Arm.log"
} else {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_mini_$Arm.log"
}
"arm $Arm mini code=$LASTEXITCODE ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    if (-not $MiniOnly) {
        & python $score --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_${Arm}_$part.txt"
        "arm $Arm score full $part code=$LASTEXITCODE" | Out-File -Append $log
    }
    & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_${Arm}_$part.txt"
    "arm $Arm score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
Get-Content $log | Select-String "arm $Arm " | ForEach-Object { $_.Line }
