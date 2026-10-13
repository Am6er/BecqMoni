# П180 — плечо малой базы из worktree; склад — ключом -Store (по умолчанию склад основного дерева ТОЛЬКО чтением).
#   & D:\BqMoni_Claude\p180\run_arm_p180.ps1 -Arm base -Build base [-Store каталог] [-SkipWd]
param([Parameter(Mandatory)][string]$Arm, [Parameter(Mandatory)][string]$Build, [switch]$SkipWd,
      [string]$Store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries',
      [string]$Root = 'D:\BqMoni_Claude\p180\wt')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = $Root
$art  = 'D:\BqMoni_Claude\p180\art'
$log  = "$art\arm_codes.txt"
$bin  = "$root\BecquerelMonitor\bin\Release_p180_$Build"
$pb   = "D:\BqMoni_Claude\p180\build_p180_$Build"
$wd   = "$root\tools\CORPUS\scripts\wd_p180$Arm"
$mini = "D:\BqMoni_Claude\p180\out\mini_$Arm"; $curves = "D:\BqMoni_Claude\p180\curves\$Arm"
New-Item -ItemType Directory -Force $curves | Out-Null
$score = "$root\tools\pie\score.py"; $minicsv = "$root\tools\CORPUS\corpus\mini.csv"
Set-Location $root
"arm $Arm start $(Get-Date -Format 'HH:mm:ss') build=$Build store=$Store" | Out-File -Append $log
if (-not $SkipWd) {
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Arm.log"
    "arm $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$root\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Arm.log"
    "arm $Arm check_appwd code=$LASTEXITCODE" | Out-File -Append $log
}
& "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -SkipScore -Extra @("--dump-curves=$curves") *> "$art\run_mini_$Arm.log"
"arm $Arm mini code=$LASTEXITCODE" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_${Arm}_$part.txt"
    "arm $Arm score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
"arm $Arm end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Get-Content $log | Select-String "arm $Arm " | ForEach-Object { $_.Line }
