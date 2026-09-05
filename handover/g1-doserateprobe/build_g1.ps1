# Полоса G1 (06.09.2026): сборка приложения в bin\Debug_G1 и ДВУХ проб —
# DoseRateProbe и DoseCoefProbeO2 — вручную тем же csc, что build_all.ps1,
# в tools\effmaker\probes\build_g1. build_all.ps1 не зовётся: он компилирует
# все пробы дерева разом, а в дереве идут чужие полосы.
#
#   pwsh handover\g1-doserateprobe\build_g1.ps1 [-NoApp]
#
# Каталог проб обставляется как у build_all.ps1: приложение, dll, три базы,
# runtimes\, ru\, поставочный config\ целиком, <проба>.exe.config. Даты
# скопированных файлов ставятся текущим временем (Copy-Item переносит дату
# источника, и сторож свежести по времени такого не видит).
param([switch]$NoApp)
$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
if (-not $NoApp) {
    & $msbuild 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
        /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_G1\' /p:IntermediateOutputPath='obj\G1\' `
        /v:m /nologo 2>&1 | Select-Object -Last 3
    if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
    "MSBUILD EXIT=0"
}

$Bin = [IO.Path]::GetFullPath('BecquerelMonitor\bin\Debug_G1')
$Out = [IO.Path]::GetFullPath('tools\effmaker\probes\build_g1')
New-Item -ItemType Directory -Force $Out | Out-Null

# --- обстановка каталога проб ---------------------------------------------
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe" -Destination "$Out\BecquerelMonitor.exe" -Force
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.pdb" -Destination "$Out\BecquerelMonitor.pdb" -Force -ErrorAction SilentlyContinue
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) { "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11 }
Get-ChildItem -LiteralPath $Bin -File -Filter '*.dll'    | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Force }
Get-ChildItem -LiteralPath $Bin -File -Filter '*.sqlite' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Force }
foreach ($d in @('runtimes', 'ru', 'config')) {
    if (-not (Test-Path -LiteralPath "$Bin\$d")) { "В $Bin НЕТ КАТАЛОГА $d"; exit 11 }
    robocopy "$Bin\$d" "$Out\$d" /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { "ROBOCOPY $d EXIT=$LASTEXITCODE"; exit 11 }
}
$probes = @('DoseRateProbe', 'DoseCoefProbeO2')
foreach ($p in $probes) { Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\$p.exe.config" -Force }
$now = Get-Date
Get-ChildItem -LiteralPath $Out -Recurse -File | ForEach-Object { $_.LastWriteTime = $now }
foreach ($must in @('BecquerelMonitor.exe', 'Microsoft.Data.Sqlite.dll', 'SQLitePCLRaw.core.dll', 'SpecUtilsNet.dll',
                    'matdb.sqlite', 'nucdb.sqlite', 'schemedb.sqlite',
                    'runtimes\win-x64\native\e_sqlite3.dll', 'ru\BecquerelMonitor.resources.dll',
                    'config\BecquerelMonitor.xml', 'config\NuclideDefinition.xml', 'config\device', 'config\ROI')) {
    if (-not (Test-Path -LiteralPath "$Out\$must")) { "В КАТАЛОГЕ ПРОБ НЕТ $must"; exit 11 }
}
"КАТАЛОГ ПРОБ ОБСТАВЛЕН: $Out"

# --- компиляция двух проб ---------------------------------------------------
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$Bin\BecquerelMonitor.exe", '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
          '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
          "/r:$Bin\Microsoft.Data.Sqlite.dll", "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll", "/r:$facades\netstandard.dll")
. tools\CORPUS\scripts\appwd_plan.ps1
$sources = @(Get-AppWdProbeSources -Repo (Get-Location).Path)
$companions = @($sources | Where-Object { -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet) } | ForEach-Object { $_.FullName })
"довески без Main: " + (($companions | ForEach-Object { [IO.Path]::GetFileName($_) }) -join ', ')
foreach ($p in $probes) {
    $src = (Resolve-Path "tools\effmaker\probes\$p.cs").Path
    $exe = "$Out\$p.exe"
    & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$exe" @refs $src @companions 2>&1
    if ($LASTEXITCODE -ne 0) { "CSC $p EXIT=$LASTEXITCODE"; exit 12 }
    $srcTime = (Get-Item -LiteralPath $src).LastWriteTime
    $exeTime = (Get-Item -LiteralPath $exe).LastWriteTime
    if ($exeTime -le $srcTime) { "$p.exe ($exeTime) НЕ СВЕЖЕЕ исходника ($srcTime)"; exit 13 }
    "CSC $p EXIT=0  exe $exeTime  >  cs $srcTime"
}
"СБОРКА G1 ПРОШЛА"
