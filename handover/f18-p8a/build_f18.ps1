# Полоса F18 (доделка П8а): приложение в bin\Debug_F18, пробы — вручную тем же
# csc, что build_all.ps1, но ТОЛЬКО СВОИ (рядом правят файлы другие полосы,
# сплошная сборка проб упала бы на их незаконченном .cs — T199/T194).
param([switch]$SkipApp)
$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'

if (-not $SkipApp) {
  & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
    'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_F18\' `
    /p:IntermediateOutputPath='obj\F18\' /v:m /nologo 2>&1 | Select-Object -Last 3
  if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
  "MSBUILD EXIT=0"
}

$Bin = [IO.Path]::GetFullPath('BecquerelMonitor\bin\Debug_F18')
$Out = [IO.Path]::GetFullPath('tools\effmaker\probes\build_f18')
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'

if (-not (Test-Path $Out)) {
  # Обстановка (зависимости NuGet, три базы, сателлит ru, каталоги config) —
  # копией из каталога, собранного оборванной полосой; двоичные файлы проб из
  # него НЕ берём, свои собираются ниже заново.
  New-Item -ItemType Directory -Path $Out | Out-Null
  $src = [IO.Path]::GetFullPath('tools\effmaker\probes\build_p8a')
  Get-ChildItem -LiteralPath $src -File | Where-Object { $_.Extension -in '.dll','.sqlite' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Force }
  foreach ($d in 'ru','runtimes','config') {
    if (Test-Path (Join-Path $src $d)) { Copy-Item -LiteralPath (Join-Path $src $d) -Destination $Out -Recurse -Force }
  }
  Copy-Item -LiteralPath "$src\BecquerelMonitor.exe.config" -Destination $Out -Force -ErrorAction SilentlyContinue
  # Матрицы, оставшиеся от чужого прогона, не наши — вычистить.
  Get-ChildItem -Path (Join-Path $Out 'config\device\response') -Filter *.rmx -ErrorAction SilentlyContinue | Remove-Item -Force
}

# ⛔ Приложение рядом с пробой и пробы собираются ОДНИМ движением (T233):
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe" -Destination "$Out\BecquerelMonitor.exe" -Force
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.pdb" -Destination "$Out\BecquerelMonitor.pdb" -Force -ErrorAction SilentlyContinue
if (Test-Path "$Bin\ru\BecquerelMonitor.resources.dll") {
  New-Item -ItemType Directory -Path "$Out\ru" -Force | Out-Null
  Copy-Item -LiteralPath "$Bin\ru\BecquerelMonitor.resources.dll" -Destination "$Out\ru\" -Force
}
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) {
  "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11
}
"APP SHA=" + (Get-FileHash "$Out\BecquerelMonitor.exe").Hash.Substring(0,16)
"APP MTIME=" + (Get-Item "$Out\BecquerelMonitor.exe").LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
"RU  SHA=" + (Get-FileHash "$Out\ru\BecquerelMonitor.resources.dll").Hash.Substring(0,16)

$refs = @("/r:$Bin\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll',
          '/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Bin\Microsoft.Data.Sqlite.dll",
          "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll")

foreach ($p in 'MatrixRefusalProbeP8','ResponseMatrixFormProbe') {
  $srcFile = (Resolve-Path "tools\effmaker\probes\$p.cs").Path
  & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\$p.exe" @refs $srcFile 2>&1
  if ($LASTEXITCODE -ne 0) { "CSC $p EXIT=$LASTEXITCODE"; exit 12 }
  # ⛔ `<проба>.exe.config` ОБЯЗАТЕЛЕН (T32): в нём перенаправления версий
  #    сборок, без него `Microsoft.Data.Sqlite` не находит `SQLitePCLRaw.core`
  #    нужной версии и первое обращение к базе падает `FileLoadException`.
  Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\$p.exe.config" -Force
  "CSC $p EXIT=0  mtime=" + (Get-Item "$Out\$p.exe").LastWriteTime.ToString('HH:mm:ss')
}
"BUILD OK"
