# П27 12.09.2026, `A72`: ЭТАЛОННАЯ сборка «до правки» — worktree C:\Users\moroz\bqp27 на HEAD
# (f981f955), приложение Release_P27ref + пробы build_p27ref. Нужна плечу побитового
# контроля ВЫКЛ (MatrixDiffProbe: эталон HEAD против дерева с ключом ВЫКЛ).
# Рецепт: MSBuild ТОЛЬКО из PowerShell, /p:GenerateManifests=false обязателен (T75).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'C:\Users\moroz\bqp27'
$art = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p27-electron-transport'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
# Пакеты NuGet в worktree не лежат (packages/ вне git) — сперва Restore, как у П23.
Set-Location $wt
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore_ref.log"
"restore_ref code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_ref.txt"
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P27ref\' `
    /p:IntermediateOutputPath='obj\Release_P27ref\' /nologo /v:m *> "$art\build_app_ref.log"
"app_ref code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_ref.txt"
if ($LASTEXITCODE -ne 0) { exit 1 }
Push-Location $wt
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P27ref' `
    -Out 'tools\effmaker\probes\build_p27ref' *> "$art\build_probes_ref.log"
"probes_ref code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_ref.txt"
Pop-Location
