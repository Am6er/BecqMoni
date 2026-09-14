# bqp9_arm.ps1 -Name a|b|v|g -Transfer 0|1 -NewMx 0|1 -Store <склад> -Out <каталог out> -Dump <каталог дампа>
param([string]$Name, [string]$Transfer, [string]$NewMx, [string]$Store, [string]$Out, [string]$Dump)
$ErrorActionPreference = 'Continue'
$wt = 'C:\Users\moroz\bqp9'
python C:\Users\moroz\bqp9_setdef.py $Transfer $NewMx
if ($LASTEXITCODE -ne 0) { Write-Output 'SETDEF FAILED'; exit 9 }
$bin = "$wt\BecquerelMonitor\bin\Debug_p9$Name"
$pb  = "$wt\tools\effmaker\probes\build_p9$Name"
$wd  = "$wt\tools\CORPUS\scripts\wd_p9$Name"
$o = & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform='AnyCPU' `
  /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Debug_p9$Name\" /v:m /nologo 2>&1
$code = $LASTEXITCODE
$o | Select-Object -Last 5
Write-Output "MSBUILD CODE $code"
if ($code -ne 0) { exit 10 }
$o = & "$wt\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $pb 2>&1
$code = $LASTEXITCODE
$o | Select-Object -Last 4
Write-Output "BUILD_ALL CODE $code"
if ($code -ne 0) { exit 11 }
$o = & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $pb -Store $Store 2>&1
$code = $LASTEXITCODE
$o | Select-Object -Last 4
Write-Output "MK_APPWD CODE $code"
if ($code -ne 0) { exit 12 }
$o = & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $Out -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store -Extra "--dump-curves=$Dump" 2>&1
$code = $LASTEXITCODE
$o | Select-Object -Last 16
Write-Output "RUN_MINI CODE $code"
exit $code
