# П179 — плечо прогона малой и (по ключу) полной базы из worktree; склад основного дерева ТОЛЬКО чтением.
#   & D:\BqMoni_Claude\p179\run_arm_p179.ps1 -Arm base -Build base [-Full] [-LightTable файл] [-KbShift 0.4]
param([Parameter(Mandatory)][string]$Arm, [Parameter(Mandatory)][string]$Build,
      [switch]$Full, [switch]$SkipWd, [string]$LightTable = '', [string]$KbShift = '', [string]$Root = 'D:\BqMoni_Claude\p179\wt')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = $Root
$art  = 'D:\BqMoni_Claude\p179\art'
$log  = "$art\arm_codes.txt"
$bin  = "$root\BecquerelMonitor\bin\Release_p179_$Build"
$pb   = "D:\BqMoni_Claude\p179\build_p179_$Build"
$wd   = "$root\tools\CORPUS\scripts\wd_p179$Build"
$Store = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$fullOut = "D:\BqMoni_Claude\p179\out\full_$Arm"; $mini = "D:\BqMoni_Claude\p179\out\mini_$Arm"
$score = "$root\tools\pie\score.py"; $minicsv = "$root\tools\CORPUS\corpus\mini.csv"
if ($LightTable) { $env:P179_LIGHT_TABLE = $LightTable } else { Remove-Item Env:P179_LIGHT_TABLE -ErrorAction SilentlyContinue }
if ($KbShift) { $env:P179_KB_SHIFT = $KbShift } else { Remove-Item Env:P179_KB_SHIFT -ErrorAction SilentlyContinue }
Set-Location $root
"arm $Arm start $(Get-Date -Format 'HH:mm:ss') build=$Build light=$LightTable kb=$KbShift" | Out-File -Append $log
if (-not $SkipWd) {
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Build.log"
    "arm $Arm mk_appwd code=$LASTEXITCODE" | Out-File -Append $log
    & "$root\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Build.log"
    "arm $Arm check_appwd code=$LASTEXITCODE" | Out-File -Append $log
}
if ($Full) {
    & "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out $fullOut -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\run_full_$Arm.log"
    "arm $Arm full code=$LASTEXITCODE" | Out-File -Append $log
}
& "$root\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -SkipScore *> "$art\run_mini_$Arm.log"
"arm $Arm mini code=$LASTEXITCODE" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    if ($Full) { & python $score --mode=spline "--out-dir=$fullOut" "--part=$part" --members *> "$art\score_full_${Arm}_$part.txt"
                 "arm $Arm score full $part code=$LASTEXITCODE" | Out-File -Append $log }
    & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_${Arm}_$part.txt"
    "arm $Arm score mini $part code=$LASTEXITCODE" | Out-File -Append $log
}
"arm $Arm end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Get-Content $log | Select-String "arm $Arm " | ForEach-Object { $_.Line }
