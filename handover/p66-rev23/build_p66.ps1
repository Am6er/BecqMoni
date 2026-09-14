# П66 14.09.2026 — сборка из worktree D:\BqMoni_Claude\p66\wt (HEAD 16de7feb; в основном дереве —
# незакоммиченные правки П65, из него не собираем). Свежему worktree нужен /t:Restore ДО /t:Build
# (иначе CS0246 — грабля П64). Release_p66 + obj\Release_p66 + пробы build_all -Out build_p66.
# Коды возврата — в codes.txt, логи рядом (A77: смотреть КОД, а не факт запуска). MSBuild — только из
# PowerShell; /p:GenerateManifests=false обязателен (T75) — у вызова /t:Restore ключи дописаны ПОСЛЕ прогона
# ради сторожа check_build_recipe.py (сам Restore манифестов не строит; прогон шёл без них, лог build_restore*.log). Образец — П51 build_p51.ps1.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p66\wt'
$art = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p66-rev23'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') head=$(git -C $wt rev-parse --short HEAD)" | Out-File -Append "$art\codes.txt"
Set-Location $wt
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore.log"
$code = $LASTEXITCODE
"restore code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p66\' `
    /p:IntermediateOutputPath='obj\Release_p66\' /nologo /v:m *> "$art\build_app.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p66' `
    -Out 'tools\effmaker\probes\build_p66' *> "$art\build_probes.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"app exe sha256 $((Get-FileHash "$wt\BecquerelMonitor\bin\Release_p66\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"probes exe sha256 $((Get-FileHash "$wt\tools\effmaker\probes\build_p66\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"build end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
exit $code
