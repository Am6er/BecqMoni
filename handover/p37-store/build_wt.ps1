# П37 13.09.2026 — единый счёт склада физики 17: сборка worktree C:\Users\moroz\bqp37 (HEAD 639bfee9 +
# правки полосы: умолчания семи ключей, PhysicsVersion 17, путь кривой). Restore (пакетов NuGet в
# worktree нет — П23/П27) + Release_p37 + build_all -Out build_p37. Коды возврата — в codes.txt,
# логи рядом (A77: смотреть КОД, а не факт запуска). MSBuild — только из PowerShell;
# /p:GenerateManifests=false обязателен (T75).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'C:\Users\moroz\bqp37'
$art = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p37-store'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Set-Location $wt
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore.log"
"restore code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p37\' `
    /p:IntermediateOutputPath='obj\Release_p37\' /nologo /v:m *> "$art\build_app.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
Push-Location $wt
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p37' `
    -Out 'tools\effmaker\probes\build_p37' *> "$art\build_probes.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
"app exe sha256 $((Get-FileHash "$wt\BecquerelMonitor\bin\Release_p37\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"probes exe sha256 $((Get-FileHash "$wt\tools\effmaker\probes\build_p37\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
exit $code
