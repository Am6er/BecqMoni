# Полоса F71 (`A260`): сборка ТРЁХ проб полосы вручную тем же
# `csc`, каким собирает `build_all.ps1` (образец — `handover/g10-a193-a195/build_g10.ps1`):
# `SelectionPanelProbeG10`, `N42RoundTripProbe`, `BqActivityProbe`.
#
# Зачем свой скрипт, когда есть `build_all.ps1`: плечо «ДО» собирается против
# `bin\Debug_F71ref` (то же дерево ДО правок полосы, собрано ПЕРВЫМ движением
# захода), а сторож свежести `build_all.ps1` такой каталог отвергает по
# устройству — приложение там СТАРШЕ исходников, и это ровно то, что нужно.
# Плечо «ПОСЛЕ» (`build_f66`) собирается штатным `build_all.ps1`.
#
# ⛔ Приложение рядом с пробой кладётся ТЕМ ЖЕ движением и сверяется по sha256
#    (`A77`: «положить файл мимо сторожа» стоило трёх часов счёта).
#
#   -Bin  каталог собранного приложения
#   -Out  каталог проб
param(
    [string]$Bin = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\bin\Debug_F71ref',
    [string]$Out = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\build_f71ref'
)
$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'

$Bin = [IO.Path]::GetFullPath($Bin)
$Out = [IO.Path]::GetFullPath($Out)
$Src = [IO.Path]::GetFullPath('tools\effmaker\probes\build')
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# Довески NuGet, родные библиотеки и три базы — из рабочего каталога проб (`T45`).
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
if (Test-Path (Join-Path $Out 'ru')) { Remove-Item -Recurse -Force (Join-Path $Out 'ru') }
Copy-Item -LiteralPath (Join-Path $Bin 'ru') -Destination $Out -Recurse -Force

# Приборы корпуса и библиотека нуклидов — из готовой оснастки `wd_b15`
# (поставочные `config\device` брать НЕЛЬЗЯ: тот же GUID, что у корпусного
# прибора, два GUID = модальное окно = зависший безоконный прогон).
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
# Довески без Main — все четыре, как их кладёт `build_all.ps1` каждой пробе.
$extra = @('ProbeDeviceConfig.cs','_TargetFramework.cs','GadrasDetector.cs','ResidualScan.cs' |
           ForEach-Object { (Resolve-Path "tools\effmaker\probes\$_").Path })

foreach ($name in 'N42RoundTripProbe','FwhmVoiceProbeF54') {
    $mine = (Resolve-Path "tools\effmaker\probes\$name.cs").Path
    & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\$name.exe" @refs $mine @extra 2>&1
    if ($LASTEXITCODE -ne 0) { "CSC $name EXIT=$LASTEXITCODE"; exit 12 }
    Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\$name.exe.config" -Force -ErrorAction SilentlyContinue
    $exe = Get-Item "$Out\$name.exe"
    $src = Get-Item $mine
    if ($exe.LastWriteTimeUtc -lt $src.LastWriteTimeUtc) { "${name}: EXE СТАРШЕ ИСХОДНИКА"; exit 13 }
    "CSC $name EXIT=0; проба свежее исходника: $($exe.LastWriteTimeUtc.ToString('o')) >= $($src.LastWriteTimeUtc.ToString('o'))"
}
