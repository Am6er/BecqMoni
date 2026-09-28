# П164 — сборка дерева: Restore + Release (bin\Release_<tag>, obj\<tag>) + build_all -Out build_<tag>.
# Коды — art\codes.txt (A77: смотреть КОД). MSBuild только из PowerShell; GenerateManifests=false (T75).
param([Parameter(Mandatory)][string]$Wt, [Parameter(Mandatory)][string]$Tag, [switch]$SkipRestore, [switch]$AppOnly)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$art = 'D:\BqMoni_Claude\p164\art'
New-Item -ItemType Directory -Force $art | Out-Null
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$codes = "$art\codes.txt"
"build start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') wt=$Wt tag=$Tag head=$(git -C $Wt rev-parse --short HEAD) dirty=$((git -C $Wt status --short -- BecquerelMonitor tools/effmaker/probes | Measure-Object).Count)" | Out-File -Append $codes
Set-Location $Wt
if (-not $SkipRestore) {
    & $msbuild "$Wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU `
        /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore_$Tag.log"
    "restore code=$LASTEXITCODE" | Out-File -Append $codes
}
& $msbuild "$Wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath="bin\Release_$Tag\" `
    /p:IntermediateOutputPath="obj\$Tag\" /nologo /v:m *> "$art\build_app_$Tag.log"
$code = $LASTEXITCODE
"app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
if ($code -ne 0) { Get-Content "$art\build_app_$Tag.log" | Select-String 'error' | Select-Object -First 20; exit $code }
"app exe sha256 $((Get-FileHash "$Wt\BecquerelMonitor\bin\Release_$Tag\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append $codes
if ($AppOnly) { Get-Content $codes | Select-Object -Last 3; exit 0 }
Push-Location $Wt
pwsh -File "$Wt\tools\effmaker\probes\build_all.ps1" -Bin "BecquerelMonitor\bin\Release_$Tag" `
    -Out "tools\effmaker\probes\build_$Tag" *> "$art\build_probes_$Tag.log"
$code = $LASTEXITCODE
"probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
Pop-Location
if ($code -ne 0) { Get-Content "$art\build_probes_$Tag.log" | Select-String 'error|ОТКАЗ|⛔' | Select-Object -First 20 }
if (Test-Path "$Wt\tools\effmaker\probes\build_$Tag\BecquerelMonitor.exe") {
    "probes exe sha256 $((Get-FileHash "$Wt\tools\effmaker\probes\build_$Tag\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append $codes
}
Get-Content $codes | Select-Object -Last 5
exit $code
