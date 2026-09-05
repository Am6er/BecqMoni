# Полоса F23 (`A212`, встречная приёмка): сборка приложения и СВОЕЙ пробы
# `ImportEmptyConfigProbeF23` вручную тем же `csc`, каким собирает `build_all.ps1`.
#
# ⛔ `build_all.ps1` НЕ ЗОВЁТСЯ нарочно: он компилирует ВСЕ исходники проб разом,
#    а в `tools\effmaker\probes` прямо сейчас правят файлы полосы F22 и F24
#    (грабля `T199`/`T194`).
#
# ⛔ Приложение рядом с пробой кладётся ТЕМ ЖЕ движением и сверяется по sha256:
#    «положить файл мимо сторожа» — ровно то, чем `A77` стоила трёх часов счёта.
#
#   .\build_f23.ps1           — дерево как есть:      bin\Debug_F23     -> build_f23
#   .\build_f23.ps1 -Before   — со СНЯТЫМИ сторожами: bin\Debug_F23bef  -> build_f23_before
#
# Ключ `-Before` НИЧЕГО САМ НЕ ПРАВИТ: сторожи снимаются отдельным движением
# (`revert_guards.py`), собирается ЭТОТ каталог, и правка тут же откатывается.
param([switch]$Before)
$ErrorActionPreference = 'Continue'

$Main = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
if ($Before) {
    $OutName = 'bin\Debug_F23bef\'
    $ObjName = 'obj\F23bef\'
    $ProbeIn = 'build_f23_before'
} else {
    $OutName = 'bin\Debug_F23\'
    $ObjName = 'obj\F23\'
    $ProbeIn = 'build_f23'
}

Set-Location $Main
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
  /p:SignManifests=false /p:GenerateManifests=false `
  /p:OutputPath=$OutName /p:IntermediateOutputPath=$ObjName /v:m /nologo 2>&1 |
  Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
"MSBUILD EXIT=0"

$Bin = [IO.Path]::GetFullPath((Join-Path $Main ('BecquerelMonitor\' + $OutName.TrimEnd('\'))))
$Out = [IO.Path]::GetFullPath((Join-Path $Main ('tools\effmaker\probes\' + $ProbeIn)))
$Src = [IO.Path]::GetFullPath((Join-Path $Main 'tools\effmaker\probes\build'))
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
# `wd_b15`. ⛔ Поставочные `config\device` брать НЕЛЬЗЯ: у них тот же GUID, что у
# корпусного прибора, а два GUID = модальное окно = зависший безоконный прогон.
Copy-Item -LiteralPath (Join-Path $Main 'tools\CORPUS\scripts\wd_b15\config') -Destination $Out -Recurse -Force

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
# ⚠ Исходник пробы — ВСЕГДА один и тот же текст, и против снятых сторожей тоже:
#   иначе опорный кадр снимала бы ДРУГАЯ проба.
$mine = (Join-Path $Main 'tools\effmaker\probes\ImportEmptyConfigProbeF23.cs')
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\ImportEmptyConfigProbeF23.exe" @refs $mine 2>&1
if ($LASTEXITCODE -ne 0) { "CSC EXIT=$LASTEXITCODE"; exit 12 }
# `<проба>.exe.config` (`T32`): без перенаправлений сборок первое же обращение к
# базе падает `FileLoadException: SQLitePCLRaw.core … не соответствует ссылке`.
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" `
          -Destination "$Out\ImportEmptyConfigProbeF23.exe.config" -Force
"CSC EXIT=0"
