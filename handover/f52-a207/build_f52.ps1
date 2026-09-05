# Полоса F52, `A207` — отсутствующее время начала набора.
#
# ⛔ ДВА ПЛЕЧА СОБИРАЮТСЯ ИЗ СРЕЗОВ ДЕРЕВА, А НЕ ИЗ РАБОЧЕГО КАТАЛОГА. 06.09.2026
#    в дереве одновременно работали полосы F50 (`MainForm.cs`) и F51
#    (`FsaCompositionInference.cs`); собери я плечо «после» из рабочего дерева, а
#    «до» из HEAD — разница между плечами включала бы чужие незаконченные правки,
#    и приговор о МОЕЙ правке был бы смесью. Поэтому:
#      before = `git archive HEAD` (чистая вершина `fe5ba764`);
#      after  = тот же срез + РОВНО ШЕСТЬ моих файлов.
#
# ⛔ `build_all.ps1` НЕ ЗАПУСКАЕТСЯ: сплошная сборка проб упала бы на чужом
#    незаконченном `.cs` соседней полосы. Собирается одна своя проба тем же
#    `csc`, каким её собирает `build_all.ps1`, и с теми же довесками без `Main`.
#
# ⚠ Каталог пробы — ПОЛНАЯ копия каталога сборки приложения: там уже лежат
#    зависимости NuGet, три `*.sqlite`, `runtimes\win-x64\native` и сателлит
#    `ru`, то есть грабля `T45` (проба собирается и умирает на первом обращении
#    к базе) обойдена целиком, а не поимённым копированием.
param(
  [Parameter(Mandatory=$true)][string]$SrcRoot,   # корень среза (…\before или …\after)
  [Parameter(Mandatory=$true)][string]$BinName,   # имя OutputPath, напр. Debug_F52_before
  [Parameter(Mandatory=$true)][string]$Out        # куда класть пробу
)
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
# ⛔ `Set-Location` НЕ меняет текущий каталог ПРОЦЕССА (грабля полосы F20).
[Environment]::CurrentDirectory = $repo

$Bin = Join-Path $SrcRoot "BecquerelMonitor\bin\$BinName"
if (-not (Test-Path "$Bin\BecquerelMonitor.exe")) { "НЕТ СБОРКИ: $Bin"; exit 10 }

if (Test-Path $Out) { Remove-Item -LiteralPath $Out -Recurse -Force }
New-Item -ItemType Directory -Path $Out | Out-Null
Get-ChildItem -LiteralPath $Bin -Force |
  ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Recurse -Force }

# ⚠ `Copy-Item` переносит ДАТУ источника — дату ставим руками, иначе «свежее»
#   и «правильное» перестают быть одним и тем же (`T233`).
(Get-Item "$Out\BecquerelMonitor.exe").LastWriteTime = Get-Date
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) {
  "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11
}
"APP DIR=$Bin"
"APP SHA=" + (Get-FileHash "$Out\BecquerelMonitor.exe").Hash.Substring(0,16)

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$Out\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll',
          '/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Out\Microsoft.Data.Sqlite.dll",
          "/r:$Out\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll")

$src = (Resolve-Path 'tools\effmaker\probes\StartTimeProbeF52.cs').Path
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\StartTimeProbeF52.exe" @refs $src 2>&1
if ($LASTEXITCODE -ne 0) { "CSC EXIT=$LASTEXITCODE"; exit 12 }
# ⛔ `<проба>.exe.config` ОБЯЗАТЕЛЕН (`T32`).
Copy-Item -LiteralPath "$Out\BecquerelMonitor.exe.config" -Destination "$Out\StartTimeProbeF52.exe.config" -Force
"CSC EXIT=0"
"BUILD OK"
