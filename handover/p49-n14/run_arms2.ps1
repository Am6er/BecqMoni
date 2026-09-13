$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $root
$log = 'D:\BqMoni_Claude\p49\arms2.log'
"start $(Get-Date -Format HH:mm:ss)" | Out-File $log -Encoding utf8
& "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$root\BecquerelMonitor\bin\Release_p49" -Wd "$root\tools\CORPUS\scripts\wd_p49" -ProbeBuild "$root\tools\effmaker\probes\build_p49" *>> 'D:\BqMoni_Claude\p49\mk_p49.log'
"mk exit=$LASTEXITCODE $(Get-Date -Format HH:mm:ss)" | Out-File $log -Append -Encoding utf8
Remove-Item -Recurse -Force "$root\tools\pie\out_p49_mini_off" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$root\tools\pie\out_p49_mini_on" -ErrorAction SilentlyContinue
& "$root\tools\CORPUS\scripts\run_mini.ps1" -Out "$root\tools\pie\out_p49_mini_off" -Wd "$root\tools\CORPUS\scripts\wd_p49" -Bin "$root\BecquerelMonitor\bin\Release_p49" -ProbeBuild "$root\tools\effmaker\probes\build_p49" *> 'D:\BqMoni_Claude\p49\arm_off.log'
"arm_off exit=$LASTEXITCODE $(Get-Date -Format HH:mm:ss)" | Out-File $log -Append -Encoding utf8
& "$root\tools\CORPUS\scripts\run_mini.ps1" -Out "$root\tools\pie\out_p49_mini_on" -Wd "$root\tools\CORPUS\scripts\wd_p49" -Bin "$root\BecquerelMonitor\bin\Release_p49" -ProbeBuild "$root\tools\effmaker\probes\build_p49" -Extra '--angcorr=1' *> 'D:\BqMoni_Claude\p49\arm_on.log'
"arm_on exit=$LASTEXITCODE $(Get-Date -Format HH:mm:ss)" | Out-File $log -Append -Encoding utf8
"done" | Out-File $log -Append -Encoding utf8
