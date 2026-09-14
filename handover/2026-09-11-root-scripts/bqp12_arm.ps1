# bqp12_arm.ps1 -Name a|b -Transfer 0|1 -NewMx 0|1 -Store <склад>  — собрать плечо в копии bqp12
# (сборка приложения в свой bin/obj, пробы, оснастка). Прогоны — отдельно.
param([string]$Name, [string]$Transfer, [string]$NewMx, [string]$Store)
$ErrorActionPreference = 'Continue'
$wt = 'C:\Users\moroz\bqp12'
python C:\Users\moroz\bqp12_setdef.py $Transfer $NewMx
if ($LASTEXITCODE -ne 0) { Write-Output 'SETDEF FAILED'; exit 9 }
$bin = "$wt\BecquerelMonitor\bin\Debug_p12$Name"
$pb  = "$wt\tools\effmaker\probes\build_p12$Name"
$wd  = "$wt\tools\CORPUS\scripts\wd_p12$Name"
$o = & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform='AnyCPU' `
  /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Debug_p12$Name\" `
  "/p:IntermediateOutputPath=obj\Debug_p12$Name\" /v:m /nologo 2>&1
$code = $LASTEXITCODE
$o | Select-Object -Last 5
Write-Output "MSBUILD CODE $code"
if ($code -ne 0) { exit 10 }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("exe: {0} {1} bytes" -f $_.LastWriteTime, $_.Length) }
$o = & "$wt\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $pb 2>&1
$code = $LASTEXITCODE
$o | Select-Object -Last 4
Write-Output "BUILD_ALL CODE $code"
if ($code -ne 0) { exit 11 }
Get-Item "$pb\CorpusFsaProbe.exe" | ForEach-Object { Write-Output ("probe: {0} {1} bytes" -f $_.LastWriteTime, $_.Length) }
$o = & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $pb -Store $Store 2>&1
$code = $LASTEXITCODE
$o | Select-Object -Last 4
Write-Output "MK_APPWD CODE $code"
exit $code
