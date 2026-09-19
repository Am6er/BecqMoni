# П107 19.09.2026 — физика 21: сборка worktree D:\BqMoni_Claude\p107\wt (HEAD 645d4870 + правки полосы).
# Restore + Release_p107 (obj\Release_p107) + build_all -Out build_p107. Коды возврата — в codes.txt (A77:
# смотреть КОД, а не факт запуска). MSBuild — только из PowerShell; /p:GenerateManifests=false обязателен (T75);
# $env:OS нужен build_all для runtimes\. Образец — П103 build_wt.ps1.
param([switch]$SkipRestore, [string]$Wt = 'D:\BqMoni_Claude\p107\wt', [string]$Tag = 'p107', [string]$Codes = 'codes.txt')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = $Wt
$art = 'D:\BqMoni_Claude\p107\art'
New-Item -ItemType Directory -Force $art | Out-Null
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$codes = "$art\$Codes"
"build start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') wt=$wt tag=$Tag" | Out-File -Append $codes
Set-Location $wt
if (-not $SkipRestore) {
    & $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU `
        /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore_$Tag.log"
    "restore code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
}
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath="bin\Release_$Tag\" `
    /p:IntermediateOutputPath="obj\Release_$Tag\" /nologo /v:m *> "$art\build_app_$Tag.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
if ($code -ne 0) { Get-Content "$art\build_app_$Tag.log" | Select-String 'error' | Select-Object -First 20; exit $code }
Push-Location $wt
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "BecquerelMonitor\bin\Release_$Tag" `
    -Out "tools\effmaker\probes\build_$Tag" *> "$art\build_probes_$Tag.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
Pop-Location
if ($code -ne 0) { Get-Content "$art\build_probes_$Tag.log" | Select-String 'error|ОТКАЗ|⛔' | Select-Object -First 20 }
"app exe sha256 $((Get-FileHash "$wt\BecquerelMonitor\bin\Release_$Tag\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append $codes
if (Test-Path "$wt\tools\effmaker\probes\build_$Tag\BecquerelMonitor.exe") {
    "probes exe sha256 $((Get-FileHash "$wt\tools\effmaker\probes\build_$Tag\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append $codes
}
"build end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
Get-Content $codes | Select-Object -Last 6
exit $code
