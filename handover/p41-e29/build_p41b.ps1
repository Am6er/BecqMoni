# П41 13.09.2026 (E29) — ВТОРАЯ сборка (смягчение направляющей A/2π) в ДРУГОЙ каталог, пока
# build_p41 занят прогоном ctrl_b: Release_p41b + obj\Release_p41b + пробы build_p41b.
# Коды возврата — в codes.txt (A77). MSBuild — только из PowerShell; /p:GenerateManifests=false (T75).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p41-e29"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build_b start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Set-Location $root
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p41b\' `
    /p:IntermediateOutputPath='obj\Release_p41b\' /nologo /v:m *> "$art\build_app_b.log"
$code = $LASTEXITCODE
"app_b code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p41b' `
    -Out 'tools\effmaker\probes\build_p41b' *> "$art\build_probes_b.log"
$code = $LASTEXITCODE
"probes_b code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"app_b exe sha256 $((Get-FileHash "$root\BecquerelMonitor\bin\Release_p41b\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"probes_b exe sha256 $((Get-FileHash "$root\tools\effmaker\probes\build_p41b\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"build_b end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
exit $code
