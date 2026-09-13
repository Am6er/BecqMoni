# П40 13.09.2026 — сборка основного дерева после переворота умолчаний трёх полей физики 16
# EfficiencySimulator (LightSubKevCurve/LightCascadeSplit раскладкой KDipLight, SplitXrayShells):
# Release_p40 + obj\Release_p40 + пробы build_all -Out build_p40. Коды возврата — в codes.txt,
# логи рядом (A77: смотреть КОД, а не факт запуска). MSBuild — только из PowerShell;
# /p:GenerateManifests=false обязателен (T75).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p40-sim-defaults"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Set-Location $root
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p40\' `
    /p:IntermediateOutputPath='obj\Release_p40\' /nologo /v:m *> "$art\build_app.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p40' `
    -Out 'tools\effmaker\probes\build_p40' *> "$art\build_probes.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"app exe sha256 $((Get-FileHash "$root\BecquerelMonitor\bin\Release_p40\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"probes exe sha256 $((Get-FileHash "$root\tools\effmaker\probes\build_p40\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"build end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
exit $code
