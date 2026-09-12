# П39: сборка приложения Release_p39 и каталога проб build_p39 (свои каталоги полосы).
# Запуск: pwsh -File handover\p39-probe-truth\build_p39.ps1 [-Probes] [-App]
param([switch]$App, [switch]$Probes)
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
$log = Join-Path $repo 'handover\p39-probe-truth'
if ($App) {
    & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
        'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
        /p:SignManifests=false /p:GenerateManifests=false `
        /p:OutputPath='bin\Release_p39\' /p:IntermediateOutputPath='obj\Release_p39\' /v:m /nologo 2>&1 |
        Tee-Object -FilePath (Join-Path $log 'build.msbuild.log') | Select-Object -Last 3
    $rc = $LASTEXITCODE
    "msbuild rc=$rc" | Tee-Object -FilePath (Join-Path $log 'build.msbuild.log') -Append
    if ($rc -ne 0) { exit $rc }
}
if ($Probes) {
    & 'tools\effmaker\probes\build_all.ps1' -Bin 'BecquerelMonitor\bin\Release_p39' -Out 'tools\effmaker\probes\build_p39' 2>&1 |
        Tee-Object -FilePath (Join-Path $log 'build.probes.log') | Select-Object -Last 6
    $rc = $LASTEXITCODE
    "build_all rc=$rc" | Tee-Object -FilePath (Join-Path $log 'build.probes.log') -Append
    exit $rc
}
exit 0
