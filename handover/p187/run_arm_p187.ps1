# П187 — малая база (или свой список) из worktree с --dump-curves. Копия run_arm_p181.ps1 (плечо kb).
#   & D:\BqMoni_Claude\p187\run_arm_p187.ps1 -Arm base [-Tag base] [-List <csv>] [-Extra @('--key')] [-SkipWd]
param([Parameter(Mandatory)][string]$Arm, [string]$Tag = 'base', [string]$List = '', [string[]]$Extra = @(), [switch]$SkipWd)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'D:\BqMoni_Claude\p187\wt'
$art  = 'D:\BqMoni_Claude\p187\art'
$log  = "$art\arm_codes.txt"
$bin  = "$root\BecquerelMonitor\bin\Release_P187_$Tag"
$pb   = "D:\BqMoni_Claude\p187\build_$Tag"
$wd   = "$root\tools\CORPUS\scripts\wd_p187_$Tag"
$Store = "$root\tools\CORPUS\corpus\geometries"
$mini = "D:\BqMoni_Claude\p187\out\$Arm"
$cur  = "D:\BqMoni_Claude\p187\curves\$Arm"
New-Item -ItemType Directory -Force $cur | Out-Null
$ex = @("--dump-curves=$cur") + $Extra
Set-Location $root
"arm $Arm start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') bin=$bin list=$List extra=$($Extra -join ' ')" | Out-File -Append $log
if (-not $SkipWd) {
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Arm.log"
    "arm $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$root\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Arm.log"
    "arm $Arm check_appwd code=$LASTEXITCODE" | Out-File -Append $log
}
$t0 = Get-Date
if ($List) {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -List $List -Extra $ex *> "$art\run_mini_$Arm.log"
} else {
    & "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra $ex *> "$art\run_mini_$Arm.log"
}
"arm $Arm mini code=$LASTEXITCODE ($([int]((Get-Date)-$t0).TotalSeconds) s) out=$mini" | Out-File -Append $log
$only = if ($List) { $List } else { "$root\tools\CORPUS\corpus\mini.csv" }
foreach ($part in @('known', 'unknown')) {
    & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$only" *> "$art\score_${Arm}_$part.txt"
    "arm $Arm score $part code=$LASTEXITCODE" | Out-File -Append $log
}
Get-Content $log | Select-String "arm $Arm " | ForEach-Object { $_.Line }
