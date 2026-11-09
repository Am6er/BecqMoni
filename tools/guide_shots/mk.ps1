# Сборка конвейера снимков гида (`AMBER319`): программа в СВОИ каталоги и две пробы.
# Зовёт `shoot.ps1 -Build`; руками — так же, оператором вызова:
#
#   & 'tools\guide_shots\mk.ps1' [-Work 'D:\BqMoni_Claude\guideshot'] [-SkipApp]
#
# 1. MSBuild приложения (Debug) в bin\guideshot и obj\guideshot — каталоги конвейера, не IDE;
#    -SkipApp — взять уже собранное оттуда.
# 2. csc: <Work>\tools\GuideShot.exe (против свежего BecquerelMonitor.exe) и HiddenRun.exe.
# Код возврата — первого отказавшего шага; лог MSBuild — <Work>\msbuild.log.
param(
    [string]$Work = 'D:\BqMoni_Claude\guideshot',
    [switch]$SkipApp
)
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$proj = Join-Path $repo 'BecquerelMonitor\BecquerelMonitor.csproj'
$bin = Join-Path $repo 'BecquerelMonitor\bin\guideshot'
$tools = Join-Path $Work 'tools'
New-Item -ItemType Directory -Force $Work, $tools | Out-Null

if (-not $SkipApp) {
    $msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
    # ⛔ /p:GenerateManifests=false и свой IntermediateOutputPath обязательны (CLAUDE.md, `T75`).
    & $msbuild $proj /t:Build /restore /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false `
        /p:GenerateManifests=false '/p:OutputPath=bin\guideshot\' '/p:IntermediateOutputPath=obj\guideshot\' `
        /v:minimal /nologo *> (Join-Path $Work 'msbuild.log')
    if ($LASTEXITCODE -ne 0) {
        Write-Host ("MSBuild: код {0}, лог {1}" -f $LASTEXITCODE, (Join-Path $Work 'msbuild.log')) -ForegroundColor Red
        exit 10
    }
    Write-Host 'MSBuild: код 0'
}
if (-not (Test-Path -LiteralPath (Join-Path $bin 'BecquerelMonitor.exe'))) {
    Write-Host "нет сборки: $bin" -ForegroundColor Red
    exit 11
}

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @(
    "/r:$bin\BecquerelMonitor.exe", "/r:$bin\WeifenLuo.WinFormsUI.Docking.dll",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', "/r:$facades\netstandard.dll"
)
# ⛔ /subsystemversion:6.00 — как у BecquerelMonitor.exe. С умолчанием csc (4.0) Windows даёт
# процессу другие метрики рамки и масштаб окон: about и prefs-* выходили другого размера.
$out = & $csc /nologo /target:exe /subsystemversion:6.00 /langversion:7.3 "/out:$tools\GuideShot.exe" @refs (Join-Path $PSScriptRoot 'GuideShot.cs') 2>&1
if ($LASTEXITCODE -ne 0) { $out | Write-Host; Write-Host 'csc GuideShot: отказ' -ForegroundColor Red; exit 12 }
$out = & $csc /nologo /target:exe /langversion:7.3 "/out:$tools\HiddenRun.exe" (Join-Path $PSScriptRoot 'HiddenRun.cs') 2>&1
if ($LASTEXITCODE -ne 0) { $out | Write-Host; Write-Host 'csc HiddenRun: отказ' -ForegroundColor Red; exit 13 }
& "$tools\HiddenRun.exe" --self-test
if ($LASTEXITCODE -ne 0) { Write-Host 'HiddenRun --self-test: отказ' -ForegroundColor Red; exit 14 }
Write-Host "пробы: $tools\GuideShot.exe, $tools\HiddenRun.exe"
exit 0
