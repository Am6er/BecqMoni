# П26 12.09.2026, `AMBER22` — сборка приложения (Release) и проб в СВОИ каталоги.
# Запуск: pwsh -File handover\p26-amber22\build_p26.ps1  (лог — рядом, build_p26.log)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log = "$root\handover\p26-amber22\build_p26.log"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File $log
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P26\' /p:IntermediateOutputPath='obj\Release_P26\' `
    /v:m /nologo *>> "$root\handover\p26-amber22\msbuild.log"
$code = $LASTEXITCODE
"msbuild code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
if ($code -ne 0) { exit $code }
Set-Location $root
& pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P26' -Out 'tools\effmaker\probes\build_p26' *>> "$root\handover\p26-amber22\build_all.log"
$code = $LASTEXITCODE
"build_all code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
exit $code
