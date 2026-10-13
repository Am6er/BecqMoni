# П186 — малая база копией-рычагом CorpusFsaProbeP186.exe (--fwhm-low=A:E0:SPAN). Оснастка wd_p186_lever из build_lever.
#   & D:\BqMoni_Claude\p186\run_lever.ps1 -Arm lev0 -Lever '0:60:32' [-SkipWd]
param([Parameter(Mandatory)][string]$Arm, [string]$Lever = '', [string[]]$Extra = @(), [switch]$SkipWd)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'D:\BqMoni_Claude\p186\wt'
$art  = 'D:\BqMoni_Claude\p186\art'
$log  = "$art\arm_codes.txt"
$bin  = "$root\BecquerelMonitor\bin\Release_P186_base"
$pb   = 'D:\BqMoni_Claude\p186\build_lever'
$wd   = "$root\tools\CORPUS\scripts\wd_p186_lever"
$Store = "$root\tools\CORPUS\corpus\geometries"
$out  = "D:\BqMoni_Claude\p186\out\$Arm"
$cur  = "D:\BqMoni_Claude\p186\curves\$Arm"
$mini = "$root\tools\CORPUS\corpus\mini.csv"
New-Item -ItemType Directory -Force $cur, $out | Out-Null
if (-not $SkipWd) {
    & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_lever.log"
    "arm $Arm mk_appwd(lever) code=$LASTEXITCODE" | Out-File -Append $log
    & "$root\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_lever.log"
    "arm $Arm check_appwd(lever) code=$LASTEXITCODE" | Out-File -Append $log
}
$keys = (Get-Content $mini | Where-Object { $_ -notmatch '^\s*#' -and $_ -notmatch '^spectrum,' -and $_.Trim() } | ForEach-Object { ($_ -split ',')[0] }) -join ','
$argv = @("--corpus=$root\tools\CORPUS\corpus", "--out=$out", "--only=$keys", "--dump-curves=$cur") + $Extra
if ($Lever) { $argv += "--fwhm-low=$Lever" }
Push-Location -LiteralPath $wd
try { & "$wd\CorpusFsaProbeP186.exe" @argv *> "$art\run_$Arm.log"; $rc = $LASTEXITCODE } finally { Pop-Location }
"arm $Arm probe code=$rc lever=$Lever" | Out-File -Append $log
foreach ($part in @('known', 'unknown')) {
    & python "$root\tools\pie\score.py" --mode=spline "--out-dir=$out" "--part=$part" --members "--only=$mini" *> "$art\score_${Arm}_$part.txt"
    "arm $Arm score $part code=$LASTEXITCODE" | Out-File -Append $log
}
Get-Content $log | Select-String "arm $Arm " | ForEach-Object { $_.Line }
