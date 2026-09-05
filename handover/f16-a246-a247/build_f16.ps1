# Полоса F16 (`A246`/`A247`): сборка приложения в `bin\Debug_F16` и СВОЕЙ пробы
# `FsaSelectionProbeF16` вручную тем же `csc`, каким собирает `build_all.ps1`.
#
# ⛔ `build_all.ps1` НЕ ЗОВЁТСЯ нарочно: он компилирует ВСЕ исходники проб разом,
#    а в `tools\effmaker\probes` прямо сейчас правят файлы соседние полосы, и их
#    незаконченный `.cs` уронил бы сборку этой (грабля `T199`/`T194`).
#
# ⛔ Приложение рядом с пробой кладётся ТЕМ ЖЕ движением и сверяется по sha256:
#    «положить файл мимо сторожа» — ровно то, чем `A77` стоила трёх часов счёта.
param([string]$Tag = 'run')
$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'

& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
  /p:SignManifests=false /p:GenerateManifests=false `
  /p:OutputPath='bin\Debug_F16\' /p:IntermediateOutputPath='obj\F16\' /v:m /nologo 2>&1 |
  Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
"MSBUILD EXIT=0"

$Bin = [IO.Path]::GetFullPath('BecquerelMonitor\bin\Debug_F16')
$Out = [IO.Path]::GetFullPath('tools\effmaker\probes\build_f16')
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
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) {
    "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11
}
"ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ: sha256 сошлись"

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$Bin\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll',
          '/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Bin\Microsoft.Data.Sqlite.dll",
          "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll")
$companion = (Resolve-Path 'tools\effmaker\probes\ProbeDeviceConfig.cs').Path
$mine = (Resolve-Path 'tools\effmaker\probes\FsaSelectionProbeF16.cs').Path
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\FsaSelectionProbeF16.exe" @refs $mine $companion 2>&1
if ($LASTEXITCODE -ne 0) { "CSC EXIT=$LASTEXITCODE"; exit 12 }
# `<проба>.exe.config` (`T32`): без перенаправлений сборок первое же обращение к
# базе падает `FileLoadException: SQLitePCLRaw.core … не соответствует ссылке`.
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" `
          -Destination "$Out\FsaSelectionProbeF16.exe.config" -Force
"CSC EXIT=0"
