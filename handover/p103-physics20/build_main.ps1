# П103 — сборка ГЛАВНОГО дерева после переноса (физика 20 + B31): Release_p103 (/t:Rebuild) + build_all -Out build_p103 —
# для прогона базы rev30 из главного дерева (задание: «корпус в конце гони из ГЛАВНОГО дерева после переноса своего»).
# Коды — codes_main.txt (A77: смотреть КОД). MSBuild — только из PowerShell; /p:GenerateManifests=false обязателен (T75).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = 'D:\BqMoni_Claude\p103\art'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build main start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes_main.txt"
Set-Location $wt
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p103\' `
    /p:IntermediateOutputPath='obj\Release_p103\' /nologo /v:m *> "$art\build_app_main.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_main.txt"
if ($code -ne 0) { Get-Content "$art\build_app_main.log" | Select-String 'error' | Select-Object -First 20; exit $code }
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p103' `
    -Out 'tools\effmaker\probes\build_p103' *> "$art\build_probes_main.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_main.txt"
if ($code -ne 0) { Get-Content "$art\build_probes_main.log" | Select-String 'error|ОТКАЗ|⛔' | Select-Object -First 20 }
"app exe sha256 $((Get-FileHash "$wt\BecquerelMonitor\bin\Release_p103\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes_main.txt"
if (Test-Path "$wt\tools\effmaker\probes\build_p103\BecquerelMonitor.exe") {
    "probes exe sha256 $((Get-FileHash "$wt\tools\effmaker\probes\build_p103\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes_main.txt"
}
"build main end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_main.txt"
Get-Content "$art\codes_main.txt" | Select-Object -Last 6
exit $code
