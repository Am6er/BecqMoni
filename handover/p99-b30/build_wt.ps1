# П99 18.09.2026 — сборка worktree D:\BqMoni_Claude\p99\wt (HEAD 93dafe11 + правки полосы).
# Restore + Release_p99 (obj\Release_p99) + build_all -Out build_p99. Коды возврата — в art\codes.txt (A77:
# смотреть КОД, а не факт запуска). MSBuild — только из PowerShell; /p:GenerateManifests=false обязателен (T75);
# $env:OS нужен build_all для runtimes\.
param([switch]$SkipRestore, [string]$Tag = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p99\wt'
$art = 'D:\BqMoni_Claude\p99\art'
New-Item -ItemType Directory -Force $art | Out-Null
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build$Tag start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Set-Location $wt
if (-not $SkipRestore) {
    & $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU `
        /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore$Tag.log"
    "restore code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
}
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p99\' `
    /p:IntermediateOutputPath='obj\Release_p99\' /nologo /v:m *> "$art\build_app$Tag.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { Get-Content "$art\build_app$Tag.log" | Select-String 'error' | Select-Object -First 20; exit $code }
Push-Location $wt
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p99' `
    -Out 'tools\effmaker\probes\build_p99' *> "$art\build_probes$Tag.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
if ($code -ne 0) { Get-Content "$art\build_probes$Tag.log" | Select-String 'error|ОТКАЗ|⛔' | Select-Object -First 20 }
"app exe sha256 $((Get-FileHash "$wt\BecquerelMonitor\bin\Release_p99\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
if (Test-Path "$wt\tools\effmaker\probes\build_p99\BecquerelMonitor.exe") {
    "probes exe sha256 $((Get-FileHash "$wt\tools\effmaker\probes\build_p99\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
}
"build$Tag end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Get-Content "$art\codes.txt" | Select-Object -Last 6
exit $code
