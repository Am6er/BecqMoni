# П123 22.09.2026 — сборка worktree D:\BqMoni_Claude\p122\wt в каталог Release_p123 / obj\Release_p123 / probes\build_p123.
# Образец — D:\BqMoni_Claude\p122\build_wt.ps1 (П122). Restore + Release + build_all. Коды — codes.txt (A77: смотреть КОД).
param([string]$Tag = 'p123')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p122\wt'
$art = 'D:\BqMoni_Claude\p123\art'
New-Item -ItemType Directory -Force $art | Out-Null
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build[$Tag] start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Set-Location $wt
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore_$Tag.log"
"restore[$Tag] code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath="bin\Release_$Tag\" `
    /p:IntermediateOutputPath="obj\Release_$Tag\" /nologo /v:m *> "$art\build_app_$Tag.log"
$code = $LASTEXITCODE
"app[$Tag] code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { Get-Content "$art\build_app_$Tag.log" | Select-Object -Last 20; exit $code }
Push-Location $wt
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "BecquerelMonitor\bin\Release_$Tag" `
    -Out "tools\effmaker\probes\build_$Tag" *> "$art\build_probes_$Tag.log"
$code = $LASTEXITCODE
"probes[$Tag] code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
if ($code -ne 0) { Get-Content "$art\build_probes_$Tag.log" | Select-Object -Last 30 }
"app exe sha256 $((Get-FileHash "$wt\BecquerelMonitor\bin\Release_$Tag\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"probes exe sha256 $((Get-FileHash "$wt\tools\effmaker\probes\build_$Tag\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"build[$Tag] end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Get-Content "$art\codes.txt" | Select-Object -Last 6
exit $code
