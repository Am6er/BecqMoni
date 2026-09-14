# П72 14.09.2026 (T258/T259) — сборка стенда из worktree D:\BqMoni_Claude\p72\<wt> (311c98b0 или с копией правки).
# Свежему worktree нужен /t:Restore ДО /t:Build (CS0246 — грабля П64). Release_<tag> + obj\Release_<tag> + пробы build_all -Out build_<tag>.
# Коды возврата — в codes.txt (A77: смотреть КОД, а не факт запуска). MSBuild — только из PowerShell; /p:GenerateManifests=false обязателен (T75).
# Вызов: pwsh -File build_p72.ps1 -Wt D:\BqMoni_Claude\p72\wt -Tag p72
param([string]$Wt = 'D:\BqMoni_Claude\p72\wt', [string]$Tag = 'p72')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$art = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p72-t258-t259'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"build $Tag start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') head=$(git -C $Wt rev-parse --short HEAD)" | Out-File -Append "$art\codes.txt"
Set-Location $Wt
& $msbuild "$Wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$art\build_restore_$Tag.log"
$code = $LASTEXITCODE
"$Tag restore code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
& $msbuild "$Wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath="bin\Release_$Tag\" `
    /p:IntermediateOutputPath="obj\Release_$Tag\" /nologo /v:m *> "$art\build_app_$Tag.log"
$code = $LASTEXITCODE
"$Tag app code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
pwsh -File "$Wt\tools\effmaker\probes\build_all.ps1" -Bin "BecquerelMonitor\bin\Release_$Tag" `
    -Out "tools\effmaker\probes\build_$Tag" *> "$art\build_probes_$Tag.log"
$code = $LASTEXITCODE
"$Tag probes code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"$Tag app exe sha256 $((Get-FileHash "$Wt\BecquerelMonitor\bin\Release_$Tag\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"$Tag probes exe sha256 $((Get-FileHash "$Wt\tools\effmaker\probes\build_$Tag\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"build $Tag end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
exit $code
