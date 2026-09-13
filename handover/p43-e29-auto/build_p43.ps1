# П43 13.09.2026 (E29) — сборка дерева с автоматикой розыгрыша на полевых сценах (путь кривой):
# Release_p43 + obj\Release_p43 + пробы build_all -Out build_p43. Коды возврата — в codes.txt,
# логи рядом (A77: смотреть КОД, а не факт запуска). MSBuild — только из PowerShell;
# /p:GenerateManifests=false обязателен (T75).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p43-e29-auto"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Set-Location $root
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p43\' `
    /p:IntermediateOutputPath='obj\Release_p43\' /nologo /v:m *> "$art\build_app.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p43' `
    -Out 'tools\effmaker\probes\build_p43' *> "$art\build_probes.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"app exe sha256 $((Get-FileHash "$root\BecquerelMonitor\bin\Release_p43\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"probes exe sha256 $((Get-FileHash "$root\tools\effmaker\probes\build_p43\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"build end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
exit $code
