# П27 12.09.2026, `A72`: сборка ДЕРЕВА С ПРАВКОЙ — приложение Release_P27 + пробы build_p27.
# Рецепт: MSBuild ТОЛЬКО из PowerShell, /p:GenerateManifests=false обязателен (T75),
# свой OutputPath и IntermediateOutputPath (чужие сборки в дереве не трогаются).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p27-electron-transport"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P27\' `
    /p:IntermediateOutputPath='obj\Release_P27\' /nologo /v:m *> "$art\build_app.log"
"app code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($LASTEXITCODE -ne 0) { exit 1 }
Push-Location $root
pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P27' `
    -Out 'tools\effmaker\probes\build_p27' *> "$art\build_probes.log"
"probes code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
