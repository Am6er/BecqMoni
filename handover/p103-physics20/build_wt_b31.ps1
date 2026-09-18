# П103 — плечо «B31 без физики 20»: сборка ЧИСТОГО worktree D:\BqMoni_Claude\p103\wt_b31 (HEAD 42291677, физика 19, без правок кода).
# Restore + Release_p103b (obj\Release_p103b) + build_all -Out build_p103b. Коды возврата — в codes.txt (A77:
# смотреть КОД, а не факт запуска). MSBuild — только из PowerShell; /p:GenerateManifests=false обязателен (T75);
# $env:OS нужен build_all для runtimes\.
param([switch]$SkipRestore)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p103\wt_b31'
$art = 'D:\BqMoni_Claude\p103\art'
New-Item -ItemType Directory -Force $art | Out-Null
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes_b31.txt"
Set-Location $wt
if (-not $SkipRestore) {
    & $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU `
        /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore_b31.log"
    "restore code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_b31.txt"
}
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p103b\' `
    /p:IntermediateOutputPath='obj\Release_p103b\' /nologo /v:m *> "$art\build_app_b31.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_b31.txt"
if ($code -ne 0) { Get-Content "$art\build_app_b31.log" | Select-String 'error' | Select-Object -First 20; exit $code }
Push-Location $wt
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_p103b' `
    -Out 'tools\effmaker\probes\build_p103b' *> "$art\build_probes_b31.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_b31.txt"
Pop-Location
if ($code -ne 0) { Get-Content "$art\build_probes_b31.log" | Select-String 'error|ОТКАЗ|⛔' | Select-Object -First 20 }
"app exe sha256 $((Get-FileHash "$wt\BecquerelMonitor\bin\Release_p103b\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes_b31.txt"
if (Test-Path "$wt\tools\effmaker\probes\build_p103b\BecquerelMonitor.exe") {
    "probes exe sha256 $((Get-FileHash "$wt\tools\effmaker\probes\build_p103b\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes_b31.txt"
}
"build end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_b31.txt"
Get-Content "$art\codes_b31.txt" | Select-Object -Last 6
exit $code
