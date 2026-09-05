# Полоса F43, строки `E17` (сетка и клеймо) и `E19` (проба «воздух» молча).
# ⛔ `build_all.ps1` НЕ трогается — его правит полоса F40; собирается ТОЛЬКО своя проба.
# ⛔ Приложение рядом с пробой и сама проба собираются ОДНИМ движением (`T233`).
#   -Out задаёт каталог проб: build_f43_before — до правки, build_f43 — после.
param([switch]$SkipApp, [string]$OutDir = 'tools\effmaker\probes\build_f43')
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
# ⛔ `Set-Location` НЕ меняет текущий каталог ПРОЦЕССА, а `[IO.Path]::GetFullPath`
#    смотрит именно на него (грабля полосы F20).
[Environment]::CurrentDirectory = $repo

if (-not $SkipApp) {
  & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
    'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_F43\' `
    /p:IntermediateOutputPath='obj\F43\' /v:m /nologo 2>&1 | Select-Object -Last 10
  if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
  "MSBUILD EXIT=0"
}

$Bin = [IO.Path]::GetFullPath('BecquerelMonitor\bin\Debug_F43')
$Out = [IO.Path]::GetFullPath($OutDir)
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'

if (-not (Test-Path $Out)) {
  New-Item -ItemType Directory -Path $Out | Out-Null
  $src = $null
  foreach ($c in 'tools\effmaker\probes\build_f35','tools\effmaker\probes\build_f34','tools\effmaker\probes\build') {
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

Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe" -Destination "$Out\BecquerelMonitor.exe" -Force
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.pdb" -Destination "$Out\BecquerelMonitor.pdb" -Force -ErrorAction SilentlyContinue
# ⚠ `Copy-Item` переносит ДАТУ источника — дату ставим руками, иначе сборка проб
#   решит, что цель свежее исходника, и не пересоберёт.
(Get-Item "$Out\BecquerelMonitor.exe").LastWriteTime = Get-Date
if (Test-Path "$Bin\ru\BecquerelMonitor.resources.dll") {
  New-Item -ItemType Directory -Path "$Out\ru" -Force | Out-Null
  Copy-Item -LiteralPath "$Bin\ru\BecquerelMonitor.resources.dll" -Destination "$Out\ru\" -Force
}
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) {
  "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11
}
"OUT=$Out"
"APP SHA=" + (Get-FileHash "$Out\BecquerelMonitor.exe").Hash.Substring(0,16)
"APP MTIME=" + (Get-Item "$Out\BecquerelMonitor.exe").LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')

$refs = @("/r:$Bin\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll',
          '/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Bin\Microsoft.Data.Sqlite.dll",
          "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll")

foreach ($p in 'EffAirGridProbeF43') {
  $srcFile = (Resolve-Path "tools\effmaker\probes\$p.cs").Path
  & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\$p.exe" @refs $srcFile 2>&1
  if ($LASTEXITCODE -ne 0) { "CSC $p EXIT=$LASTEXITCODE"; exit 12 }
  # ⛔ `<проба>.exe.config` ОБЯЗАТЕЛЕН (`T32`): без перенаправлений версий
  #    первое обращение к базе падает `FileLoadException: SQLitePCLRaw.core`.
  Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\$p.exe.config" -Force
  "CSC $p EXIT=0  mtime=" + (Get-Item "$Out\$p.exe").LastWriteTime.ToString('HH:mm:ss')
}
"BUILD OK"
