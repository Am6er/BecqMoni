# Сборка приёмки `N9` — `EstarCheck.cs`. Проекта у неё нет, как и у проб
# effmaker; собирается тем же csc и по тем же граблям.
#
#   pwsh tools\estar\build_check.ps1 [-Bin <каталог сборки приложения>]
#                                    [-Out <куда класть exe>]
#
# Умолчания: -Bin BecquerelMonitor\bin\Debug_Codex, -Out tools\estar\build.
#
# ⚠ Рядом с exe кладутся exe.config (без него Microsoft.Data.Sqlite валится
# TypeInitializationException — редирект SQLitePCLRaw.core живёт только в
# конфиге), все dll, три *.sqlite и нативный runtimes\win-x64\native\
# e_sqlite3.dll. Тот же список, что у tools\effmaker\probes\build_all.ps1, и
# по той же причине: чистый каталог даёт exe, который собирается и умирает на
# первом обращении к базе.
param(
    [string]$Bin = "",
    [string]$Out = ""
)
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Bin) { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
if (-not $Out) { $Out = Join-Path $PSScriptRoot 'build' }
if (-not (Test-Path (Join-Path $Bin 'BecquerelMonitor.exe'))) {
    Write-Host "нет $Bin\BecquerelMonitor.exe — сначала соберите приложение"
    exit 2
}
New-Item -ItemType Directory -Force $Out | Out-Null

$flat = @('BecquerelMonitor.exe', 'BecquerelMonitor.pdb', 'BecquerelMonitor.exe.config',
          '*.dll', '*.sqlite')
foreach ($mask in $flat) {
    Get-ChildItem (Join-Path $Bin $mask) -File -ErrorAction SilentlyContinue |
        Copy-Item -Destination $Out -Force
}
$rtSrc = Join-Path $Bin 'runtimes'
if (Test-Path $rtSrc) { Copy-Item $rtSrc $Out -Recurse -Force }

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @(
    "/r:$Bin\BecquerelMonitor.exe",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
    "/r:$Bin\Microsoft.Data.Sqlite.dll",
    "/r:$facades\netstandard.dll"
)

$src = Join-Path $PSScriptRoot 'EstarCheck.cs'
$exe = Join-Path $Out 'EstarCheck.exe'
$log = & $csc /nologo /target:exe /langversion:7.3 "/out:$exe" @refs $src 2>&1
if ($LASTEXITCODE -ne 0) {
    $log | Select-Object -First 12 | ForEach-Object { Write-Host "    $_" }
    Write-Host "СЛОМАНО: EstarCheck.cs"
    exit 1
}
$appConfig = Join-Path $Out 'BecquerelMonitor.exe.config'
if (Test-Path $appConfig) { Copy-Item $appConfig "$exe.config" -Force }
Write-Host "ok   EstarCheck.cs -> $exe"
