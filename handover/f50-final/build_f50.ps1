# Полоса F50, ФИНАЛ строки `A244` — снятие костыля `MainForm.cs:158-160`.
# ⛔ Приложение и пробы рядом с ним собираются ОДНИМ движением (`T233`).
# ⛔ `build_all.ps1` НЕ ЗАПУСКАТЬ: в дереве живут соседние полосы.
param(
  [string]$AppDir   = 'bin\Debug_F50',
  [string]$ObjDir   = 'obj\F50\',
  [string]$ProbeDir = 'tools\effmaker\probes\build_f50',
  [switch]$SkipApp
)
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
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
$Out = [IO.Path]::GetFullPath($ProbeDir)
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'

if (-not (Test-Path $Out)) {
  New-Item -ItemType Directory -Path $Out | Out-Null
  # ⛔ `T45`: свежий каталог даёт пробы, которые собираются и умирают.
  $src = $null
  foreach ($c in 'tools\effmaker\probes\build','tools\effmaker\probes\build_f47') {
    if (Test-Path $c) { $src = [IO.Path]::GetFullPath($c); break }
  }
  if (-not $src) { "НЕТ КАТАЛОГА-ОБРАЗЦА С ЗАВИСИМОСТЯМИ"; exit 13 }
  Get-ChildItem -LiteralPath $src -File | Where-Object { $_.Extension -in '.dll','.sqlite' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Force }
  foreach ($d in 'ru','runtimes','config') {
    if (Test-Path (Join-Path $src $d)) { Copy-Item -LiteralPath (Join-Path $src $d) -Destination $Out -Recurse -Force }
  }
  Copy-Item -LiteralPath "$src\BecquerelMonitor.exe.config" -Destination $Out -Force -ErrorAction SilentlyContinue
}

# Каталог `config` нужен пробам, поднимающим менеджеров-одиночек (O14, F35, F27).
# ⛔ Только ЧТЕНИЕ поставочных конфигов — копия рядом с пробой, оригиналы не трогаются.
if (-not (Test-Path "$Out\config")) {
  Copy-Item -LiteralPath 'tools\effmaker\probes\build\config' -Destination $Out -Recurse -Force
}
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe" -Destination "$Out\BecquerelMonitor.exe" -Force
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.pdb" -Destination "$Out\BecquerelMonitor.pdb" -Force -ErrorAction SilentlyContinue
# ⚠ `Copy-Item` переносит ДАТУ источника — дату ставим руками (`T233`, F27).
(Get-Item "$Out\BecquerelMonitor.exe").LastWriteTime = Get-Date
if (Test-Path "$Bin\ru\BecquerelMonitor.resources.dll") {
  New-Item -ItemType Directory -Path "$Out\ru" -Force | Out-Null
  Copy-Item -LiteralPath "$Bin\ru\BecquerelMonitor.resources.dll" -Destination "$Out\ru\" -Force
}
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) {
  "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11
}
"APP DIR=$Bin"
"APP SHA=" + (Get-FileHash "$Out\BecquerelMonitor.exe").Hash.Substring(0,16)

$refs = @("/r:$Bin\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll',
          '/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Bin\Microsoft.Data.Sqlite.dll",
          "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll")

foreach ($p in 'CultureProbeO14','RestCultureProbeF28','GraphCultureProbeF27',
               'CalibGraphProbeF35','GridStampProbeF45','RestCultureProbeF47') {
  $srcFile = (Resolve-Path "tools\effmaker\probes\$p.cs").Path
  & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\$p.exe" @refs $srcFile 2>&1
  if ($LASTEXITCODE -ne 0) { "CSC $p EXIT=$LASTEXITCODE"; exit 12 }
  # ⛔ `<проба>.exe.config` ОБЯЗАТЕЛЕН (`T32`).
  Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\$p.exe.config" -Force
  "CSC $p EXIT=0  mtime=" + (Get-Item "$Out\$p.exe").LastWriteTime.ToString('HH:mm:ss')
}
"BUILD OK"
