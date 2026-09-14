# Полоса F49, `A190` + `A228`.
#
#   & handover\f49-a190-a228\build_f49.ps1 -AppDir bin\Debug_F49 -ObjDir obj\F49\ -Wd tools\CORPUS\scripts\wd_f49
#
# ⛔ `build_all.ps1` НЕ ЗАПУСКАЕТСЯ: в дереве работают соседние полосы, и
#    сплошная сборка упала бы на чужом незаконченном `.cs`. Собираются ТОЛЬКО
#    три нужные пробы — тем же `csc` и теми же ссылками, что в `build_all.ps1`.
#
# ⛔ ПРИЛОЖЕНИЕ И ПРОБЫ РЯДОМ С НИМ СОБИРАЮТСЯ ОДНИМ ДВИЖЕНИЕМ (`T233`), и
#    рабочий каталог строится ТЕМ ЖЕ движением, которым проверяется: иначе
#    остаётся состояние «приложение свежее, пробы старые», и его ничем не видно.
#
# ⛔ Состав рабочего каталога — по `appwd_plan.ps1`: сборка + пробы + ПОСТАВОЧНАЯ
#    библиотека нуклидов + приборы КОРПУСА (поставочные `config\device\*.xml`
#    класть НЕЛЬЗЯ: `AtomSpectraVCP.xml` несёт тот же GUID, что корпусный
#    прибор, а два одинаковых GUID = модальное окно = зависание, `B6`).
param(
  [string]$AppDir = 'bin\Debug_F49',
  [string]$ObjDir = 'obj\F49\',
  [string]$Wd     = 'tools\CORPUS\scripts\wd_f49',
  [switch]$SkipApp
)
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
# ⛔ `Set-Location` НЕ меняет текущий каталог ПРОЦЕССА, а `[IO.Path]::GetFullPath`
#    смотрит именно на него (грабля полосы F20).
[Environment]::CurrentDirectory = $repo

if (-not $SkipApp) {
  & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
    'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath="$AppDir\" `
    /p:IntermediateOutputPath=$ObjDir /v:m /nologo 2>&1 | Select-Object -Last 8
  if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
  "MSBUILD EXIT=0"
}

$Bin = [IO.Path]::GetFullPath("BecquerelMonitor\$AppDir")
$Out = [IO.Path]::GetFullPath($Wd)
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'

New-Item -ItemType Directory -Force $Out | Out-Null

# 1. Сборка приложения: exe, конфиг, pdb, библиотеки, три базы.
Get-ChildItem -LiteralPath $Bin -File |
  Where-Object { $_.Extension -in '.dll', '.sqlite', '.pdb' -or $_.Name -in 'BecquerelMonitor.exe', 'BecquerelMonitor.exe.config' } |
  ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Force }
foreach ($d in 'ru', 'runtimes') {
  $src = Join-Path $Bin $d
  if (Test-Path $src) { Copy-Item -LiteralPath $src -Destination $Out -Recurse -Force }
}
# ⚠ `Copy-Item` переносит ДАТУ ИСТОЧНИКА — дату ставим руками, иначе сторож
#   свежести смотрел бы на время сборки приложения, а не копирования.
(Get-Item "$Out\BecquerelMonitor.exe").LastWriteTime = Get-Date
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) {
  "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11
}

# 2. Конфиг: поставочная библиотека нуклидов + приборы КОРПУСА.
New-Item -ItemType Directory -Force "$Out\config" | Out-Null
New-Item -ItemType Directory -Force "$Out\config\device" | Out-Null
foreach ($n in 'NuclideDefinition.xml', 'BecquerelMonitor.xml') {
  Copy-Item -LiteralPath "$repo\BecquerelMonitor\config\$n" -Destination "$Out\config\$n" -Force
}
Get-ChildItem "$repo\tools\CORPUS\corpus\devices\*.xml" -File |
  ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination "$Out\config\device" -Force }

"APP DIR=$Bin"
"APP SHA=" + (Get-FileHash "$Out\BecquerelMonitor.exe").Hash.Substring(0, 16)
"WD=$Out  приборов корпуса: " + (Get-ChildItem "$Out\config\device\*.xml" -File).Count

# 3. Пробы. Довески без `Main` идут КАЖДОЙ пробе — тем же правилом, что в
#    `build_all.ps1` (список, который надо помнить, однажды забывают).
$refs = @("/r:$Bin\BecquerelMonitor.exe", '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
          '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', "/r:$Bin\Microsoft.Data.Sqlite.dll",
          "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll", "/r:$facades\netstandard.dll")
$all = @(Get-ChildItem "$repo\tools\effmaker\probes\*.cs" -File)
$companions = @($all | Where-Object {
  -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet)
} | ForEach-Object { $_.FullName })

foreach ($p in 'LabelPathProbeF49', 'BqActivityProbe', 'LabelTruthProbe') {
  $srcFile = (Resolve-Path "tools\effmaker\probes\$p.cs").Path
  & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\$p.exe" @refs $srcFile @companions 2>&1 |
    Select-Object -First 6
  if ($LASTEXITCODE -ne 0) { "CSC $p EXIT=$LASTEXITCODE"; exit 12 }
  # ⛔ `<проба>.exe.config` ОБЯЗАТЕЛЕН (`T32`): без него binding redirect
  #    SQLitePCLRaw не применяется и первое же чтение базы падает.
  Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\$p.exe.config" -Force
  "CSC $p EXIT=0  mtime=" + (Get-Item "$Out\$p.exe").LastWriteTime.ToString('HH:mm:ss')
}
"BUILD OK"
