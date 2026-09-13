$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
$wt = 'D:\BqMoni_Claude\p49\wt'
$main = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $wt
$log = 'D:\BqMoni_Claude\p49\head.log'
"start $(Get-Date -Format HH:mm:ss)" | Out-File $log -Encoding utf8
& "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p49h" -Wd "$wt\tools\CORPUS\scripts\wd_p49h" -ProbeBuild "$wt\tools\effmaker\probes\build_p49h" -Store "$main\tools\CORPUS\corpus\geometries" *>> 'D:\BqMoni_Claude\p49\mk_head.log'
"mk_head exit=$LASTEXITCODE $(Get-Date -Format HH:mm:ss)" | Out-File $log -Append -Encoding utf8
& "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out "$main\tools\pie\out_p49_head" -Wd "$wt\tools\CORPUS\scripts\wd_p49h" -Bin "$wt\BecquerelMonitor\bin\Release_p49h" -ProbeBuild "$wt\tools\effmaker\probes\build_p49h" -Store "$main\tools\CORPUS\corpus\geometries" *>> 'D:\BqMoni_Claude\p49\arm_head.log'
"arm_head exit=$LASTEXITCODE $(Get-Date -Format HH:mm:ss)" | Out-File $log -Append -Encoding utf8
"done" | Out-File $log -Append -Encoding utf8
