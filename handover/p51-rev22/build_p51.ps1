# П51 14.09.2026 — сборка основного дерева (HEAD 391f9b71, физика 18) после переноса склада:
# Release_p51 + obj\Release_p51 + пробы build_all -Out build_p51. Коды возврата — в codes.txt, логи
# рядом (A77: смотреть КОД, а не факт запуска). MSBuild — только из PowerShell; /p:GenerateManifests=false
# обязателен (T75). Пакеты NuGet в основном дереве есть — Restore не зовётся. Образец — П38 build_p38.ps1.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p51-rev22"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') head=$(git -C $root rev-parse --short HEAD)" | Out-File -Append "$art\codes.txt"
Set-Location $root
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p51\' `
    /p:IntermediateOutputPath='obj\Release_p51\' /nologo /v:m *> "$art\build_app.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p51' `
    -Out 'tools\effmaker\probes\build_p51' *> "$art\build_probes.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"app exe sha256 $((Get-FileHash "$root\BecquerelMonitor\bin\Release_p51\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"probes exe sha256 $((Get-FileHash "$root\tools\effmaker\probes\build_p51\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"build end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
exit $code
