$env:OS = 'Windows_NT'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
& 'tools\CORPUS\scripts\mk_appwd.ps1' -Bin 'BecquerelMonitor\bin\Release_p48' -Wd 'tools\CORPUS\scripts\wd_p48' -ProbeBuild 'tools\effmaker\probes\build_p48'
Write-Host "MK_APPWD EXIT $LASTEXITCODE"
exit $LASTEXITCODE
