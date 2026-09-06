# Полоса G9 (`A255`): сборка приложения в `bin\Debug_G9` и СВОЕЙ пробы
# `PeakHighlightProbeG9` вручную тем же `csc`, каким собирает `build_all.ps1`
# (образец — `handover/f16-a246-a247/build_f16.ps1`).
#
# ⛔ `build_all.ps1` НЕ ЗОВЁТСЯ нарочно: он компилирует ВСЕ исходники проб разом,
#    а в `tools\effmaker\probes` прямо сейчас правят файлы соседние полосы
#    (F56, F58, F59, G8), и их незаконченный `.cs` уронил бы сборку этой.
#
# ⛔ Приложение рядом с пробой кладётся ТЕМ ЖЕ движением и сверяется по sha256:
#    «положить файл мимо сторожа» — ровно то, чем `A77` стоила трёх часов счёта.
#
#   -Bin  каталог собранного приложения (умолчание bin\Debug_G9 этого дерева;
#         для плеча «до» — bin\Debug_G9ref опорного дерева bq_g9_ref)
#   -Out  каталог пробы
#   -NoApp  не пересобирать приложение (для плеча «до» оно собрано отдельно)
param(
    [string]$Bin = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\bin\Debug_G9',
    [string]$Out = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\build_g9',
    [switch]$NoApp
)
$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'

if (-not $NoApp) {
    & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
      'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
      /p:SignManifests=false /p:GenerateManifests=false `
      /p:OutputPath='bin\Debug_G9\' /p:IntermediateOutputPath='obj\G9\' /v:m /nologo 2>&1 |
      Select-Object -Last 3
    if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
    "MSBUILD EXIT=0"
}

$Bin = [IO.Path]::GetFullPath($Bin)
$Out = [IO.Path]::GetFullPath($Out)
$Src = [IO.Path]::GetFullPath('tools\effmaker\probes\build')
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# Довески NuGet, родные библиотеки и три базы — из рабочего каталога проб
# (`T45`: без них проба собирается и умирает на первом же обращении к базе).
foreach ($n in 'Microsoft.Data.Sqlite.dll','SQLitePCLRaw.batteries_v2.dll','SQLitePCLRaw.core.dll',
               'SQLitePCLRaw.provider.dynamic_cdecl.dll','SQLitePCLRaw.provider.e_sqlite3.dll',
               'SpecUtilsNet.dll','MathNet.Numerics.dll','System.Buffers.dll','System.IO.Ports.dll',
               'System.Memory.dll','System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll',
               'InTheHand.BluetoothLE.dll','InTheHand.Net.Bluetooth.dll',
               'WeifenLuo.WinFormsUI.Docking.dll','WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll',
               'matdb.sqlite','nucdb.sqlite','schemedb.sqlite') {
    Copy-Item -LiteralPath (Join-Path $Src $n) -Destination (Join-Path $Out $n) -Force
}
Copy-Item -LiteralPath (Join-Path $Src 'runtimes') -Destination $Out -Recurse -Force
Copy-Item -LiteralPath (Join-Path $Bin 'ru') -Destination $Out -Recurse -Force

# Настройка прогона: приборы корпуса и библиотека нуклидов — из готовой оснастки
# `wd_b15` (поставочные `config\device` брать НЕЛЬЗЯ: у них тот же GUID, что у
# корпусного прибора, а два GUID = модальное окно = зависший безоконный прогон).
Copy-Item -LiteralPath 'tools\CORPUS\scripts\wd_b15\config' -Destination $Out -Recurse -Force

Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe" -Destination "$Out\BecquerelMonitor.exe" -Force
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.pdb" -Destination "$Out\BecquerelMonitor.pdb" -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\BecquerelMonitor.exe.config" -Force -ErrorAction SilentlyContinue
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) {
    "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11
}
"ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ: sha256 сошлись ($((Get-FileHash "$Out\BecquerelMonitor.exe").Hash.Substring(0,16))…)"

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$Bin\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll',
          '/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Bin\Microsoft.Data.Sqlite.dll",
          "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll")
$companion = (Resolve-Path 'tools\effmaker\probes\ProbeDeviceConfig.cs').Path
$target = (Resolve-Path 'tools\effmaker\probes\_TargetFramework.cs').Path
$mine = (Resolve-Path 'tools\effmaker\probes\PeakHighlightProbeG9.cs').Path
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\PeakHighlightProbeG9.exe" @refs $mine $companion $target 2>&1
if ($LASTEXITCODE -ne 0) { "CSC EXIT=$LASTEXITCODE"; exit 12 }
"CSC EXIT=0"
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\PeakHighlightProbeG9.exe.config" -Force -ErrorAction SilentlyContinue
$exe = Get-Item "$Out\PeakHighlightProbeG9.exe"
$src = Get-Item $mine
if ($exe.LastWriteTimeUtc -lt $src.LastWriteTimeUtc) { "EXE СТАРШЕ ИСХОДНИКА"; exit 13 }
"проба свежее исходника: $($exe.LastWriteTimeUtc.ToString('o')) >= $($src.LastWriteTimeUtc.ToString('o'))"
