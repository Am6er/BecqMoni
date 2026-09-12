# П28 12.09.2026 — сборка приложения (Release) в СВОЙ каталог для проверки оснастки.
# Зачем своя сборка, хотя задание её не просило: у всех готовых Release_P2x в дереве
# два исходника приложения (EnergySpectrumView.cs, FsaAnalyzer.cs — правки соседей)
# НОВЕЕ exe, и build_all.ps1 при заверении отказал бы по T41 (сборка старше исходников).
# Лог — build_app.log рядом; код MSBuild — последней строкой.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log  = "$root\handover\p28-t-rig\build_app.log"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File $log
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P28t\' /p:IntermediateOutputPath='obj\Release_P28t\' `
    /v:m /nologo *>> "$root\handover\p28-t-rig\msbuild_app.log"
$code = $LASTEXITCODE
"msbuild code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
exit $code
