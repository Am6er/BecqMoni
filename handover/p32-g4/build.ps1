# П32 12.09.2026, `F16`/`A64` — сборка ДЕРЕВА на HEAD (cdf86bbf): приложение Release_P32 + пробы build_p32.
# Рецепт: MSBuild ТОЛЬКО из PowerShell, /p:GenerateManifests=false обязателен (T75),
# свой OutputPath и IntermediateOutputPath (чужие сборки в дереве не трогаются).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p32-g4"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P32\' `
    /p:IntermediateOutputPath='obj\Release_P32\' /nologo /v:m *> "$art\build_app.log"
"app code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($LASTEXITCODE -ne 0) { exit 1 }
Push-Location $root
pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P32' `
    -Out 'tools\effmaker\probes\build_p32' *> "$art\build_probes.log"
"probes code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
