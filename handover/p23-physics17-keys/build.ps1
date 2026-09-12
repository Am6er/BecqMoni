# П23 12.09.2026 — сборка приложения (Release_P23) и каталога проб (build_p23).
# Рецепт — по заданию полосы: MSBuild ТОЛЬКО из PowerShell, свой OutputPath и
# IntermediateOutputPath, без манифестов; перед MSBuild — $env:OS (T253).
# Коды возврата пишутся в codes.txt рядом; журнал — build_app.log / build_probes.log.
param([switch]$ProbesOnly)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$out = "$root\handover\p23-physics17-keys"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
Set-Location $root
if (-not $ProbesOnly) {
    & $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
        /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P23\' `
        /p:IntermediateOutputPath='obj\Release_P23\' /nologo /v:m *> "$out\build_app.log"
    "app code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
& pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P23' -Out 'tools\effmaker\probes\build_p23' *> "$out\build_probes.log"
"probes code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
exit $LASTEXITCODE
