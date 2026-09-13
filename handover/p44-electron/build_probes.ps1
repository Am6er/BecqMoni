# П44: сборка проб в свой каталог из своей сборки приложения.
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log = 'D:\BqMoni_Claude\p44\build_probes.log'
Set-Location $root
& pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p44' -Out 'tools\effmaker\probes\build_p44' 2>&1 | Out-File -Encoding utf8 $log
$code = $LASTEXITCODE
"exit=$code" | Out-File -Append -Encoding utf8 $log
Get-Content $log | Select-String -Pattern 'error CS|СЛОМАН|не собрал|exit=|ИТОГ|собрал' | Select-Object -First 30
"BUILD_PROBES_EXIT=$code"
exit $code
